using System.Text.Json;
using System.Text.Json.Serialization;
using Orrery.Core.Models;

namespace Orrery.Core.Storage;

/// <summary>Everything lives in one JSON file in the user's app-data folder. No accounts, no network.</summary>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orrery", "orrery.json");

    /// <summary>Null when there is no file yet, so the caller can decide to offer a fresh start.</summary>
    public static OrreryData? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<OrreryData>(stream, Options);
    }

    /// <summary>Writes to a temp file first, so a crash mid-save can't leave a half-written file behind.</summary>
    public static void Save(OrreryData data, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        File.Move(temp, path, overwrite: true);
    }
}
