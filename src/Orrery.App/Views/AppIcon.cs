using Avalonia.Controls;
using Avalonia.Platform;

namespace Orrery.App.Views;

/// <summary>The logo, for the window and the tray.</summary>
public static class AppIcon
{
    public static WindowIcon Create() => new(AssetLoader.Open(new Uri("avares://Orrery/Assets/Orrery.png")));
}
