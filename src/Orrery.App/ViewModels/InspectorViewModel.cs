using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public sealed partial class InspectorStepViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Guid _goalId;
    private readonly Guid _stepId;

    public InspectorStepViewModel(MainViewModel main, Goal goal, Step step, DependencyGraph graph, bool highlighted)
    {
        _main = main;
        _goalId = goal.Id;
        _stepId = step.Id;
        Title = step.Title;
        Done = step.Done;
        IsHighlighted = highlighted;
        var blocked = step.Done ? null : graph.BlockedReason(step, MainViewModel.Today);
        BlockedText = blocked ?? "";
        IsBlocked = blocked is not null;
        Detail = string.Join(" · ", new[]
        {
            step.Energy switch { Energy.Low => "low", Energy.High => "high", _ => "medium" } + " energy",
            step.EstimateMinutes is { } m ? $"~{m} min" : "",
            step.DeferCount >= 3 ? "put off a few times" : "",
        }.Where(s => s.Length > 0));
        CanStart = !step.Done && !IsBlocked && !main.Focus.IsVisible;
        Opacity = step.Done ? 0.45 : IsBlocked ? 0.65 : 1;
    }

    public string Title { get; }
    public bool Done { get; }
    public bool IsHighlighted { get; }
    public bool IsBlocked { get; }
    public string BlockedText { get; }
    public string Detail { get; }
    public bool CanStart { get; }
    public double Opacity { get; }

    [RelayCommand]
    private void Toggle() => _main.SetStepDone(_goalId, _stepId, !Done);

    [RelayCommand]
    private void Start() => _main.StartFocus(_goalId, _stepId);

    [RelayCommand]
    private void Remove() => _main.RemoveStep(_goalId, _stepId);
}

public sealed record WaitOption(string Label, Guid? Id);

public sealed partial class DependencyChipViewModel : ObservableObject
{
    private readonly Action _remove;

    public DependencyChipViewModel(string title, Action remove)
    {
        Title = title;
        _remove = remove;
    }

    public string Title { get; }

    [RelayCommand]
    private void Remove() => _remove();
}

/// <summary>
/// The one panel for a goal, opened from anywhere. Everything you can do to a goal lives here, so you never have
/// to leave the page you're on to log progress, start working, add a step, or say what it waits for.
/// </summary>
public partial class InspectorViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private Guid? _goalId;
    private Guid? _stepId;

    public InspectorViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<InspectorStepViewModel> Steps { get; } = [];
    public ObservableCollection<WaitOption> WaitOptions { get; } = [];
    public ObservableCollection<DependencyChipViewModel> Dependencies { get; } = [];
    public ObservableCollection<WaitOption> DependencyCandidates { get; } = [];
    public string[] EnergyNames { get; } = ["Low energy", "Medium energy", "High energy"];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private GoalRowViewModel? _row;
    [ObservableProperty] private bool _hasSteps;
    [ObservableProperty] private bool _canChain;
    [ObservableProperty] private bool _hasDependencies;
    [ObservableProperty] private bool _hasDependencyCandidates;

    [ObservableProperty] private bool _showLog;
    [ObservableProperty] private bool _showMarkDone;
    [ObservableProperty] private string _logUnit = "";
    [ObservableProperty] private string _logAmountText = "";
    [ObservableProperty] private string _markDoneLabel = "Mark done";

    [ObservableProperty] private string _newStepTitle = "";
    [ObservableProperty] private int _newStepEnergy = 1;
    [ObservableProperty] private int _waitIndex;
    [ObservableProperty] private int _dependencyIndex;

    public bool CanStart => IsOpen && !_main.Focus.IsVisible;

    public void Open(Guid goalId, Guid? stepId)
    {
        _goalId = goalId;
        _stepId = stepId;
        IsOpen = true;
        Refresh();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _goalId = null;
        _main.GoalsPage.Refresh();
    }

    public void Refresh()
    {
        if (_goalId is not { } id || _main.FindGoal(id) is not { Archived: false } goal)
        {
            IsOpen = false;
            _goalId = null;
            return;
        }

        var data = _main.Data;
        var today = MainViewModel.Today;
        var log = data.Contributions;
        Row = new GoalRowViewModel(data, goal, today);
        OnPropertyChanged(nameof(CanStart));

        var graph = new DependencyGraph(data.Goals, g => Progress.IsComplete(g, log));
        Steps.Clear();
        foreach (var step in goal.Steps)
            Steps.Add(new InspectorStepViewModel(_main, goal, step, graph, step.Id == _stepId));
        HasSteps = Steps.Count > 0;
        CanChain = goal.Steps.Count(s => !s.Done) > 1;

        WaitOptions.Clear();
        WaitOptions.Add(new WaitOption("Can start any time", null));
        foreach (var s in goal.Steps.Where(s => !s.Done))
            WaitOptions.Add(new WaitOption($"After “{s.Title}”", s.Id));
        // Replacing a ComboBox's items clears its selection, so put the first option back.
        WaitIndex = 0;
        OnPropertyChanged(nameof(WaitIndex));

        var kind = goal.Measure.Kind;
        ShowLog = kind is MeasureKind.Amount or MeasureKind.Time;
        LogUnit = goal.Measure.UnitLabel;
        ShowMarkDone = kind == MeasureKind.Done && !Progress.IsComplete(goal, log);
        MarkDoneLabel = goal.Rhythm.Kind == RhythmKind.None ? "Mark done" : "Done for now";

        Dependencies.Clear();
        foreach (var depId in goal.DependsOnGoals)
            if (_main.FindGoal(depId) is { } dep)
                Dependencies.Add(new DependencyChipViewModel(dep.Title, () => RemoveDependency(depId)));
        HasDependencies = Dependencies.Count > 0;

        DependencyCandidates.Clear();
        DependencyCandidates.Add(new WaitOption("Wait for another goal…", null));
        foreach (var other in data.Goals.Where(g => g.Id != goal.Id && !g.Archived && !goal.DependsOnGoals.Contains(g.Id)))
        {
            if (graph.CanWaitOnGoal(other.Id) && !graph.WouldCreateCycle(goal.Id, other.Id))
                DependencyCandidates.Add(new WaitOption(other.Title, other.Id));
        }
        HasDependencyCandidates = DependencyCandidates.Count > 1;
        DependencyIndex = 0;
        OnPropertyChanged(nameof(DependencyIndex));
    }

    private Goal? CurrentGoal => _goalId is { } id ? _main.FindGoal(id) : null;

    [RelayCommand]
    private void StartFocus()
    {
        if (_goalId is { } id)
            _main.StartFocus(id, null);
    }

    [RelayCommand]
    private void Log()
    {
        if (_goalId is not { } id)
            return;
        if (double.TryParse(LogAmountText, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount > 0)
        {
            LogAmountText = "";
            _main.Log(id, amount);
        }
    }

    [RelayCommand]
    private void MarkDone()
    {
        if (_goalId is { } id)
            _main.Log(id, 1);
    }

    [RelayCommand]
    private void AddStep()
    {
        if (CurrentGoal is not { } goal || string.IsNullOrWhiteSpace(NewStepTitle))
            return;
        var step = new Step { Title = NewStepTitle.Trim(), Energy = (Energy)NewStepEnergy };
        if (WaitIndex > 0 && WaitIndex < WaitOptions.Count && WaitOptions[WaitIndex].Id is { } waitsFor)
            step.DependsOn.Add(waitsFor);
        goal.Steps.Add(step);
        NewStepTitle = "";
        WaitIndex = 0;
        _main.Changed();
    }

    /// <summary>"Do these in order": every unfinished step waits for the one before it.</summary>
    [RelayCommand]
    private void ChainSteps()
    {
        if (CurrentGoal is not { } goal)
            return;
        DependencyGraph.Chain(goal.Steps.Where(s => !s.Done).ToList());
        _main.Changed();
    }

    [RelayCommand]
    private void AddDependency()
    {
        if (CurrentGoal is not { } goal || DependencyIndex <= 0 || DependencyIndex >= DependencyCandidates.Count)
            return;
        if (DependencyCandidates[DependencyIndex].Id is { } id)
            goal.DependsOnGoals.Add(id);
        _main.Changed();
    }

    private void RemoveDependency(Guid id)
    {
        if (CurrentGoal is not { } goal)
            return;
        goal.DependsOnGoals.Remove(id);
        _main.Changed();
    }

    [RelayCommand]
    private void Edit()
    {
        if (_goalId is { } id)
            _main.EditGoal(id);
    }

    [RelayCommand]
    private void Archive()
    {
        if (CurrentGoal is not { } goal)
            return;
        goal.Archived = true;
        _goalId = null;
        IsOpen = false;
        _main.Changed();
    }
}
