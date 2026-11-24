using Avalonia;

namespace Orrery.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if !DEBUG
        // Run from wherever the zip was unpacked: install into AppData and carry on from there.
        if (Install.TryRelocate(args))
            return;
#endif
        // One Orrery at a time: a second launch just asks the first one to come forward.
        using var mutex = new Mutex(true, "Orrery.SingleInstance", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(App.ShowEventName).Set(); } catch { }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
