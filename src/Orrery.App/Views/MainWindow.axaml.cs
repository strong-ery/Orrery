using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Orrery.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            (DataContext as ViewModels.MainViewModel)?.FitToWidth(e.NewSize.Width);
            Remember();
        };
        PositionChanged += (_, _) => Remember();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                Remember();
        };
        Opened += (_, _) => _tracking = true;
    }

    private bool _tracking;

    /// <summary>Notes where the window is, so it comes back in the same place and size (and maximized, if it was).</summary>
    private void Remember()
    {
        if (!_tracking || DataContext is not ViewModels.MainViewModel main || WindowState == WindowState.Minimized)
            return;
        var ui = main.Data.Settings.Ui;
        ui.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            ui.WindowX = Position.X;
            ui.WindowY = Position.Y;
            ui.WindowWidth = ClientSize.Width;
            ui.WindowHeight = ClientSize.Height;
        }
        main.UiChanged();
    }

    /// <summary>Puts the window back where it was, unless that place is no longer on any screen.</summary>
    public void RestorePlace(Orrery.Core.Models.UiState ui)
    {
        if (ui.WindowWidth is { } w and >= 600 && ui.WindowHeight is { } h and >= 400)
        {
            Width = w;
            Height = h;
        }
        if (ui.WindowX is { } x && ui.WindowY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new Avalonia.PixelPoint(x, y);
            // If that place is no longer on any screen (a monitor was unplugged), fall back to the middle of the main one.
            Opened += (_, _) =>
            {
                var on = Screens.All.Any(s => s.WorkingArea.Contains(new Avalonia.PixelPoint(Position.X + 80, Position.Y + 24)));
                if (!on && Screens.Primary is { } primary)
                {
                    var wa = primary.WorkingArea;
                    Position = new Avalonia.PixelPoint(wa.X + Math.Max(0, (wa.Width - (int)(Width * primary.Scaling)) / 2),
                        wa.Y + Math.Max(0, (wa.Height - (int)(Height * primary.Scaling)) / 2));
                }
            };
        }
        if (ui.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void OnOpenDataFolder(object? sender, RoutedEventArgs e) => (DataContext as ViewModels.MainViewModel)?.OpenDataFolder();

    /// <summary>Raised when the window is closed but the app carries on in the tray.</summary>
    public event Action? HiddenToTray;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing is "put away", unless the user quit from the tray or Windows is shutting down.
        (DataContext as ViewModels.MainViewModel)?.SaveUiNow();
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

    private ViewModels.CaptureViewModel? _hooked;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hooked is not null)
            _hooked.Opened -= FocusTitle;
        (DataContext as ViewModels.MainViewModel)?.FitToWidth(Bounds.Width > 0 ? Bounds.Width : Width);
        _hooked = (DataContext as ViewModels.MainViewModel)?.Capture;
        if (_hooked is not null)
            _hooked.Opened += FocusTitle;
    }

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
