using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

/// <summary>
/// One thing on today's list. Each kind of fact has its own slot (what it is, what it asks of you, what it unlocks),
/// so a row never has to squash a deadline and a count of waiting steps into one sentence.
/// </summary>
public partial class PlanRowViewModel : ObservableObject
{
    private readonly PlanItem _item;
    private readonly MainViewModel _main;

    public PlanRowViewModel(PlanItem item, Goal goal, MainViewModel main)
    {
        _item = item;
        _main = main;
        Key = PlanOrder.KeyOf(item);
        Title = item.Title;
        GoalTitle = item.StepId is null ? "" : item.GoalTitle;
        CategoryName = item.Category?.Name ?? "";
        Accent = Palette.For(item.Category);
        IsPassion = !PlanOrder.IsResponsibility(item);

        DemandNote = item.DemandNote;
        UnlockNote = item.Unlocks switch { 0 => "", 1 => "1 step is waiting on this", _ => $"{item.Unlocks} steps are waiting on this" };
        var energy = item.Energy switch { Energy.Low => "low energy", Energy.High => "high energy", _ => "medium energy" };
        var time = item.EstimateMinutes is { } m ? $"~{m} min" : "";
        Meta = string.Join(" · ", new[] { energy, time }.Where(s => s.Length > 0));

        ShowAmount = goal.Measure.Kind is MeasureKind.Amount or MeasureKind.Time;
        UnitLabel = goal.Measure.UnitLabel;
        var suggested = goal.Measure.Kind == MeasureKind.Time
            ? item.EstimateMinutes ?? 25
            : Math.Max(1, Math.Round(item.Demand.Remaining));
        AmountText = suggested.ToString("0.#", CultureInfo.InvariantCulture);
        CheckTip = ShowAmount ? $"Log {AmountText} {UnitLabel}".Trim() : "Mark done";
        CanStart = !main.Focus.IsVisible;
    }

    public Guid Key { get; }
    public string Title { get; }
    public string GoalTitle { get; }
    public string CategoryName { get; }
    public IBrush Accent { get; }
    public bool IsPassion { get; }

    /// <summary>What it asks of you today: "finish by Dec 21", "due today". Empty when it asks nothing.</summary>
    public string DemandNote { get; }
    public bool HasDemand => DemandNote.Length > 0;

    /// <summary>What finishing it opens up. A separate fact from the deadline, shown separately.</summary>
    public string UnlockNote { get; }
    public bool HasUnlock => UnlockNote.Length > 0;

    public string Meta { get; }
    public bool HasGoalTitle => GoalTitle.Length > 0;
    public bool ShowAmount { get; }
    public string UnitLabel { get; }
    public string CheckTip { get; }
    public bool CanStart { get; }

    [ObservableProperty] private string _amountText = "";
    [ObservableProperty] private bool _isLogging;

    // Where it sits in the list. Set by the page once the whole list is arranged.
    [ObservableProperty] private bool _canMoveUp;
    [ObservableProperty] private bool _canMoveDown;
    [ObservableProperty] private string _upTip = "Do this earlier";
    [ObservableProperty] private string _downTip = "Do this later";
    [ObservableProperty] private bool _startsPassions;

    [RelayCommand]
    private void Complete()
    {
        var amount = 1.0;
        if (ShowAmount && !double.TryParse(AmountText, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            amount = 0;
        _main.Complete(_item, amount);
    }

    [RelayCommand]
    private void ToggleLog() => IsLogging = !IsLogging;

    [RelayCommand]
    private void Start() => _main.StartFocus(_item.GoalId, _item.StepId);

    [RelayCommand]
    private void Inspect() => _main.Inspect(_item.GoalId, _item.StepId);

    [RelayCommand]
    private void MoveUp() => _main.TodayPage.Move(Key, -1);

    [RelayCommand]
    private void MoveDown() => _main.TodayPage.Move(Key, +1);
}

public sealed class StreakCardViewModel
{
    public StreakCardViewModel(Category category, StreakResult streak)
    {
        Name = category.Name;
        Accent = Palette.For(category);
        Current = streak.Current;
        Best = streak.Best;
        TodayMet = streak.TodayMet;
        Grace = streak.GraceAvailable
            ? "grace day ready"
            : $"grace used {streak.LastGraceUsed:MMM d}";
    }

    public string Name { get; }
    public IBrush Accent { get; }
    public int Current { get; }
    public int Best { get; }
    public bool TodayMet { get; }
    public string Grace { get; }
    public string Detail => $"best {Best} · {Grace}";
}

public partial class TodayViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _loading = true;
    private List<PlanItem> _arranged = [];

    public TodayViewModel(MainViewModel main)
    {
        _main = main;
        _bias = main.Data.Settings.Bias;
        _lowEnergy = main.Data.Settings.LowEnergy;
        _loading = false;
    }

    /// <summary>The single most useful thing to do next, shown large.</summary>
    [ObservableProperty]
    private PlanRowViewModel? _hero;

    public ObservableCollection<PlanRowViewModel> Rest { get; } = [];
    public ObservableCollection<StreakCardViewModel> Streaks { get; } = [];
    public ObservableCollection<CalendarItemViewModel> Upcoming { get; } = [];

    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _subheading = "";
    [ObservableProperty] private bool _hasRest;
    [ObservableProperty] private bool _hasHero;
    [ObservableProperty] private double _dayProgress;
    [ObservableProperty] private bool _hasDayProgress;
    [ObservableProperty] private double _bias;
    [ObservableProperty] private bool _lowEnergy;
    [ObservableProperty] private bool _showTuning;
    [ObservableProperty] private int _toGo;
    [ObservableProperty] private bool _hasToGo;

    // Which of the "nothing to show" states applies. At most one is true.
    [ObservableProperty] private bool _isFirstRun;
    [ObservableProperty] private bool _isAllDone;
    [ObservableProperty] private bool _isQuiet;
    [ObservableProperty] private bool _isLowEnergyEmpty;
    [ObservableProperty] private bool _hasUpcoming;
    [ObservableProperty] private string _doneSummary = "";

    public bool ShowControls => !IsFirstRun;

    public string BiasLabel => Bias switch
    {
        <= -0.66 => "Obligations first",
        <= -0.2 => "Leaning obligations",
        < 0.2 => "Balanced",
        < 0.66 => "Leaning passions",
        _ => "Passions first",
    };

    partial void OnBiasChanged(double value)
    {
        OnPropertyChanged(nameof(BiasLabel));
        Apply();
    }

    partial void OnLowEnergyChanged(bool value) => Apply();

    private void Apply()
    {
        if (_loading)
            return;
        _main.Data.Settings.Bias = Bias;
        _main.Data.Settings.LowEnergy = LowEnergy;
        _main.Changed();
    }

    [RelayCommand]
    private void NewGoal() => _main.ShowDesignerCommand.Execute(null);

    [RelayCommand]
    private void QuickAdd() => _main.Capture.Open();

    [RelayCommand]
    private void TurnOffLowEnergy() => LowEnergy = false;

    /// <summary>Moves one row earlier or later. A passion can't pass a responsibility, so those moves do nothing.</summary>
    public void Move(Guid key, int delta)
    {
        if (PlanOrder.Move(_arranged, key, delta) is not { } keys)
            return;
        _main.Data.Settings.PlanOrder = keys;
        _main.Data.Settings.PlanOrderDay = MainViewModel.Today;
        _main.Changed();
    }

    /// <summary>Where a dragged row would really land, so the page can show it before the drop.</summary>
    public int ClampTarget(Guid key, int target) => PlanOrder.ClampTarget(_arranged, key, target);

    /// <summary>Drops a row at a position in the list (0 is "up next"). A passion stops short of the responsibilities.</summary>
    public void MoveTo(Guid key, int target)
    {
        if (PlanOrder.MoveTo(_arranged, key, target) is not { } keys)
            return;
        _main.Data.Settings.PlanOrder = keys;
        _main.Data.Settings.PlanOrderDay = MainViewModel.Today;
        _main.Changed();
    }

    public void Refresh()
    {
        var today = MainViewModel.Today;
        var data = _main.Data;
        Heading = today.ToString("dddd, MMM d", CultureInfo.CurrentCulture);

        // An arrangement is for one day only; tomorrow starts from the planner's own order again.
        if (data.Settings.PlanOrderDay != today)
        {
            data.Settings.PlanOrder = [];
            data.Settings.PlanOrderDay = today;
        }

        var plan = Planner.Suggest(data, new PlanRequest(today, data.Settings.MaxToday, Bias, LowEnergy));
        _arranged = PlanOrder.Arrange(plan, data.Settings.PlanOrder);
        var rows = _arranged
            .Select(item => new PlanRowViewModel(item, data.Goals.First(g => g.Id == item.GoalId), _main))
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.CanMoveUp = i > 0 && PlanOrder.Move(_arranged, row.Key, -1) is not null;
            row.CanMoveDown = i < rows.Count - 1 && PlanOrder.Move(_arranged, row.Key, +1) is not null;
            row.UpTip = PlanOrder.WhyNot(_arranged, row.Key, -1) ?? "Do this earlier";
            row.DownTip = PlanOrder.WhyNot(_arranged, row.Key, +1) ?? "Do this later";
            row.StartsPassions = row.IsPassion && i > 0 && !rows[i - 1].IsPassion;
        }

        Hero = rows.FirstOrDefault();
        HasHero = Hero is not null;
        Rest.Clear();
        foreach (var row in rows.Skip(1))
            Rest.Add(row);
        HasRest = Rest.Count > 0;
        ToGo = rows.Count;
        HasToGo = rows.Count > 0;

        var doneToday = data.Contributions.Where(c => c.Day == today).ToList();
        var done = doneToday.Select(c => c.GoalId).Distinct().Count();
        var total = done + rows.Count;
        DayProgress = total == 0 ? 0 : 100.0 * done / total;
        HasDayProgress = total > 0;

        // Exactly one of these explains an empty page, from "you haven't started" to "you've finished".
        var empty = rows.Count == 0;
        IsFirstRun = !data.Goals.Any(g => !g.Archived);
        IsLowEnergyEmpty = empty && !IsFirstRun && LowEnergy;
        IsAllDone = empty && !IsFirstRun && !LowEnergy && done > 0;
        IsQuiet = empty && !IsFirstRun && !LowEnergy && done == 0;
        OnPropertyChanged(nameof(ShowControls));

        var minutes = doneToday.Sum(c => c.Minutes);
        DoneSummary = string.Join(" · ", new[]
        {
            done == 1 ? "1 goal moved" : $"{done} goals moved",
            minutes >= 60 ? $"{minutes / 60:0.#} h focused" : minutes > 0 ? $"{minutes:0} min focused" : "",
        }.Where(s => s.Length > 0));

        Subheading = IsFirstRun ? "Welcome" : empty ? "" : $"{done} done · {rows.Count} to go{(LowEnergy ? " · low-energy mode" : "")}";

        Upcoming.Clear();
        if (empty && !IsFirstRun)
        {
            IBrush BucketOf(Guid goalId) => Palette.For(data.Goals.FirstOrDefault(g => g.Id == goalId) is { } g ? data.CategoryOf(g) : null);
            foreach (var e in CalendarModel.Events(data, today.AddDays(1), today.AddDays(14), today)
                         .Where(e => e.Kind != CalendarKind.Worked).Take(3))
                Upcoming.Add(new CalendarItemViewModel(_main, e, BucketOf(e.GoalId)));
        }
        HasUpcoming = Upcoming.Count > 0;

        Streaks.Clear();
        foreach (var category in data.Categories)
            Streaks.Add(new StreakCardViewModel(category, Core.Engine.Streaks.For(category, data, today)));
    }
}
