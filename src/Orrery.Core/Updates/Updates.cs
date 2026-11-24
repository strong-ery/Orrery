using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orrery.Core.Updates;

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

/// <summary>A release newer than the running build.</summary>
public sealed record UpdateInfo(string Version, string Notes, string DownloadUrl);

/// <summary>Asks GitHub whether a newer release exists, and downloads it. Same approach as str-amp: a zip attached to a release.</summary>
public static class Updates
{
    public const string Owner = "strong-ery", Repo = "Orrery", ExeName = "Orrery.App.exe";
    private const string ClientId = "Orrery (https://github.com/strong-ery/Orrery)";

    private static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ClientId);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return http;
    }

    /// <summary>The newer release, or null when up to date. Throws when GitHub cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = Client(TimeSpan.FromSeconds(12));
        using var response = await http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
        if (release is null || release.Draft || release.Prerelease || !AppVersion.IsNewer(release.TagName))
            return null;
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        return asset is null ? null : new UpdateInfo(release.TagName.TrimStart('v', 'V'), release.Body ?? "", asset.BrowserDownloadUrl);
    }

    /// <summary>Downloads the zip and unpacks it to a fresh temp folder; returns the folder that holds the exe.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var root = Path.Combine(Path.GetTempPath(), "orrery-update", update.Version);
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        var zip = Path.Combine(Path.GetTempPath(), $"orrery-update-{update.Version}.zip");

        using (var http = Client(TimeSpan.FromMinutes(5)))
        using (var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total is > 0)
                    progress?.Report((double)done / total.Value);
            }
        }

        try
        {
            ZipFile.ExtractToDirectory(zip, root, true);
        }
        finally
        {
            File.Delete(zip);
        }
        var exe = Directory.EnumerateFiles(root, ExeName, SearchOption.AllDirectories).FirstOrDefault();
        return exe is null ? throw new InvalidDataException("The update did not contain the app.") : Path.GetDirectoryName(exe)!;
    }
}
