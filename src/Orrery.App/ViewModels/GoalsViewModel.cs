using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public sealed class StepRowViewModel
{
    public StepRowViewModel(Step step, DependencyGraph graph, DateOnly today)
    {
        Title = step.Title;
        Done = step.Done;
        var blocked = step.Done ? null : graph.BlockedReason(step, today);
        BlockedText = blocked ?? "";
        IsBlocked = blocked is not null;
    }

    public string Title { get; }
    public bool Done { get; }
    public bool IsBlocked { get; }
    public string BlockedText { get; }
}

/// <summary>A goal's summary, shared by the list, the orbit tooltips and the inspector.</summary>
public partial class GoalRowViewModel : ObservableObject
{
    public GoalRowViewModel(OrreryData data, Goal goal, DateOnly today)
    {
        Id = goal.Id;
        IsArchived = goal.Archived;
        var category = data.CategoryOf(goal);
        Role = category?.Role ?? CategoryRole.Obligation;
        Title = goal.Title;
        CategoryName = category?.Name ?? "";
        Accent = Palette.For(category);
        AccentColor = Palette.ColorFor(category);

        var log = data.Contributions;
        var fraction = Progress.Fraction(goal, log);
        // A plain one-off with no steps is just done or not. A ring stuck at 0% says nothing, so it gets words instead.
        var plainDone = goal.Measure.Kind == MeasureKind.Done && !goal.Leaves().Any();
        HasFraction = fraction is not null && !plainDone;
        Fraction = (fraction ?? 0) * 100;

        var unit = goal.Measure.UnitLabel;
        var current = Progress.Current(goal, log);
        var week = Progress.Between(goal, log, today.AddDays(-6), today);
        ProgressText = fraction switch
        {
            not null when plainDone => Progress.IsComplete(goal, log) ? "Done" : "Not done yet",
            not null when Progress.Target(goal) > 0 =>
                $"{Planner.Format(current)} / {Planner.Format(Progress.Target(goal))} {unit}".Replace("  ", " ").Trim()
                + $" · {Math.Round(fraction.Value * 100)}%",
            not null => current > 0 ? "Done" : "Not done yet",
            _ when goal.Measure.Kind == MeasureKind.Done => week > 0 ? "Done recently" : "Not done recently",
            _ => $"{Planner.Format(week)} {unit} in the last 7 days".Replace("  ", " "),
        };

        var parts = new List<string>();
        parts.Add(goal.Rhythm.Kind switch
        {
            RhythmKind.Repeat when goal.Rhythm.EveryDays <= 1 => "every day",
            RhythmKind.Repeat => $"every {goal.Rhythm.EveryDays} days",
            RhythmKind.Cadence => $"{Planner.Format(goal.Rhythm.PerWeek)} {(unit.Length > 0 ? unit : "times")} a week",
            _ => goal.HasFinishLine ? "has a finish line" : "open-ended",
        });
        if (goal.Deadline is { } due)
            parts.Add($"due {due:MMM d}");
        Subtitle = string.Join(" · ", parts);

        var demand = Demands.For(goal, log, today);
        DemandText = demand.IsDemanding ? Planner.DescribeDemand(goal, demand) : "";
        Pressure = Planner.Pressure(goal, log, today, demand);
        Horizon = Horizons.Of(goal, log, today);

        var graph = new DependencyGraph(data.Goals, g => Progress.IsComplete(g, log));
        Steps = new ObservableCollection<StepRowViewModel>(goal.Leaves().Select(s => new StepRowViewModel(s, graph, today)));
        BlockedText = graph.BlockedReason(goal, today) ?? "";
        IsComplete = Progress.IsComplete(goal, log);

        var units = goal.Roots().Where(goal.IsSection).ToList();
        Moons = (units.Count > 0 ? units : goal.Leaves().ToList()).Take(8)
            .Select(u => goal.IsDone(u) ? MoonState.Done : graph.BlockedReason(u, today) is not null ? MoonState.Blocked : MoonState.Open)
            .ToList();
        DependsOnIds = goal.DependsOnGoals.ToList();

        var leaves = goal.Leaves().ToList();
        var remaining = leaves.Count > 0
            ? leaves.Where(s => !s.Done).Sum(s => s.EstimateMinutes ?? 30)
            : goal.Rhythm is { Kind: RhythmKind.Cadence, PerWeek: > 0 } ? goal.Rhythm.PerWeek : 45;
        Effort = Math.Clamp(Math.Log(1 + remaining / 30.0) / Math.Log(1 + 600 / 30.0), 0, 1);
        StepSummary = leaves.Count == 0 ? "" : $"{leaves.Count(s => s.Done)}/{leaves.Count} steps";
    }

    public Action<Guid>? OnOpen { get; set; }
    public Action<Guid>? OnEdit { get; set; }
    public Action<Guid>? OnArchive { get; set; }
    public Action<Guid>? OnRestore { get; set; }
    public Action<Guid>? OnDelete { get; set; }

    [RelayCommand]
    private void Open() => OnOpen?.Invoke(Id);

    [RelayCommand]
    private void Edit() => OnEdit?.Invoke(Id);

    [RelayCommand]
    private void Archive() => OnArchive?.Invoke(Id);

    [RelayCommand]
    private void Restore() => OnRestore?.Invoke(Id);

    [RelayCommand]
    private void Delete() => OnDelete?.Invoke(Id);

    /// <summary>How much work is left, 0 to 1 on a log scale (about ten hours is the top). Drives a planet's size.</summary>
    public double Effort { get; }

    public bool IsArchived { get; }
    public bool CanArchive => !IsArchived;

    public Guid Id { get; }
    public CategoryRole Role { get; }
    public string Title { get; }
    public string CategoryName { get; }
    public IBrush Accent { get; }
    public Color AccentColor { get; }
    public bool HasFraction { get; }
    public double Fraction { get; }
    public string ProgressText { get; }
    public string Subtitle { get; }
    public string DemandText { get; }
    public bool HasDemand => DemandText.Length > 0;
    public string BlockedText { get; }
    public bool IsBlocked => BlockedText.Length > 0;
    public bool IsComplete { get; }
    public double Pressure { get; }
    public Horizon Horizon { get; }
    public string StepSummary { get; }
    public IReadOnlyList<MoonState> Moons { get; }
    public IReadOnlyList<Guid> DependsOnIds { get; }
    public ObservableCollection<StepRowViewModel> Steps { get; }
    public bool HasSteps => Steps.Count > 0;

    /// <summary>The line shown under a title in lists and orbit tooltips.</summary>
    public string Headline => HasDemand ? DemandText : ProgressText;
}

public sealed record GoalGroup(string Label, IReadOnlyList<GoalRowViewModel> Rows);

public enum MoonState { Done, Open, Blocked }

/// <summary>One goal as drawn on the orbit map. Moons are its sections (or steps); links are the goals it waits for.</summary>
public sealed record OrbitBody(
    Guid Id, string Title, Color Accent, CategoryRole Role, Horizon Horizon,
    double Pressure, double? Fraction, string Line1, string Line2,
    IReadOnlyList<MoonState> Moons, IReadOnlyList<Guid> DependsOn, double Effort = 0.3, int TodayItems = 0);

public partial class GoalsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public GoalsViewModel(MainViewModel main)
    {
        _main = main;
        _mode = main.Data.Settings.GoalsMode;
    }

    public ObservableCollection<GoalGroup> Groups { get; } = [];

    /// <summary>A new list every refresh, so the orbit map sees a change and redraws.</summary>
    [ObservableProperty]
    private IReadOnlyList<OrbitBody> _bodies = [];

    /// <summary>0 = orbit map, 1 = list, 2 = dependency graph.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsOrbit), nameof(AsList), nameof(AsGraph))]
    private int _mode;

    [ObservableProperty]
    private Orrery.Core.Engine.FlowLayout? _flow;

    [ObservableProperty]
    private IReadOnlyDictionary<Guid, Color> _goalColors = new Dictionary<Guid, Color>();

    [ObservableProperty]
    private bool _graphIsEmpty;

    [ObservableProperty]
    private string _flowSummary = "";

    /// <summary>Days ahead the orbit map is showing. Zero is today.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LookAheadText), nameof(IsLookingAhead))]
    private double _lookAhead;

    public bool IsLookingAhead => LookAhead >= 1;

    public string LookAheadText => LookAhead < 1 ? "" :
        $"In {(int)LookAhead} days · {MainViewModel.Today.AddDays((int)LookAhead):MMM d}";

    partial void OnLookAheadChanged(double value)
    {
        var rounded = Math.Round(value);
        if (rounded != value)
        {
            LookAhead = rounded;
            return;
        }
        Refresh();
    }

    [RelayCommand]
    private void ResetLookAhead() => LookAhead = 0;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private int _todayCount;

    [ObservableProperty]
    private int _doneToday;

    [ObservableProperty]
    private Guid? _selectedId;

    [ObservableProperty]
    private string _summary = "";

    public bool AsOrbit => Mode == 0;
    public bool AsList => Mode == 1;
    public bool AsGraph => Mode == 2;

    partial void OnModeChanged(int value)
    {
        _main.Data.Settings.GoalsMode = value;
        Core.Storage.JsonStore.Save(_main.Data);
    }

    [RelayCommand]
    private void ShowOrbit() => Mode = 0;

    [RelayCommand]
    private void ShowList() => Mode = 1;

    [RelayCommand]
    private void ShowGraph() => Mode = 2;

    [RelayCommand]
    private void OpenNode(FlowNode node)
    {
        SelectedId = node.GoalId;
        _main.Inspect(node.GoalId, node.StepId);
    }

    [RelayCommand]
    private void Open(Guid id)
    {
        SelectedId = id;
        _main.Inspect(id);
    }

    [ObservableProperty]
    private bool _showPutAway;

    [ObservableProperty]
    private int _putAwayCount;

    public bool HasPutAway => PutAwayCount > 0;

    partial void OnPutAwayCountChanged(int value) => OnPropertyChanged(nameof(HasPutAway));

    [RelayCommand]
    private void TogglePutAway()
    {
        ShowPutAway = !ShowPutAway;
        Refresh();
    }

    [RelayCommand]
    private void NewGoal() => _main.ShowDesignerCommand.Execute(null);

    [RelayCommand]
    private void AddStep() => _main.Capture.Open();

    private GoalRowViewModel Wire(GoalRowViewModel row)
    {
        row.OnOpen = Open;
        row.OnEdit = _main.EditGoal;
        row.OnArchive = _main.ArchiveGoal;
        row.OnRestore = _main.RestoreGoal;
        row.OnDelete = _main.DeleteGoal;
        return row;
    }

    public void Refresh()
    {
        var data = _main.Data;
        var today = MainViewModel.Today;
        var live = data.Goals.Where(g => !g.Archived).Select(g => Wire(new GoalRowViewModel(data, g, today))).ToList();
        var rows = live.Where(r => !r.IsComplete).ToList();
        var finished = live.Where(r => r.IsComplete).ToList();
        var putAway = data.Goals.Where(g => g.Archived).Select(g => Wire(new GoalRowViewModel(data, g, today)))
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList();
        PutAwayCount = putAway.Count;
        if (putAway.Count == 0)
            ShowPutAway = false;

        Groups.Clear();
        foreach (var group in rows
                     .OrderBy(r => r.Horizon).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                     .GroupBy(r => r.Horizon))
            Groups.Add(new GoalGroup(Horizons.Label(group.Key), group.ToList()));
        if (finished.Count > 0)
            Groups.Add(new GoalGroup("FINISHED", finished.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase).ToList()));
        if (ShowPutAway)
            Groups.Add(new GoalGroup("PUT AWAY", putAway));

        // Looking ahead re-reads every goal as if it were that many days from now: rings and pressure both move.
        var ahead = today.AddDays((int)LookAhead);
        var plan = Planner.Suggest(data, new PlanRequest(today, data.Settings.MaxToday, data.Settings.Bias, data.Settings.LowEnergy));
        var bodyRows = LookAhead == 0
            ? rows
            : data.Goals.Where(g => !g.Archived && !Progress.IsComplete(g, data.Contributions))
                .Select(g => new GoalRowViewModel(data, g, ahead)).ToList();
        Bodies = bodyRows
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(row => new OrbitBody(row.Id, row.Title, row.AccentColor, row.Role, row.Horizon, row.Pressure,
                row.HasFraction ? row.Fraction / 100 : null, row.Subtitle, row.Headline, row.Moons, row.DependsOnIds,
                row.Effort, LookAhead == 0 ? plan.Count(p => p.GoalId == row.Id) : 0))
            .ToList();

        TodayCount = plan.Count;

        Flow = FlowGraph.Build(data, today, g => Progress.IsComplete(g, data.Contributions));
        GraphIsEmpty = Flow.Nodes.Count == 0;
        GoalColors = data.Goals.ToDictionary(g => g.Id, g => Palette.ColorFor(data.CategoryOf(g)));
        FlowSummary = Flow.Nodes.Count == 0 ? "" :
            string.Join(" · ", new[]
            {
                Flow.Nodes.Count(n => n.State == NodeState.Open) is > 0 and var r1 ? $"{r1} ready" : null,
                Flow.Nodes.Count(n => n.State == NodeState.Blocked) is > 0 and var w ? $"{w} waiting" : null,
                Flow.Nodes.Count(n => n.State == NodeState.Done) is > 0 and var d ? $"{d} done" : null,
            }.Where(x => x is not null));

        DoneToday = data.Contributions.Where(c => c.Day == today).Select(c => c.GoalId).Distinct().Count();

        IsEmpty = live.Count == 0 && putAway.Count == 0;
        var pressing = rows.Count(r => r.Horizon == Horizon.Now);
        var count = live.Count;
        Summary = IsEmpty ? "" : (count == 1 ? "1 goal" : $"{count} goals")
            + (pressing == 0 ? " · nothing pressing" : $" · {pressing} need{(pressing == 1 ? "s" : "")} you today");
    }
}
