using Avalonia.Controls;
using Avalonia.Platform;

namespace Orrery.App.Views;

/// <summary>The logo, for the window and the tray. Each is drawn at about the size it is shown, so nothing is shrunk hard.</summary>
public static class AppIcon
{
    public static WindowIcon Window() => Open("Orrery-64");
    public static WindowIcon Tray() => Open("Orrery-32");

    private static WindowIcon Open(string name) => new(AssetLoader.Open(new Uri($"avares://Orrery/Assets/{name}.png")));
}
