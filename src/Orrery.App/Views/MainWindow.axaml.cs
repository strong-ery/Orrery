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
        // Ctrl+K from anywhere jumps to quick add.
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CaptureBox.Focus();
            CaptureBox.SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
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
