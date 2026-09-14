using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using EchoesOfAzeroth.ServerManager.ViewModels;

namespace EchoesOfAzeroth.ServerManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _initialized;
    private bool _closingAfterCleanup;

    public MainWindow()
    {
        InitializeComponent();
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingAfterCleanup)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        await _viewModel.PrepareForExitAsync();
        await _viewModel.DisposeAsync();
        _closingAfterCleanup = true;
        Close();
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
