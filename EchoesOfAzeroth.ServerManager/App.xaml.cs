using System.Windows;
using System.Windows.Threading;
using EchoesOfAzeroth.ServerManager.Services;
using EchoesOfAzeroth.ServerManager.ViewModels;

namespace EchoesOfAzeroth.ServerManager;

public partial class App : Application
{
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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "An unexpected error occurred. Full details were written to the manager log.",
            "Server Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
