using System.Runtime.InteropServices;
using System.Text;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.App;

/// <summary>
/// The sound a reminder makes. There is a built-in chime (written to the app folder the first time it is needed, so
/// nothing ships as a binary). A custom .wav or .mp3 is copied into the app's own sounds folder when chosen, so it
/// keeps working even if the original is moved or deleted.
/// </summary>
public static class Chime
{
    private const string Alias = "orrery_sound";
    private const long MaxBytes = 12 * 1024 * 1024;

    public static string SoundsDir { get; } = Path.Combine(Path.GetDirectoryName(JsonStore.DefaultPath)!, "sounds");

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? result, int length, IntPtr callback);

    public static bool IsSupported(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3";

    /// <summary>Copies a chosen file into the sounds folder and records it. Returns an error message, or null on success.</summary>
    public static string? Import(Settings settings, string source)
    {
        try
        {
            if (!IsSupported(source))
                return "Pick a .wav or .mp3 file.";
            if (new FileInfo(source).Length > MaxBytes)
                return "That file is too large for a reminder sound (12 MB at most).";

            Directory.CreateDirectory(SoundsDir);
            var stored = $"custom-{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}";
            File.Copy(source, Path.Combine(SoundsDir, stored), overwrite: false);

            RemoveCustom(settings);
            settings.NotifySoundFile = stored;
            settings.NotifySoundName = Path.GetFileName(source);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Could not copy that file.";
        }
    }

    /// <summary>Goes back to the built-in chime and removes the stored copy.</summary>
    public static void UseDefault(Settings settings)
    {
        RemoveCustom(settings);
        settings.NotifySoundFile = "";
        settings.NotifySoundName = "";
    }

    private static void RemoveCustom(Settings settings)
    {
        if (settings.NotifySoundFile.Length == 0)
            return;
        try { File.Delete(Path.Combine(SoundsDir, Path.GetFileName(settings.NotifySoundFile))); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Plays the chosen sound, or the chime if none is chosen or the file has gone missing. Never throws.</summary>
    public static void Play(Settings settings)
    {
        try
        {
            var path = settings.NotifySoundFile.Length > 0
                ? Path.Combine(SoundsDir, Path.GetFileName(settings.NotifySoundFile))
                : "";
            if (path.Length == 0 || !File.Exists(path))
                path = EnsureDefault();
            var type = Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? "mpegvideo" : "waveaudio";
            mciSendString($"close {Alias}", null, 0, IntPtr.Zero);
            if (mciSendString($"open \"{path}\" type {type} alias {Alias}", null, 0, IntPtr.Zero) == 0)
                mciSendString($"play {Alias}", null, 0, IntPtr.Zero);
        }
        catch (Exception e) when (e is IOException or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException)
        {
        }
    }

    private static string EnsureDefault()
    {
        Directory.CreateDirectory(SoundsDir);
        var path = Path.Combine(SoundsDir, "chime.wav");
        if (!File.Exists(path))
            File.WriteAllBytes(path, SynthesizeChime());
        return path;
    }

    /// <summary>Two soft bell notes, a fifth apart, each with a little shimmer and a long fade. About a second.</summary>
    private static byte[] SynthesizeChime()
    {
        const int rate = 44100;
        var samples = new short[(int)(rate * 1.3)];
        void Note(double freq, double start, double gain)
        {
            var first = (int)(start * rate);
            for (var i = first; i < samples.Length; i++)
            {
                var t = (i - first) / (double)rate;
                var attack = Math.Min(1, t / 0.006);
                var envelope = attack * Math.Exp(-t * 4.2);
                var tone = Math.Sin(2 * Math.PI * freq * t)
                           + 0.35 * Math.Sin(2 * Math.PI * freq * 2.01 * t) * Math.Exp(-t * 3)
                           + 0.12 * Math.Sin(2 * Math.PI * freq * 3.98 * t) * Math.Exp(-t * 6);
                var sum = samples[i] + tone * envelope * gain * 9000;
                samples[i] = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
            }
        }
        Note(783.99, 0.0, 0.8);
        Note(1174.66, 0.16, 1.0);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples.Length * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples.Length * 2);
        foreach (var s in samples)
            w.Write(s);
        return ms.ToArray();
    }
}
