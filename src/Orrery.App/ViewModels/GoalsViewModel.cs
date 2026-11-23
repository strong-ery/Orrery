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
        HasFraction = fraction is not null;
        Fraction = (fraction ?? 0) * 100;

        var unit = goal.Measure.UnitLabel;
        var current = Progress.Current(goal, log);
        var week = Progress.Between(goal, log, today.AddDays(-6), today);
        ProgressText = fraction switch
        {
            not null when Progress.Target(goal) > 0 =>
                $"{Planner.Format(current)} / {Planner.Format(Progress.Target(goal))} {unit}".Replace("  ", " ").Trim()
                + $" · {Math.Round(fraction.Value * 100)}%",
            not null => current > 0 ? "done" : "not done",
            _ when goal.Measure.Kind == MeasureKind.Done => week > 0 ? "done recently" : "not done recently",
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
        Steps = new ObservableCollection<StepRowViewModel>(goal.Steps.Select(s => new StepRowViewModel(s, graph, today)));
        BlockedText = graph.BlockedReason(goal, today) ?? "";
        IsComplete = Progress.IsComplete(goal, log);

        var done = goal.Steps.Count(s => s.Done);
        StepSummary = goal.Steps.Count == 0 ? "" : $"{done}/{goal.Steps.Count} steps";
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
    public ObservableCollection<StepRowViewModel> Steps { get; }
    public bool HasSteps => Steps.Count > 0;

    /// <summary>The line shown under a title in lists and orbit tooltips.</summary>
    public string Headline => HasDemand ? DemandText : ProgressText;
}

public sealed record GoalGroup(string Label, IReadOnlyList<GoalRowViewModel> Rows);

/// <summary>One goal as drawn on the orbit map.</summary>
public sealed record OrbitBody(
    Guid Id, string Title, Color Accent, CategoryRole Role, Horizon Horizon,
    double Pressure, double? Fraction, string Line1, string Line2);

public partial class GoalsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public GoalsViewModel(MainViewModel main)
    {
        _main = main;
        _asList = main.Data.Settings.GoalsAsList;
    }

    public ObservableCollection<GoalGroup> Groups { get; } = [];

    /// <summary>A new list every refresh, so the orbit map sees a change and redraws.</summary>
    [ObservableProperty]
    private IReadOnlyList<OrbitBody> _bodies = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AsOrbit))]
    private bool _asList;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private Guid? _selectedId;

    [ObservableProperty]
    private string _summary = "";

    public bool AsOrbit => !AsList;

    partial void OnAsListChanged(bool value)
    {
        _main.Data.Settings.GoalsAsList = value;
        Core.Storage.JsonStore.Save(_main.Data);
    }

    [RelayCommand]
    private void ShowOrbit() => AsList = false;

    [RelayCommand]
    private void ShowList() => AsList = true;

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
                row.HasFraction ? row.Fraction / 100 : null, row.Subtitle, row.Headline))
            .ToList();

        IsEmpty = rows.Count == 0;
        var now = rows.Count(r => r.Horizon == Horizon.Now);
        Summary = IsEmpty ? "" : $"{rows.Count} goals · {now} wanting you now";
    }
}
