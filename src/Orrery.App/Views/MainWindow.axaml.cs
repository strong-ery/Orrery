using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Orrery.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Raised when the window is closed but the app carries on in the tray.</summary>
    public event Action? HiddenToTray;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing is "put away", unless the user quit from the tray or Windows is shutting down.
        if (!App.Quitting && e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+K from anywhere opens quick add; Esc puts it away.
        if (DataContext is ViewModels.MainViewModel { Capture: var capture })
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                capture.Open();
                FocusTitle();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && capture.IsOpen)
            {
                capture.IsOpen = false;
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>The form opens with the cursor in the name field, ready to type.</summary>
    public void FocusTitle() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => CaptureTitle.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);

    private void OnQuickAddClicked(object? sender, RoutedEventArgs e) => FocusTitle();

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel main)
            main.Capture.IsOpen = false;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximized();
    private void OnMinimizeClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximizeClicked(object? sender, RoutedEventArgs e) => ToggleMaximized();
    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
