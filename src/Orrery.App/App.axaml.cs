using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Orrery.App.ViewModels;
using Orrery.App.Views;
using Orrery.Core.Storage;

namespace Orrery.App;

public partial class App : Application
{
    /// <summary>Closing the window hides Orrery to the tray so nudges keep working; only "Quit" really exits.</summary>
    public static bool Quitting { get; private set; }

    public const string ShowEventName = "Orrery.ShowWindow";

    private MainViewModel? _main;
    private NudgeHost? _nudges;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _main = new MainViewModel();
            var window = new MainWindow { DataContext = _main, Icon = AppIcon.Create() };
            desktop.MainWindow = window;
            window.HiddenToTray += OnHiddenToTray;

            _nudges = new NudgeHost(_main, (goal, step) =>
            {
                ShowMain();
                _main.Inspect(goal, step);
            });
            _nudges.Start();
            _main.PickSoundFile = async () =>
            {
                var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Choose a reminder sound",
                    AllowMultiple = false,
                    FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("Sound (.wav, .mp3)") { Patterns = ["*.wav", "*.mp3"] }],
                });
                return files.Count > 0 ? files[0].Path.LocalPath : null;
            };
            _main.SendTestReminder = () => _nudges.Show("Reminder", "This is what a reminder looks like.", null);
            SetUpTray();

            // A second launch signals this event; bring the window forward.
            var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            new Thread(() =>
            {
                while (show.WaitOne())
                    Avalonia.Threading.Dispatcher.UIThread.Post(ShowMain);
            }) { IsBackground = true }.Start();

            _main.Updates.Found = u => _nudges.Show("Update available", $"Orrery {u.Version} is ready to install.",
                () => { ShowMain(); _ = _main.Updates.InstallAsync(); }, "Install");
            _ = _main.Updates.CheckQuietlyAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ShowMain()
    {
        if (_desktop?.MainWindow is not { } w)
            return;
        w.Show();
        if (w.WindowState == WindowState.Minimized)
            w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public void QuitApp()
    {
        Quitting = true;
        _desktop?.Shutdown();
    }

    private void OnHiddenToTray()
    {
        if (_main is null || _main.Data.Settings.TrayHintShown)
            return;
        _main.Data.Settings.TrayHintShown = true;
        JsonStore.Save(_main.Data);
        _nudges?.Show("Still running",
            "Orrery stays in the tray so it can remind you about things that are due. Right-click the tray icon to quit.", null);
    }

    private string NudgeLabel() => _main!.RemindersLabel;

    private void SetUpTray()
    {
        if (_main is null)
            return;

        var open = new NativeMenuItem("Open Orrery");
        open.Click += (_, _) => ShowMain();

        var nudgeToggle = new NativeMenuItem(NudgeLabel());
        nudgeToggle.Click += (_, _) =>
        {
            _main.Data.Settings.Nudges = !_main.Data.Settings.Nudges;
            JsonStore.Save(_main.Data);
            _main.SettingsPage.Reload();
            nudgeToggle.Header = NudgeLabel();
        };

        var quit = new NativeMenuItem("Quit Orrery");
        quit.Click += (_, _) => QuitApp();

        var tray = new TrayIcon
        {
            Icon = AppIcon.Create(),
            ToolTipText = "Orrery",
            Menu = new NativeMenu { Items = { open, nudgeToggle, new NativeMenuItemSeparator(), quit } },
        };
        tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }
}
