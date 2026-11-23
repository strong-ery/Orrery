using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Orrery.App.Views;

/// <summary>
/// A small card in the corner of the screen. It does not take focus, goes away by itself, and is the only thing
/// that interrupts you, so it is only used for things that matter.
/// </summary>
public partial class ToastWindow : Window
{
    private readonly Action? _open;

    public ToastWindow() => InitializeComponent();

    public ToastWindow(string heading, string message, Action? open, string openLabel = "Open") : this()
    {
        Heading.Text = heading;
        Body.Text = message;
        OpenButton.Content = openLabel;
        OpenButton.IsVisible = open is not null;
        _open = open;

        Opened += (_, _) =>
        {
            // The height is only known once the content has been measured.
            Dispatcher.UIThread.Post(Place, DispatcherPriority.Loaded);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        };
    }

    private void Place()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
            return;
        var wa = screen.WorkingArea;
        var scale = screen.Scaling;
        var size = FrameSize ?? ClientSize;
        var w = (int)(size.Width * scale);
        var h = (int)(size.Height * scale);
        Position = new PixelPoint(wa.Right - w - (int)(8 * scale), wa.Bottom - h - (int)(8 * scale));
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        _open?.Invoke();
        Close();
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close();
}
