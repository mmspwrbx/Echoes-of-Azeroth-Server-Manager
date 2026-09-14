using EchoesOfAzeroth.ServerManager.Models;

namespace EchoesOfAzeroth.ServerManager.Services;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    private AppRuntime(
        SettingsService settingsService,
        AppSettings settings,
        LocalizationService localization,
        LoggingService logging,
        MySqlServiceManager database,
        AuthServerManager authServer,
        WorldServerManager worldServer,
        ServerStackManager stack,
        MonitoringService monitoring,
        ValidationService validation)
    {
        SettingsService = settingsService;
        Settings = settings;
        Localization = localization;
        Logging = logging;
        Database = database;
        AuthServer = authServer;
        WorldServer = worldServer;
        Stack = stack;
        Monitoring = monitoring;
        Validation = validation;
    }

    public SettingsService SettingsService { get; }
    public AppSettings Settings { get; }
    public LocalizationService Localization { get; }
    public LoggingService Logging { get; }
    public MySqlServiceManager Database { get; }
    public AuthServerManager AuthServer { get; }
    public WorldServerManager WorldServer { get; }
    public ServerStackManager Stack { get; }
    public MonitoringService Monitoring { get; }
    public ValidationService Validation { get; }

    public static async Task<AppRuntime> CreateAsync(string? settingsPath = null, CancellationToken cancellationToken = default)
    {
        var settingsService = new SettingsService(settingsPath);
        var settings = await settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var localization = new LocalizationService();
        localization.SetCulture(settings.Language);
        var logging = new LoggingService(settings.LogsDirectory);
        var portMonitor = new PortMonitor();
        var database = new MySqlServiceManager(settings, portMonitor, logging);
        var authServer = new AuthServerManager(settings, portMonitor, logging);
        var worldServer = new WorldServerManager(settings, portMonitor, logging);
        var stack = new ServerStackManager(database, authServer, worldServer, logging);
        var monitoring = new MonitoringService(database, authServer, worldServer, logging);
        var validation = new ValidationService(settings, database);
        return new AppRuntime(
            settingsService,
            settings,
            localization,
            logging,
            database,
            authServer,
            worldServer,
            stack,
            monitoring,
            validation);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        try { await Monitoring.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }
        try { await WorldServer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }
        try { await AuthServer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }
        try { await Logging.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }

        if (errors.Count > 0)
        {
            throw new AggregateException("One or more runtime services failed to dispose.", errors);
        }
    }
}
