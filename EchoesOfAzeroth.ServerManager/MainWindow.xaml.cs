using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using EchoesOfAzeroth.ServerManager.ViewModels;

namespace EchoesOfAzeroth.ServerManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _initialized;
    private bool _shutdownInProgress;
    private bool _closingAfterCleanup;
    private Task? _initializationTask;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowIcon();
        _viewModel = null!;
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        viewModel.ManagerLogs.CollectionChanged += OnManagerLogChanged;
        viewModel.AuthLogs.CollectionChanged += OnAuthLogChanged;
        viewModel.WorldLogs.CollectionChanged += OnWorldLogChanged;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximizedState();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizedState() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void ApplyWindowIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/AppIcon.ico", UriKind.Absolute));
            if (resource is null)
            {
                return;
            }

            using var stream = resource.Stream;
            Icon = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        }
        catch (IOException)
        {
            // The branded icon is optional until Assets/AppIcon.ico is supplied.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _initializationTask = InitializeWithErrorHandlingAsync();
    }

    private async Task InitializeWithErrorHandlingAsync()
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (OperationCanceledException exception) when (Application.Current is App app && app.IsExpectedShutdownCancellation(exception))
        {
            // Closing during initial validation is an expected cancellation.
        }
        catch (Exception exception)
        {
            (Application.Current as App)?.ReportUnhandledException(exception);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterCleanup)
        {
            return;
        }

        e.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        IsEnabled = false;
        var app = Application.Current as App;
        app?.BeginShutdown();
        _viewModel.BeginShutdown();
        try
        {
            if (_initializationTask is not null)
            {
                await _initializationTask;
            }

            await _viewModel.PrepareForExitAsync();
        }
        catch (OperationCanceledException exception) when (app?.IsExpectedShutdownCancellation(exception) == true)
        {
        }
        catch (Exception exception)
        {
            app?.ReportUnhandledException(exception);
        }
        finally
        {
            try
            {
                await _viewModel.DisposeAsync();
            }
            catch (OperationCanceledException exception) when (app?.IsExpectedShutdownCancellation(exception) == true)
            {
            }
            catch (Exception exception)
            {
                app?.ReportUnhandledException(exception);
            }

            _closingAfterCleanup = true;
            Close();
        }
    }

    private void OnManagerLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd(ManagerLogList);
    private void OnAuthLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd(AuthLogList);
    private void OnWorldLogChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd(WorldLogList);

    private static void ScrollToEnd(System.Windows.Controls.ListBox listBox)
    {
        if (listBox.Items.Count > 0)
        {
            listBox.ScrollIntoView(listBox.Items[^1]);
        }
    }
}
