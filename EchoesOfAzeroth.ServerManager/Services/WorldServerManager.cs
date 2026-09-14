using System.Diagnostics;
using EchoesOfAzeroth.ServerManager.Infrastructure.Windows;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class WorldServerManager : AzerothProcessManager
{
    private const string ReadyMarker = "WORLD: World Initialized";
    private TaskCompletionSource<bool>? _readySource;

    public WorldServerManager(AppSettings settings, PortMonitor portMonitor, LoggingService logger)
        : base(settings, portMonitor, logger)
    {
    }

    public bool CanSendCommands => Status == ComponentStatus.Running && IsOwned && GetCurrentProcess() is not null;

    protected override string ExecutableSetting => Settings.WorldServerExecutable;
    protected override string ArgumentsSetting => Settings.WorldServerArguments;
    protected override int PortSetting => Settings.WorldServerPort;
    protected override LogChannel LogChannel => Models.LogChannel.WorldServer;
    protected override string ComponentName => "WorldServer";

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = AttachExistingProcess();
            var external = process is not null && !IsOwned;
            SetStatus(ComponentStatus.Starting);

            if (process is null)
            {
                if (await PortMonitor.IsAcceptingConnectionsAsync(PortSetting, cancellationToken).ConfigureAwait(false))
                {
                    SetStatus(ComponentStatus.Error);
                    throw new InvalidOperationException($"Port {PortSetting} is already in use by another process.");
                }

                _readySource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process = StartOwnedProcess(true);
            }

            bool ready;
            if (external)
            {
                ready = await WaitForPortAsync(process, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ready = await WaitForReadyMarkerAsync(process, cancellationToken).ConfigureAwait(false);
                if (ready)
                {
                    SetPortOpen(await PortMonitor.IsAcceptingConnectionsAsync(PortSetting, cancellationToken).ConfigureAwait(false));
                }
            }

            if (!ready)
            {
                SetStatus(ComponentStatus.Error);
                if (!IsAlive(process))
                {
                    throw new InvalidOperationException($"WorldServer exited during startup with code {process.ExitCode}.");
                }

                var readiness = external ? $"port {PortSetting}" : $"output marker '{ReadyMarker}'";
                throw new TimeoutException($"WorldServer did not reach {readiness} within {Settings.StartupTimeoutSeconds} seconds.");
            }

            SetStatus(ComponentStatus.Running);
            Logger.Info(LogChannel.Manager, "WorldServer reported that the world is initialized.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(ComponentStatus.Error);
            Logger.Error(LogChannel.Manager, "WorldServer failed to start.", exception);
            throw;
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = GetCurrentProcess() ?? AttachExistingProcess();
            if (process is null)
            {
                CompleteStop();
                return;
            }

            SetStatus(ComponentStatus.Stopping);
            BeginExpectedStop();

            if (IsOwned)
            {
                Logger.Info(LogChannel.Manager, "Sending 'server shutdown 0' to WorldServer.");
                try
                {
                    await process.StandardInput.WriteLineAsync("server shutdown 0").ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    Logger.Error(LogChannel.Manager, "WorldServer rejected the shutdown command.", exception);
                }
            }
            else
            {
                Logger.Info(LogChannel.Manager, "WorldServer is external; requesting graceful console shutdown.");
                if (!ConsoleControlSignal.TrySendCtrlBreak(process.Id, out var error))
                {
                    Logger.Info(LogChannel.Manager, $"Console control signal was unavailable for WorldServer: {error}");
                    process.CloseMainWindow();
                }
            }

            if (!await WaitForExitAsync(process, TimeSpan.FromSeconds(Settings.GracefulShutdownTimeoutSeconds), cancellationToken).ConfigureAwait(false))
            {
                await ForceKillAsync(process, cancellationToken).ConfigureAwait(false);
            }

            CompleteStop();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(ComponentStatus.Error);
            Logger.Error(LogChannel.Manager, "WorldServer failed to stop.", exception);
            throw;
        }
        finally
        {
            OperationGate.Release();
        }
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        var normalized = command.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        var process = GetCurrentProcess();
        if (process is null || Status != ComponentStatus.Running)
        {
            throw new InvalidOperationException("WorldServer is not running.");
        }

        if (!IsOwned)
        {
            throw new InvalidOperationException("The external WorldServer process has no redirected standard input.");
        }

        await process.StandardInput.WriteLineAsync(normalized).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        Logger.Info(Models.LogChannel.WorldServer, $"> {normalized}");
    }

    protected override void OnOutputLine(string line, bool isError)
    {
        if (line.Contains(ReadyMarker, StringComparison.OrdinalIgnoreCase))
        {
            _readySource?.TrySetResult(true);
        }
    }

    private async Task<bool> WaitForReadyMarkerAsync(Process process, CancellationToken cancellationToken)
    {
        var markerTask = _readySource?.Task ?? Task.FromResult(false);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Settings.StartupTimeoutSeconds), cancellationToken);
        var completed = await Task.WhenAny(markerTask, exitTask, timeoutTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return completed == markerTask && await markerTask.ConfigureAwait(false);
    }
}

