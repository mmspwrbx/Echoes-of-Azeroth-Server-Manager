using System.ComponentModel;
using System.ServiceProcess;
using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class MySqlServiceManager
{
    private readonly AppSettings _settings;
    private readonly PortMonitor _portMonitor;
    private readonly LoggingService _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ComponentStatus _status = ComponentStatus.Stopped;
    private bool _portOpen;

    public MySqlServiceManager(AppSettings settings, PortMonitor portMonitor, LoggingService logger)
    {
        _settings = settings;
        _portMonitor = portMonitor;
        _logger = logger;
    }

    public event EventHandler? StateChanged;

    public ComponentStatus Status => _status;
    public bool PortOpen => _portOpen;

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var controller = new ServiceController(_settings.MySqlServiceName);
            _ = await Task.Run(() => controller.Status, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var controller = new ServiceController(_settings.MySqlServiceName);
            var serviceStatus = await Task.Run(() => controller.Status, cancellationToken).ConfigureAwait(false);
            SetStatus(MapStatus(serviceStatus));
            SetPortOpen(await _portMonitor.IsAcceptingConnectionsAsync(_settings.MySqlPort, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(ComponentStatus.Error);
            SetPortOpen(false);
            _logger.Error(LogChannel.Manager, $"MySQL service '{_settings.MySqlServiceName}' was not found.", exception);
        }
        catch (Win32Exception exception)
        {
            SetStatus(ComponentStatus.Error);
            _logger.Error(LogChannel.Manager, "Unable to query the MySQL Windows service.", exception);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var controller = new ServiceController(_settings.MySqlServiceName);
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Running)
            {
                _logger.Info(LogChannel.Manager, $"MySQL service '{_settings.MySqlServiceName}' is already running; it will not be restarted.");
            }
            else
            {
                SetStatus(ComponentStatus.Starting);
                _logger.Info(LogChannel.Manager, $"Starting MySQL service '{_settings.MySqlServiceName}'.");
                await Task.Run(controller.Start, cancellationToken).ConfigureAwait(false);
                if (!await WaitForServiceStatusAsync(ServiceControllerStatus.Running, cancellationToken).ConfigureAwait(false))
                {
                    throw new System.TimeoutException($"MySQL service did not reach Running within {_settings.StartupTimeoutSeconds} seconds.");
                }
            }

            var portReady = await _portMonitor.WaitUntilAcceptingConnectionsAsync(
                _settings.MySqlPort,
                TimeSpan.FromSeconds(_settings.StartupTimeoutSeconds),
                null,
                cancellationToken).ConfigureAwait(false);
            if (!portReady)
            {
                throw new System.TimeoutException($"MySQL is running but port {_settings.MySqlPort} is not accepting connections.");
            }

            SetPortOpen(true);
            SetStatus(ComponentStatus.Running);
            _logger.Info(LogChannel.Manager, $"MySQL is ready on port {_settings.MySqlPort}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(ComponentStatus.Error);
            _logger.Error(LogChannel.Manager, "MySQL service failed to start.", exception);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var controller = new ServiceController(_settings.MySqlServiceName);
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                SetPortOpen(false);
                SetStatus(ComponentStatus.Stopped);
                return;
            }

            SetStatus(ComponentStatus.Stopping);
            _logger.Info(LogChannel.Manager, $"Stopping MySQL service '{_settings.MySqlServiceName}'.");
            await Task.Run(controller.Stop, cancellationToken).ConfigureAwait(false);
            if (!await WaitForServiceStatusAsync(ServiceControllerStatus.Stopped, cancellationToken).ConfigureAwait(false))
            {
                throw new System.TimeoutException($"MySQL service did not stop within {_settings.GracefulShutdownTimeoutSeconds} seconds.");
            }

            SetPortOpen(false);
            SetStatus(ComponentStatus.Stopped);
            _logger.Info(LogChannel.Manager, "MySQL service stopped.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(ComponentStatus.Error);
            _logger.Error(LogChannel.Manager, "MySQL service failed to stop. Administrator privileges may be required.", exception);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitForServiceStatusAsync(ServiceControllerStatus desiredStatus, CancellationToken cancellationToken)
    {
        var timeoutSeconds = desiredStatus == ServiceControllerStatus.Running
            ? _settings.StartupTimeoutSeconds
            : _settings.GracefulShutdownTimeoutSeconds;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var controller = new ServiceController(_settings.MySqlServiceName);
            var status = await Task.Run(() => controller.Status, cancellationToken).ConfigureAwait(false);
            SetStatus(MapStatus(status));
            if (status == desiredStatus)
            {
                return true;
            }

            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static ComponentStatus MapStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ComponentStatus.Running,
        ServiceControllerStatus.StartPending or ServiceControllerStatus.ContinuePending => ComponentStatus.Starting,
        ServiceControllerStatus.StopPending or ServiceControllerStatus.PausePending => ComponentStatus.Stopping,
        _ => ComponentStatus.Stopped
    };

    private void SetStatus(ComponentStatus status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPortOpen(bool value)
    {
        if (_portOpen == value)
        {
            return;
        }

        _portOpen = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
