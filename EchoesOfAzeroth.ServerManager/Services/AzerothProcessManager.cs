using System.Diagnostics;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public abstract class AzerothProcessManager : IAsyncDisposable
{
    private readonly object _processSync = new();
    private Process? _process;
    private ComponentStatus _status = ComponentStatus.Stopped;
    private bool _portOpen;
    private bool _expectedStop;

    protected AzerothProcessManager(AppSettings settings, PortMonitor portMonitor, LoggingService logger)
    {
        Settings = settings;
        PortMonitor = portMonitor;
        Logger = logger;
    }

    public event EventHandler? StateChanged;

    public ComponentStatus Status => _status;
    public bool PortOpen => _portOpen;
    public bool IsOwned { get; private set; }
    public int? ProcessId
    {
        get
        {
            lock (_processSync)
            {
                return IsAlive(_process) ? _process!.Id : null;
            }
        }
    }

    protected AppSettings Settings { get; }
    protected PortMonitor PortMonitor { get; }
    protected LoggingService Logger { get; }
    protected SemaphoreSlim OperationGate { get; } = new(1, 1);
    protected abstract string ExecutableSetting { get; }
    protected abstract string ArgumentsSetting { get; }
    protected abstract int PortSetting { get; }
    protected abstract LogChannel LogChannel { get; }
    protected abstract string ComponentName { get; }

    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_processSync)
        {
            process = _process;
        }

        if (!IsAlive(process))
        {
            process = FindExistingProcess();
            if (process is not null)
            {
                SetProcess(process, false);
                SetStatus(ComponentStatus.Running);
                Logger.Info(LogChannel.Manager, $"Detected externally started {ComponentName} (PID {process.Id}).");
            }
            else if (Status != ComponentStatus.Error && Status is not ComponentStatus.Starting and not ComponentStatus.Stopping)
            {
                SetStatus(ComponentStatus.Stopped);
            }
        }
        else if (Status is not ComponentStatus.Starting and not ComponentStatus.Stopping and not ComponentStatus.Error)
        {
            SetStatus(ComponentStatus.Running);
        }

        SetPortOpen(await PortMonitor.IsAcceptingConnectionsAsync(PortSetting, cancellationToken).ConfigureAwait(false));
    }

    protected Process? AttachExistingProcess()
    {
        Process? current;
        lock (_processSync)
        {
            current = _process;
        }

        if (IsAlive(current))
        {
            return current;
        }

        var existing = FindExistingProcess();
        if (existing is null)
        {
            return null;
        }

        SetProcess(existing, false);
        Logger.Info(LogChannel.Manager, $"Using existing {ComponentName} process (PID {existing.Id}); duplicate start prevented.");
        return existing;
    }

    protected Process StartOwnedProcess(bool redirectInput)
    {
        var executablePath = Settings.GetExecutablePath(ExecutableSetting);
        if (!Directory.Exists(Settings.ServerRootPath))
        {
            throw new DirectoryNotFoundException($"Server root directory does not exist: {Settings.ServerRootPath}");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"{ComponentName} executable was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = ArgumentsSetting,
            WorkingDirectory = Settings.ServerRootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnProcessExited;

        _expectedStop = false;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Windows did not start {ComponentName}.");
        }

        SetProcess(process, true);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Logger.Info(LogChannel.Manager, $"Started {ComponentName} (PID {process.Id}).");
        return process;
    }

    protected async Task<bool> WaitForPortAsync(Process process, CancellationToken cancellationToken)
    {
        var ready = await PortMonitor.WaitUntilAcceptingConnectionsAsync(
            PortSetting,
            TimeSpan.FromSeconds(Settings.StartupTimeoutSeconds),
            () => !IsAlive(process),
            cancellationToken).ConfigureAwait(false);
        SetPortOpen(ready);
        return ready;
    }

    protected async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsAlive(process))
        {
            return true;
        }

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (completed != exitTask)
        {
            return false;
        }

        await exitTask.ConfigureAwait(false);
        return true;
    }

    protected async Task ForceKillAsync(Process process, CancellationToken cancellationToken)
    {
        if (!IsAlive(process))
        {
            return;
        }

        Logger.Info(LogChannel.Manager, $"Graceful {ComponentName} shutdown timed out; using emergency process-tree termination.");
        process.Kill(true);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected Process? GetCurrentProcess()
    {
        lock (_processSync)
        {
            return IsAlive(_process) ? _process : null;
        }
    }

    protected void BeginExpectedStop() => _expectedStop = true;

    protected void CompleteStop()
    {
        SetPortOpen(false);
        SetStatus(ComponentStatus.Stopped);
        ClearProcess();
    }

    protected void SetStatus(ComponentStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void SetPortOpen(bool value)
    {
        if (_portOpen == value)
        {
            return;
        }

        _portOpen = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected static bool IsAlive(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    protected virtual void OnOutputLine(string line, bool isError)
    {
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        Logger.Info(LogChannel, e.Data);
        OnOutputLine(e.Data, false);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        Logger.Error(LogChannel, e.Data);
        OnOutputLine(e.Data, true);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess)
        {
            return;
        }

        lock (_processSync)
        {
            if (!ReferenceEquals(_process, exitedProcess))
            {
                return;
            }
        }

        int exitCode;
        try
        {
            exitCode = exitedProcess.ExitCode;
        }
        catch
        {
            exitCode = -1;
        }

        SetPortOpen(false);
        if (_expectedStop)
        {
            SetStatus(ComponentStatus.Stopped);
            Logger.Info(LogChannel.Manager, $"{ComponentName} stopped normally (exit code {exitCode}).");
        }
        else
        {
            SetStatus(ComponentStatus.Error);
            Logger.Error(LogChannel.Manager, $"{ComponentName} exited unexpectedly with code {exitCode}.");
        }
    }

    private Process? FindExistingProcess()
    {
        var expectedPath = Path.GetFullPath(Settings.GetExecutablePath(ExecutableSetting));
        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        foreach (var candidate in Process.GetProcessesByName(processName))
        {
            try
            {
                var candidatePath = candidate.MainModule?.FileName;
                if (candidatePath is not null &&
                    string.Equals(Path.GetFullPath(candidatePath), expectedPath, StringComparison.OrdinalIgnoreCase) &&
                    !candidate.HasExited)
                {
                    candidate.EnableRaisingEvents = true;
                    candidate.Exited += OnProcessExited;
                    return candidate;
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            candidate.Dispose();
        }

        return null;
    }

    private void SetProcess(Process process, bool owned)
    {
        Process? previous;
        lock (_processSync)
        {
            previous = _process;
            _process = process;
            IsOwned = owned;
            _expectedStop = false;
        }

        if (previous is not null && !ReferenceEquals(previous, process))
        {
            previous.Dispose();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearProcess()
    {
        Process? previous;
        lock (_processSync)
        {
            previous = _process;
            _process = null;
            IsOwned = false;
        }

        previous?.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public virtual ValueTask DisposeAsync()
    {
        ClearProcess();
        OperationGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
