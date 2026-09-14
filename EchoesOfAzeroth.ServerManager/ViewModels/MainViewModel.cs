using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using EchoesOfAzeroth.ServerManager.Infrastructure;
using EchoesOfAzeroth.ServerManager.Models;
using EchoesOfAzeroth.ServerManager.Services;

namespace EchoesOfAzeroth.ServerManager.ViewModels;

public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private const int MaximumLogEntriesPerChannel = 1500;
    private readonly AppRuntime _runtime;
    private readonly IDialogService _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lifecycleSync = new();
    private readonly HashSet<Task<bool>> _activeOperations = [];
    private Task? _prepareForExitTask;
    private Task? _disposeTask;
    private int _shutdownStarted;
    private IReadOnlyList<ValidationIssue> _validationIssues = [];
    private bool _isBusy;
    private string _worldCommandText = string.Empty;
    private LanguageOption? _selectedLanguage;

    public MainViewModel(AppRuntime runtime, IDialogService dialogs)
    {
        _runtime = runtime;
        _dialogs = dialogs;
        Loc = runtime.Localization;
        Settings = runtime.Settings;
        Languages =
        [
            new LanguageOption("en-US", "English"),
            new LanguageOption("ru-RU", "Русский")
        ];
        _selectedLanguage = Languages.First(x => x.CultureName == Settings.Language ||
            (Settings.Language != "ru-RU" && x.CultureName == "en-US"));

        StartServerCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.Stack.StartAsync), () => !IsBusy);
        StopServerCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.Stack.StopAsync), () => !IsBusy);
        RestartServerCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.Stack.RestartAsync), () => !IsBusy);
        StartDatabaseCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.Database.StartAsync), () => !IsBusy);
        StopDatabaseCommand = new AsyncRelayCommand(StopDatabaseSafelyAsync, () => !IsBusy);
        RestartDatabaseCommand = new AsyncRelayCommand(RestartDatabaseSafelyAsync, () => !IsBusy);
        StartAuthCommand = new AsyncRelayCommand(StartAuthSafelyAsync, () => !IsBusy);
        StopAuthCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.AuthServer.StopAsync), () => !IsBusy);
        RestartAuthCommand = new AsyncRelayCommand(RestartAuthSafelyAsync, () => !IsBusy);
        StartWorldCommand = new AsyncRelayCommand(StartWorldSafelyAsync, () => !IsBusy);
        StopWorldCommand = new AsyncRelayCommand(() => RunOperationAsync(_runtime.WorldServer.StopAsync), () => !IsBusy);
        RestartWorldCommand = new AsyncRelayCommand(RestartWorldSafelyAsync, () => !IsBusy);
        SendWorldCommandCommand = new AsyncRelayCommand(SendWorldCommandAsync, () => !IsBusy && CanSendWorldCommand);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);

        runtime.Logging.EntryAdded += OnLogEntryAdded;
        runtime.Database.StateChanged += OnComponentStateChanged;
        runtime.AuthServer.StateChanged += OnComponentStateChanged;
        runtime.WorldServer.StateChanged += OnComponentStateChanged;
        runtime.Localization.PropertyChanged += OnLocalizationChanged;
    }

    public LocalizationService Loc { get; }
    public AppSettings Settings { get; }
    public IReadOnlyList<LanguageOption> Languages { get; }
    public ObservableCollection<LogEntry> ManagerLogs { get; } = [];
    public ObservableCollection<LogEntry> AuthLogs { get; } = [];
    public ObservableCollection<LogEntry> WorldLogs { get; } = [];

    public AsyncRelayCommand StartServerCommand { get; }
    public AsyncRelayCommand StopServerCommand { get; }
    public AsyncRelayCommand RestartServerCommand { get; }
    public AsyncRelayCommand StartDatabaseCommand { get; }
    public AsyncRelayCommand StopDatabaseCommand { get; }
    public AsyncRelayCommand RestartDatabaseCommand { get; }
    public AsyncRelayCommand StartAuthCommand { get; }
    public AsyncRelayCommand StopAuthCommand { get; }
    public AsyncRelayCommand RestartAuthCommand { get; }
    public AsyncRelayCommand StartWorldCommand { get; }
    public AsyncRelayCommand StopWorldCommand { get; }
    public AsyncRelayCommand RestartWorldCommand { get; }
    public AsyncRelayCommand SendWorldCommandCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }

    public ComponentStatus DatabaseStatus => _runtime.Database.Status;
    public ComponentStatus AuthStatus => _runtime.AuthServer.Status;
    public ComponentStatus WorldStatus => _runtime.WorldServer.Status;
    public string DatabaseStatusText => Loc[DatabaseStatus.ToString()];
    public string AuthStatusText => Loc[AuthStatus.ToString()];
    public string WorldStatusText => Loc[WorldStatus.ToString()];
    public bool IsOnline => _runtime.Stack.IsOnline;
    public string OverallStatusText => Loc[IsOnline ? "ServerOnline" : "ServerOffline"];
    public string DatabasePortText => GetPortText(Settings.MySqlPort, _runtime.Database.PortOpen);
    public string AuthPortText => GetPortText(Settings.AuthServerPort, _runtime.AuthServer.PortOpen);
    public string WorldPortText => GetPortText(Settings.WorldServerPort, _runtime.WorldServer.PortOpen);
    public bool CanSendWorldCommand => _runtime.WorldServer.CanSendCommands;

    public bool HasValidationIssues => _validationIssues.Count > 0;
    public string ValidationSummary => string.Join(
        Environment.NewLine,
        _validationIssues.Select(issue => $"• {Loc[issue.ResourceKey]}{(issue.Detail is null ? string.Empty : $": {issue.Detail}")}"));

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string WorldCommandText
    {
        get => _worldCommandText;
        set => SetProperty(ref _worldCommandText, value);
    }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value) || value is null)
            {
                return;
            }

            Settings.Language = value.CultureName;
            Loc.SetCulture(value.CultureName);
        }
    }

    public async Task InitializeAsync()
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        _runtime.Logging.Info(LogChannel.Manager, "Echoes of Azeroth Server Manager started.");
        await RefreshValidationAsync(_lifetime.Token).ConfigureAwait(true);
        await _runtime.Monitoring.RefreshNowAsync(_lifetime.Token).ConfigureAwait(true);
        _runtime.Monitoring.Start();
        NotifyComponentState();
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
        {
            _lifetime.Cancel();
        }
    }

    public Task PrepareForExitAsync()
    {
        BeginShutdown();
        lock (_lifecycleSync)
        {
            return _prepareForExitTask ??= PrepareForExitCoreAsync();
        }
    }

    private async Task PrepareForExitCoreAsync()
    {
        Task<bool>[] activeOperations;
        lock (_lifecycleSync)
        {
            activeOperations = _activeOperations.ToArray();
        }

        await Task.WhenAll(activeOperations).ConfigureAwait(true);
        await _runtime.Monitoring.DisposeAsync().ConfigureAwait(true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            Math.Max(5, Settings.GracefulShutdownTimeoutSeconds * 2 + 5)));
        try
        {
            if (_runtime.WorldServer.IsOwned && _runtime.WorldServer.Status != ComponentStatus.Stopped)
            {
                await _runtime.WorldServer.StopAsync(timeout.Token).ConfigureAwait(true);
            }

            if (_runtime.AuthServer.IsOwned && _runtime.AuthServer.Status != ComponentStatus.Stopped)
            {
                await _runtime.AuthServer.StopAsync(timeout.Token).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _runtime.Logging.Error(LogChannel.Manager, "Managed child-process cleanup failed during application exit.", exception);
        }
    }

    private async Task StartAuthSafelyAsync()
    {
        if (!await EnsureDatabaseRunningAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunOperationAsync(_runtime.AuthServer.StartAsync).ConfigureAwait(true);
    }

    private async Task StartWorldSafelyAsync()
    {
        if (!await EnsureDatabaseRunningAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunOperationAsync(_runtime.WorldServer.StartAsync).ConfigureAwait(true);
    }

    private async Task RestartAuthSafelyAsync()
    {
        if (!await EnsureDatabaseRunningAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunOperationAsync(_runtime.AuthServer.RestartAsync).ConfigureAwait(true);
    }

    private async Task RestartWorldSafelyAsync()
    {
        if (!await EnsureDatabaseRunningAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunOperationAsync(_runtime.WorldServer.RestartAsync).ConfigureAwait(true);
    }

    private async Task<bool> EnsureDatabaseRunningAsync()
    {
        if (_runtime.Database.Status == ComponentStatus.Running && _runtime.Database.PortOpen)
        {
            return true;
        }

        if (!_dialogs.Confirm(Loc["DatabaseNotRunningStart"], Loc["Confirmation"]))
        {
            return false;
        }

        return await RunOperationAsync(_runtime.Database.StartAsync).ConfigureAwait(true);
    }

    private async Task StopDatabaseSafelyAsync()
    {
        var dependentsRunning = _runtime.AuthServer.Status != ComponentStatus.Stopped ||
                                _runtime.WorldServer.Status != ComponentStatus.Stopped;
        if (dependentsRunning)
        {
            if (!_dialogs.Confirm(Loc["DatabaseHasDependentsStop"], Loc["Confirmation"]))
            {
                return;
            }

            await RunOperationAsync(_runtime.Stack.StopDatabaseWithDependentsAsync).ConfigureAwait(true);
            return;
        }

        await RunOperationAsync(_runtime.Database.StopAsync).ConfigureAwait(true);
    }

    private async Task RestartDatabaseSafelyAsync()
    {
        var dependentsRunning = _runtime.AuthServer.Status == ComponentStatus.Running ||
                                _runtime.WorldServer.Status == ComponentStatus.Running;
        if (dependentsRunning)
        {
            if (!_dialogs.Confirm(Loc["DatabaseRestartDependents"], Loc["Confirmation"]))
            {
                return;
            }

            await RunOperationAsync(_runtime.Stack.RestartDatabaseWithDependentsAsync).ConfigureAwait(true);
            return;
        }

        await RunOperationAsync(_runtime.Database.RestartAsync).ConfigureAwait(true);
    }

    private async Task SendWorldCommandAsync()
    {
        if (_runtime.WorldServer.Status != ComponentStatus.Running)
        {
            _dialogs.ShowError(Loc["WorldNotRunning"], Loc["ErrorTitle"]);
            return;
        }

        if (!_runtime.WorldServer.IsOwned)
        {
            _dialogs.ShowError(Loc["WorldNotManaged"], Loc["ErrorTitle"]);
            return;
        }

        var command = WorldCommandText;
        WorldCommandText = string.Empty;
        await RunOperationAsync(token => _runtime.WorldServer.SendCommandAsync(command, token)).ConfigureAwait(true);
    }

    private async Task SaveSettingsAsync()
    {
        if (!AreSettingsValid())
        {
            _dialogs.ShowError(Loc["InvalidSettings"], Loc["ErrorTitle"]);
            return;
        }

        var saved = await RunOperationAsync(async token =>
        {
            await _runtime.SettingsService.SaveAsync(Settings, token).ConfigureAwait(false);
            _runtime.Logging.SetLogDirectory(Settings.LogsDirectory);
            await RefreshValidationAsync(token).ConfigureAwait(false);
            _runtime.Logging.Info(LogChannel.Manager, "Settings saved.");
        }).ConfigureAwait(true);
        if (saved)
        {
            _dialogs.ShowInformation(Loc["SettingsSaved"], Loc["ServerManager"]);
        }
    }

    private bool AreSettingsValid() =>
        !string.IsNullOrWhiteSpace(Settings.ServerRootPath) &&
        !string.IsNullOrWhiteSpace(Settings.MySqlServiceName) &&
        !string.IsNullOrWhiteSpace(Settings.AuthServerExecutable) &&
        !string.IsNullOrWhiteSpace(Settings.WorldServerExecutable) &&
        Settings.MySqlPort is > 0 and <= 65535 &&
        Settings.AuthServerPort is > 0 and <= 65535 &&
        Settings.WorldServerPort is > 0 and <= 65535 &&
        Settings.StartupTimeoutSeconds > 0 &&
        Settings.GracefulShutdownTimeoutSeconds > 0;

    private Task<bool> RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        lock (_lifecycleSync)
        {
            if (Volatile.Read(ref _shutdownStarted) != 0)
            {
                return Task.FromResult(false);
            }

            var task = RunOperationCoreAsync(operation);
            _activeOperations.Add(task);
            _ = task.ContinueWith(completed =>
            {
                lock (_lifecycleSync)
                {
                    _activeOperations.Remove(completed);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task<bool> RunOperationCoreAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            await operation(_lifetime.Token).ConfigureAwait(true);
            await _runtime.Monitoring.RefreshNowAsync(_lifetime.Token).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _runtime.Logging.Error(LogChannel.Manager, "UI operation failed.", exception);
            _dialogs.ShowError(Loc["OperationFailed"], Loc["ErrorTitle"]);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyComponentState();
        }
    }

    private async Task RefreshValidationAsync(CancellationToken cancellationToken)
    {
        _validationIssues = await _runtime.Validation.ValidateAsync(cancellationToken).ConfigureAwait(false);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(HasValidationIssues));
            OnPropertyChanged(nameof(ValidationSummary));
        });
    }

    private string GetPortText(int port, bool isOpen) => $"{Loc["Port"]} {port} · {Loc[isOpen ? "Open" : "Closed"]}";

    private void OnComponentStateChanged(object? sender, EventArgs e)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(NotifyComponentState);
    }

    private void NotifyComponentState()
    {
        OnPropertyChanged(nameof(DatabaseStatus));
        OnPropertyChanged(nameof(AuthStatus));
        OnPropertyChanged(nameof(WorldStatus));
        OnPropertyChanged(nameof(DatabaseStatusText));
        OnPropertyChanged(nameof(AuthStatusText));
        OnPropertyChanged(nameof(WorldStatusText));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(OverallStatusText));
        OnPropertyChanged(nameof(DatabasePortText));
        OnPropertyChanged(nameof(AuthPortText));
        OnPropertyChanged(nameof(WorldPortText));
        OnPropertyChanged(nameof(CanSendWorldCommand));
        RaiseCommandStates();
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyComponentState();
        OnPropertyChanged(nameof(ValidationSummary));
    }

    private void OnLogEntryAdded(object? sender, LogEntry entry)
    {
        if (Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            var target = entry.Channel switch
            {
                LogChannel.WorldServer => WorldLogs,
                LogChannel.AuthServer => AuthLogs,
                _ => ManagerLogs
            };
            target.Add(entry);
            while (target.Count > MaximumLogEntriesPerChannel)
            {
                target.RemoveAt(0);
            }
        });
    }

    private void RaiseCommandStates()
    {
        StartServerCommand.RaiseCanExecuteChanged();
        StopServerCommand.RaiseCanExecuteChanged();
        RestartServerCommand.RaiseCanExecuteChanged();
        StartDatabaseCommand.RaiseCanExecuteChanged();
        StopDatabaseCommand.RaiseCanExecuteChanged();
        RestartDatabaseCommand.RaiseCanExecuteChanged();
        StartAuthCommand.RaiseCanExecuteChanged();
        StopAuthCommand.RaiseCanExecuteChanged();
        RestartAuthCommand.RaiseCanExecuteChanged();
        StartWorldCommand.RaiseCanExecuteChanged();
        StopWorldCommand.RaiseCanExecuteChanged();
        RestartWorldCommand.RaiseCanExecuteChanged();
        SendWorldCommandCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
    }

    public ValueTask DisposeAsync()
    {
        BeginShutdown();
        lock (_lifecycleSync)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task<bool>[] activeOperations;
        lock (_lifecycleSync)
        {
            activeOperations = _activeOperations.ToArray();
        }

        await Task.WhenAll(activeOperations).ConfigureAwait(false);
        _runtime.Logging.EntryAdded -= OnLogEntryAdded;
        _runtime.Database.StateChanged -= OnComponentStateChanged;
        _runtime.AuthServer.StateChanged -= OnComponentStateChanged;
        _runtime.WorldServer.StateChanged -= OnComponentStateChanged;
        _runtime.Localization.PropertyChanged -= OnLocalizationChanged;
        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
