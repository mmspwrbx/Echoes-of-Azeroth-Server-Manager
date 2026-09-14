using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class ServerStackManager
{
    private readonly MySqlServiceManager _database;
    private readonly AuthServerManager _authServer;
    private readonly WorldServerManager _worldServer;
    private readonly LoggingService _logger;
    private readonly SemaphoreSlim _stackGate = new(1, 1);

    public ServerStackManager(
        MySqlServiceManager database,
        AuthServerManager authServer,
        WorldServerManager worldServer,
        LoggingService logger)
    {
        _database = database;
        _authServer = authServer;
        _worldServer = worldServer;
        _logger = logger;
    }

    public bool IsOnline =>
        _database.Status == ComponentStatus.Running &&
        _database.PortOpen &&
        _authServer.Status == ComponentStatus.Running &&
        _authServer.PortOpen &&
        _worldServer.Status == ComponentStatus.Running;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _stackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info(LogChannel.Manager, "Starting the server stack in dependency order: MySQL -> AuthServer -> WorldServer.");
            await _database.StartAsync(cancellationToken).ConfigureAwait(false);
            await _authServer.StartAsync(cancellationToken).ConfigureAwait(false);
            await _worldServer.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(LogChannel.Manager, "SERVER ONLINE");
        }
        finally
        {
            _stackGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info(LogChannel.Manager, "Stopping the server stack in dependency order: WorldServer -> AuthServer -> MySQL.");
            await StopProcessesAsync(cancellationToken).ConfigureAwait(false);
            await _database.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(LogChannel.Manager, "SERVER OFFLINE");
        }
        finally
        {
            _stackGate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        await _stackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info(LogChannel.Manager, "Restarting the complete server stack.");
            await StopProcessesAsync(cancellationToken).ConfigureAwait(false);
            await _database.StopAsync(cancellationToken).ConfigureAwait(false);
            await _database.StartAsync(cancellationToken).ConfigureAwait(false);
            await _authServer.StartAsync(cancellationToken).ConfigureAwait(false);
            await _worldServer.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info(LogChannel.Manager, "SERVER ONLINE");
        }
        finally
        {
            _stackGate.Release();
        }
    }

    public async Task StopDatabaseWithDependentsAsync(CancellationToken cancellationToken)
    {
        await _stackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopProcessesAsync(cancellationToken).ConfigureAwait(false);
            await _database.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stackGate.Release();
        }
    }

    public async Task RestartDatabaseWithDependentsAsync(CancellationToken cancellationToken)
    {
        await _stackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var restartAuth = _authServer.Status == ComponentStatus.Running;
            var restartWorld = _worldServer.Status == ComponentStatus.Running;
            await StopProcessesAsync(cancellationToken).ConfigureAwait(false);
            await _database.RestartAsync(cancellationToken).ConfigureAwait(false);
            if (restartAuth)
            {
                await _authServer.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            if (restartWorld)
            {
                await _worldServer.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _stackGate.Release();
        }
    }

    private async Task StopProcessesAsync(CancellationToken cancellationToken)
    {
        if (_worldServer.Status != ComponentStatus.Stopped)
        {
            await _worldServer.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_authServer.Status != ComponentStatus.Stopped)
        {
            await _authServer.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

