using System.Diagnostics;

namespace Orrery.App;

/// <summary>
/// Orrery installs itself the way str-amp does: the first run from anywhere (Downloads, Desktop) copies the app into
/// %LocalAppData%\Orrery, adds a Start menu shortcut and hands over to that copy. Updates replace the files there.
/// Your data stays where it always was, in %AppData%\Orrery, and is never touched.
/// </summary>
public static class Install
{
    private const string ShortcutDescription = "Orrery goal tracker";

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orrery");

    public static string? ExePath
    {
        get
        {
            var p = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(p) && !string.Equals(Path.GetFileName(p), "dotnet.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p)
                ? Path.GetFullPath(p)
                : null;
        }
    }

    private static bool Same(string a, string b) => string.Equals(
        Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    /// <summary>True once the installed copy has been started, meaning this process should exit at once.</summary>
    public static bool TryRelocate(string[] args)
    {
        if (!OperatingSystem.IsWindows() || ExePath is not { } exe || Path.GetDirectoryName(exe) is not { } here || Same(here, Directory))
            return false;
        try
        {
            Copy(here, Directory);
            var installed = Path.Combine(Directory, Path.GetFileName(exe));
            if (!File.Exists(installed))
                return false;
            SetStartMenuShortcut(installed);
            Process.Start(new ProcessStartInfo
            {
                FileName = installed,
                Arguments = string.Join(' ', args.Select(a => $"\"{a}\"")),
                UseShellExecute = true,
                WorkingDirectory = Directory,
            });
            return true;
        }
        catch
        {
            return false; // no write access or similar: just run where we are
        }
    }

    private static void Copy(string from, string to)
    {
        System.IO.Directory.CreateDirectory(to);
        foreach (var file in System.IO.Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        foreach (var dir in System.IO.Directory.EnumerateDirectories(from))
            Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    /// <summary>Keeps the Start menu entry pointing at the installed copy.</summary>
    public static void SetStartMenuShortcut(string target)
    {
        object? shell = null, link = null;
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (string.IsNullOrEmpty(dir) || type is null || (shell = Activator.CreateInstance(type)) is null)
                return;
            dynamic s = shell;
            link = s.CreateShortcut(Path.Combine(dir, "Orrery.lnk"));
            dynamic l = link;
            l.TargetPath = target;
            l.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            l.Description = ShortcutDescription;
            l.IconLocation = $"{target},0";
            l.Save();
        }
        catch
        {
            // a missing shortcut is not worth failing over
        }
        finally
        {
            foreach (var o in new[] { link, shell })
                if (o is not null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(o);
        }
    }

    /// <summary>
    /// Replaces the installed files with an unpacked update. This process cannot overwrite its own exe, so a small
    /// hidden PowerShell script waits for it to exit, copies the new files over, and starts Orrery again.
    /// </summary>
    public static bool TryApplyUpdate(string staged, out string error)
    {
        error = "";
        if (ExePath is not { } exe || Path.GetDirectoryName(exe) is not { } target)
        {
            error = "Could not find the running app.";
            return false;
        }
        if (!System.IO.Directory.Exists(staged) || !System.IO.Directory.EnumerateFileSystemEntries(staged).Any())
        {
            error = "The downloaded update is empty.";
            return false;
        }
        try
        {
            var script = Path.Combine(Path.GetTempPath(), $"orrery-update-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(script, $$"""
                param([int]$ProcessId)
                try { Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction SilentlyContinue } catch {}
                Start-Sleep -Milliseconds 500
                $attempt = 0
                do {
                    robocopy "{{staged}}" "{{target}}" /E /IS /IT /R:2 /W:1 | Out-Null
                    $code = $LASTEXITCODE
                    $attempt++
                } while ($code -ge 8 -and $attempt -lt 3)
                Start-Process -FilePath "{{exe}}"
                Remove-Item -LiteralPath "{{staged}}" -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
                """);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{script}\" -ProcessId {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
