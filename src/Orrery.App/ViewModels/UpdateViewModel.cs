using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Storage;
using Orrery.Core.Updates;

namespace Orrery.App.ViewModels;

/// <summary>Looks for a newer release on GitHub, and installs it when asked: download, swap the files, restart.</summary>
public partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(24);
    private readonly MainViewModel _main;
    private UpdateInfo? _pending;

    public UpdateViewModel(MainViewModel main) => _main = main;

    public string Version => $"Orrery {AppVersion.Current}";

    /// <summary>Raised once per new version found by the quiet check, so the app can say so.</summary>
    public Action<UpdateInfo>? Found { get; set; }

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _canInstall;
    [ObservableProperty] private string _installLabel = "";

    /// <summary>On launch: ask at most once a day, and stay quiet unless there is something new.</summary>
    public async Task CheckQuietlyAsync()
    {
        var s = _main.Data.Settings;
        if (s.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < Every)
            return;
        try
        {
            var update = await Updates.CheckAsync();
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            JsonStore.Save(_main.Data);
            if (update is null)
                return;
            Offer(update);
            if (s.AnnouncedUpdate != update.Version)
            {
                s.AnnouncedUpdate = update.Version;
                JsonStore.Save(_main.Data);
                Found?.Invoke(update);
            }
        }
        catch
        {
            // offline: try again next launch
        }
    }

    private void Offer(UpdateInfo update)
    {
        _pending = update;
        InstallLabel = $"Install {update.Version} and restart";
        Status = $"Orrery {update.Version} is available.";
        CanInstall = true;
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (Busy)
            return;
        Busy = true;
        CanInstall = false;
        Status = "Checking…";
        try
        {
            var update = await Updates.CheckAsync();
            _main.Data.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            JsonStore.Save(_main.Data);
            if (update is null)
                Status = "You're up to date.";
            else
                Offer(update);
        }
        catch
        {
            Status = "Couldn't reach GitHub. Check your connection and try again.";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (_pending is not { } update || Busy)
            return;
        Busy = true;
        CanInstall = false;
        try
        {
            Status = "Downloading…";
            var staged = await Updates.DownloadAsync(update, new Progress<double>(p => Status = $"Downloading… {p:P0}"));
            Status = "Installing…";
            if (!Install.TryApplyUpdate(staged, out var error))
            {
                Status = $"Couldn't install the update: {error}";
                CanInstall = true;
                return;
            }
            JsonStore.Save(_main.Data);
            (Avalonia.Application.Current as App)?.QuitApp();
        }
        catch (Exception ex)
        {
            Status = $"Couldn't download the update: {ex.Message}";
            CanInstall = true;
        }
        finally
        {
            Busy = false;
        }
    }
}
