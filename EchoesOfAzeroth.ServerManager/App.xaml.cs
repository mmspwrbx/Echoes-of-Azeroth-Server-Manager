using System.Windows;
using System.Windows.Threading;
using EchoesOfAzeroth.ServerManager.Services;
using EchoesOfAzeroth.ServerManager.ViewModels;

namespace EchoesOfAzeroth.ServerManager;

public partial class App : Application
{
    internal bool IsShuttingDown { get; private set; }

    internal void BeginShutdown() => IsShuttingDown = true;

    internal bool IsExpectedShutdownCancellation(Exception exception) =>
        IsShuttingDown &&
        exception is OperationCanceledException canceled &&
        canceled.CancellationToken.IsCancellationRequested;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppRuntime? runtime = null;
        try
        {
            var selfTest = e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase);
            var settingsPath = GetArgumentValue(e.Args, "--settings");
            runtime = await AppRuntime.CreateAsync(settingsPath).ConfigureAwait(true);

            if (selfTest)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var reportPath = GetArgumentValue(e.Args, "--report") ??
                                 Path.Combine(AppContext.BaseDirectory, "self-test-report.json");
                var includeUiSmokeTest = e.Args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase);
                var exitCode = await SelfTestRunner.RunAsync(
                    runtime,
                    reportPath,
                    includeUiSmokeTest,
                    CancellationToken.None).ConfigureAwait(true);
                await runtime.DisposeAsync().ConfigureAwait(true);
                runtime = null;
                Shutdown(exitCode);
                return;
            }

            var viewModel = new MainViewModel(runtime, new DialogService());
            runtime = null;
            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(true);
            }

            MessageBox.Show(
                $"Echoes of Azeroth Server Manager could not start.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Server Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? GetArgumentValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BeginShutdown();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandledException(e.Exception);
        e.Handled = true;
    }

    internal void ReportUnhandledException(Exception exception)
    {
        if (IsExpectedShutdownCancellation(exception))
        {
            return;
        }

        try
        {
            var directory = (MainWindow?.DataContext as MainViewModel)?.Settings.LogsDirectory ??
                            Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "EchoesOfAzeroth", "ServerManager", "logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"manager-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] ERROR Unhandled application exception.{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // The global error dialog must remain available even if log storage fails.
        }

        MessageBox.Show(
            "An unexpected error occurred. Full details were written to the manager log.",
            "Server Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
