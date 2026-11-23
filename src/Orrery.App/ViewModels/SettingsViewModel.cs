using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Storage;

namespace Orrery.App.ViewModels;

/// <summary>
/// What Orrery may interrupt you about, and how often. Every reminder kind has its own switch and its own "again
/// after", so you can have deadlines nag every couple of hours and the things you love only once a day.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private static readonly double[] Repeats = [1, 2, 4, 8, 24];
    private static readonly int[] PerDay = [1, 2, 3, 4, 6, 8, 12];
    private static readonly int[] ListSizes = [3, 4, 5, 6, 8, 10];
    private static readonly int[] DayHours = [1, 2, 3, 4, 5, 6, 8];

    private readonly MainViewModel _main;
    private bool _loading;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        RepeatNames = Repeats.Select(h => h switch { 1 => "Every hour", 24 => "Once a day", _ => $"Every {h:0} hours" }).ToList();
        PerDayNames = PerDay.Select(n => n == 1 ? "1 reminder" : $"{n} reminders").ToList();
        ListSizeNames = ListSizes.Select(n => $"{n} things").ToList();
        DayLengthNames = DayHours.Select(h => h == 1 ? "1 hour" : $"{h} hours").ToList();
        FromNames = Hours(6, 12).Select(Clock).ToList();
        UntilNames = Hours(17, 24).Select(Clock).ToList();
        GentleNames = Hours(12, 21).Select(Clock).ToList();
        Reload();
    }

    public IReadOnlyList<string> RepeatNames { get; }
    public IReadOnlyList<string> PerDayNames { get; }
    public IReadOnlyList<string> ListSizeNames { get; }
    public IReadOnlyList<string> DayLengthNames { get; }
    public IReadOnlyList<string> FromNames { get; }
    public IReadOnlyList<string> UntilNames { get; }
    public IReadOnlyList<string> GentleNames { get; }

    public string DataPath => JsonStore.DefaultPath;

    [ObservableProperty] private bool _remindersOn;
    [ObservableProperty] private bool _deadlines;
    [ObservableProperty] private bool _evening;
    [ObservableProperty] private bool _passions;
    [ObservableProperty] private int _deadlineRepeat;
    [ObservableProperty] private int _eveningRepeat;
    [ObservableProperty] private int _passionRepeat;
    [ObservableProperty] private int _perDayIndex;
    [ObservableProperty] private int _fromIndex;
    [ObservableProperty] private int _untilIndex;
    [ObservableProperty] private int _gentleIndex;
    [ObservableProperty] private int _listSizeIndex;
    [ObservableProperty] private int _dayLengthIndex;
    [ObservableProperty] private bool _soundOn;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SoundLabel), nameof(IsCustomSound))] private string _soundName = "";
    [ObservableProperty] private string _soundError = "";

    public bool IsCustomSound => SoundName.Length > 0;
    public string SoundLabel => SoundName.Length > 0 ? SoundName : "Built-in chime";

    private static IEnumerable<double> Hours(double from, double to)
    {
        for (var h = from; h <= to + 0.001; h += 0.5)
            yield return h;
    }

    private static string Clock(double hour)
    {
        var h = (int)hour % 24;
        var minutes = hour - Math.Floor(hour) >= 0.5 ? "30" : "00";
        var suffix = h < 12 ? "AM" : "PM";
        var twelve = h % 12 == 0 ? 12 : h % 12;
        return hour >= 24 ? "Midnight" : $"{twelve}:{minutes} {suffix}";
    }

    private static int Nearest(IEnumerable<double> options, double value)
    {
        var list = options.ToList();
        var best = 0;
        for (var i = 1; i < list.Count; i++)
            if (Math.Abs(list[i] - value) < Math.Abs(list[best] - value))
                best = i;
        return best;
    }

    /// <summary>Reads the stored settings into the form, without writing anything back.</summary>
    public void Reload()
    {
        _loading = true;
        var s = _main.Data.Settings;
        RemindersOn = s.Nudges;
        Deadlines = s.NudgeDeadlines;
        Evening = s.NudgeEvening;
        Passions = s.NudgePassions;
        DeadlineRepeat = Nearest(Repeats, s.NudgeRepeatUrgentHours);
        EveningRepeat = Nearest(Repeats, s.NudgeRepeatGentleHours);
        PassionRepeat = Nearest(Repeats, s.NudgeRepeatPassionHours);
        PerDayIndex = Nearest(PerDay.Select(n => (double)n), s.NudgeMaxPerDay);
        FromIndex = Nearest(Hours(6, 12), s.NudgeFromHour);
        UntilIndex = Nearest(Hours(17, 24), s.NudgeUntilHour);
        GentleIndex = Nearest(Hours(12, 21), s.NudgeGentleFromHour);
        ListSizeIndex = Nearest(ListSizes.Select(n => (double)n), s.MaxToday);
        DayLengthIndex = Nearest(DayHours.Select(h => h * 60.0), s.DayCapacityMinutes);
        SoundOn = s.NotifySound;
        SoundName = s.NotifySoundName;
        SoundError = "";
        _loading = false;
    }

    private void Save(Action<Core.Models.Settings> change)
    {
        if (_loading)
            return;
        change(_main.Data.Settings);
        JsonStore.Save(_main.Data);
    }

    partial void OnRemindersOnChanged(bool value) => Save(s => s.Nudges = value);
    partial void OnDeadlinesChanged(bool value) => Save(s => s.NudgeDeadlines = value);
    partial void OnEveningChanged(bool value) => Save(s => s.NudgeEvening = value);
    partial void OnPassionsChanged(bool value) => Save(s => s.NudgePassions = value);
    partial void OnDeadlineRepeatChanged(int value) => Save(s => s.NudgeRepeatUrgentHours = Repeats[Math.Clamp(value, 0, Repeats.Length - 1)]);
    partial void OnEveningRepeatChanged(int value) => Save(s => s.NudgeRepeatGentleHours = Repeats[Math.Clamp(value, 0, Repeats.Length - 1)]);
    partial void OnPassionRepeatChanged(int value) => Save(s => s.NudgeRepeatPassionHours = Repeats[Math.Clamp(value, 0, Repeats.Length - 1)]);
    partial void OnPerDayIndexChanged(int value) => Save(s => s.NudgeMaxPerDay = PerDay[Math.Clamp(value, 0, PerDay.Length - 1)]);
    partial void OnFromIndexChanged(int value) => Save(s => s.NudgeFromHour = Hours(6, 12).ElementAt(Math.Clamp(value, 0, FromNames.Count - 1)));
    partial void OnUntilIndexChanged(int value) => Save(s => s.NudgeUntilHour = Hours(17, 24).ElementAt(Math.Clamp(value, 0, UntilNames.Count - 1)));
    partial void OnGentleIndexChanged(int value) => Save(s => s.NudgeGentleFromHour = Hours(12, 21).ElementAt(Math.Clamp(value, 0, GentleNames.Count - 1)));

    partial void OnDayLengthIndexChanged(int value)
    {
        Save(s => s.DayCapacityMinutes = DayHours[Math.Clamp(value, 0, DayHours.Length - 1)] * 60);
        if (!_loading)
            _main.RefreshAll();
    }

    partial void OnListSizeIndexChanged(int value)
    {
        Save(s => s.MaxToday = ListSizes[Math.Clamp(value, 0, ListSizes.Length - 1)]);
        if (!_loading)
            _main.RefreshAll();
    }

    partial void OnSoundOnChanged(bool value) => Save(s => s.NotifySound = value);

    [RelayCommand]
    private async Task ChooseSound()
    {
        if (_main.PickSoundFile is null || await _main.PickSoundFile() is not { } path)
            return;
        var error = Chime.Import(_main.Data.Settings, path);
        SoundError = error ?? "";
        if (error is null)
        {
            SoundName = _main.Data.Settings.NotifySoundName;
            JsonStore.Save(_main.Data);
            Chime.Play(_main.Data.Settings);
        }
    }

    [RelayCommand]
    private void UseDefaultSound()
    {
        Chime.UseDefault(_main.Data.Settings);
        SoundName = "";
        SoundError = "";
        JsonStore.Save(_main.Data);
        Chime.Play(_main.Data.Settings);
    }

    [RelayCommand]
    private void PlaySound() => Chime.Play(_main.Data.Settings);

    [RelayCommand]
    private void TestReminder() => _main.SendTestReminder?.Invoke();

    [RelayCommand]
    private void OpenDataFolder() => _main.OpenDataFolder();
}
