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
        StepSummary = leaves.Count == 0 ? "" : $"{leaves.Count(s => s.Done)}/{leaves.Count} steps";
    }

    public Action<Guid>? OnOpen { get; set; }

    [RelayCommand]
    private void Open() => OnOpen?.Invoke(Id);

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
    IReadOnlyList<MoonState> Moons, IReadOnlyList<Guid> DependsOn);

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

    public void Refresh()
    {
        var data = _main.Data;
        var today = MainViewModel.Today;
        var rows = data.Goals
            .Where(g => !g.Archived && !Progress.IsComplete(g, data.Contributions))
            .Select(g => new GoalRowViewModel(data, g, today))
            .ToList();

        foreach (var row in rows)
            row.OnOpen = Open;

        Groups.Clear();
        foreach (var group in rows
                     .OrderBy(r => r.Horizon).ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                     .GroupBy(r => r.Horizon))
            Groups.Add(new GoalGroup(Horizons.Label(group.Key), group.ToList()));

        Bodies = rows
            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(row => new OrbitBody(row.Id, row.Title, row.AccentColor, row.Role, row.Horizon, row.Pressure,
                row.HasFraction ? row.Fraction / 100 : null, row.Subtitle, row.Headline, row.Moons, row.DependsOnIds))
            .ToList();

        TodayCount = Planner.Suggest(data, new PlanRequest(today, data.Settings.MaxToday, data.Settings.Bias, data.Settings.LowEnergy)).Count;

        Flow = FlowGraph.Build(data, today, g => Progress.IsComplete(g, data.Contributions));
        GraphIsEmpty = Flow.Nodes.Count == 0;
        GoalColors = data.Goals.ToDictionary(g => g.Id, g => Palette.ColorFor(data.CategoryOf(g)));

        DoneToday = data.Contributions.Where(c => c.Day == today).Select(c => c.GoalId).Distinct().Count();

        IsEmpty = rows.Count == 0;
        var now = rows.Count(r => r.Horizon == Horizon.Now);
        Summary = IsEmpty ? "" : $"{rows.Count} goals · {now} wanting you now";
    }
}
