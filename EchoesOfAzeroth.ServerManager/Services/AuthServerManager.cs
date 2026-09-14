using EchoesOfAzeroth.ServerManager.Infrastructure.Windows;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class AuthServerManager : AzerothProcessManager
{
    public AuthServerManager(AppSettings settings, PortMonitor portMonitor, LoggingService logger)
        : base(settings, portMonitor, logger)
    {
    }

    protected override string ExecutableSetting => Settings.AuthServerExecutable;
    protected override string ArgumentsSetting => Settings.AuthServerArguments;
    protected override int PortSetting => Settings.AuthServerPort;
    protected override LogChannel LogChannel => Models.LogChannel.AuthServer;
    protected override string ComponentName => "AuthServer";

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await OperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = AttachExistingProcess();
            if (process is null)
            {
                if (await PortMonitor.IsAcceptingConnectionsAsync(PortSetting, cancellationToken).ConfigureAwait(false))
                {
                    SetStatus(ComponentStatus.Error);
                    throw new InvalidOperationException($"Port {PortSetting} is already in use by another process.");
                }

                SetStatus(ComponentStatus.Starting);
                process = StartOwnedProcess(false);
            }
            else
            {
                SetStatus(ComponentStatus.Starting);
            }

            if (!await WaitForPortAsync(process, cancellationToken).ConfigureAwait(false))
            {
                SetStatus(ComponentStatus.Error);
                if (!IsAlive(process))
                {
                    throw new InvalidOperationException($"AuthServer exited during startup with code {process.ExitCode}.");
                }

                throw new TimeoutException($"AuthServer did not accept TCP connections on port {PortSetting} within {Settings.StartupTimeoutSeconds} seconds.");
            }

            SetStatus(ComponentStatus.Running);
            Logger.Info(LogChannel.Manager, $"AuthServer is ready on port {PortSetting}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(ComponentStatus.Error);
            Logger.Error(LogChannel.Manager, "AuthServer failed to start.", exception);
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
            Logger.Info(LogChannel.Manager, $"Requesting graceful AuthServer shutdown (PID {process.Id}).");

            if (!ConsoleControlSignal.TrySendCtrlBreak(process.Id, out var error))
            {
                Logger.Info(LogChannel.Manager, $"Console control signal was unavailable for AuthServer: {error}");
                process.CloseMainWindow();
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
            Logger.Error(LogChannel.Manager, "AuthServer failed to stop.", exception);
            throw;
        }
        finally
        {
            OperationGate.Release();
        }
    }
}
