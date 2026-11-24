namespace Orrery.Core.Updates;

/// <summary>The app's own version, bumped by hand to match each GitHub release tag.</summary>
public static class AppVersion
{
    public const string Current = "1.0.0";

    /// <summary>Whether a tag like "v1.2.0" (or "1.2.0") is strictly newer than the running version.</summary>
    public static bool IsNewer(string? tag, string current = Current) =>
        TryParse(tag, out var t) && TryParse(current, out var c) && t > c;

    private static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        return !string.IsNullOrWhiteSpace(text) && Version.TryParse(text.Trim().TrimStart('v', 'V'), out version!);
    }
}
