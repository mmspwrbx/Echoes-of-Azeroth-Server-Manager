namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class MonitoringService : IAsyncDisposable
{
    private readonly MySqlServiceManager _database;
    private readonly AuthServerManager _authServer;
    private readonly WorldServerManager _worldServer;
    private readonly LoggingService _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _monitorTask;

    public MonitoringService(
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

    public void Start()
    {
        _monitorTask ??= MonitorAsync(_shutdown.Token);
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _database.RefreshAsync(cancellationToken),
            _authServer.RefreshAsync(cancellationToken),
            _worldServer.RefreshAsync(cancellationToken)).ConfigureAwait(false);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                try
                {
                    await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error(Models.LogChannel.Manager, "Periodic state monitoring failed.", exception);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_monitorTask is not null)
        {
            await _monitorTask.ConfigureAwait(false);
        }

        _shutdown.Dispose();
    }
}

