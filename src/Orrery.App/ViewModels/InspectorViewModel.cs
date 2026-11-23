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

    public InspectorStepViewModel(MainViewModel main, Goal goal, Step step, DependencyGraph graph, bool highlighted, int depth)
    {
        _main = main;
        _goalId = goal.Id;
        _stepId = step.Id;
        Title = step.Title;
        IsSection = goal.IsSection(step);
        Done = goal.IsDone(step);
        IsHighlighted = highlighted;
        Indent = new Avalonia.Thickness(depth * 18, 0, 0, 0);
        var blocked = Done ? null : graph.BlockedReason(step, MainViewModel.Today);
        BlockedText = blocked ?? "";
        IsBlocked = blocked is not null;
        var estimate = goal.EstimateOf(step);
        if (IsSection)
        {
            var (d, total) = goal.CountOf(step);
            Detail = string.Join(" · ", new[]
            {
                $"{d}/{total} steps",
                estimate is { } e ? $"~{e} min" : "",
            }.Where(s => s.Length > 0));
        }
        else
        {
            Detail = string.Join(" · ", new[]
            {
                step.Energy switch { Energy.Low => "low", Energy.High => "high", _ => "medium" } + " energy",
                estimate is { } m ? $"~{m} min" : "",
                step.DeferCount >= 3 ? "put off a few times" : "",
            }.Where(s => s.Length > 0));
        }
        CanStart = !IsSection && !Done && !IsBlocked && !main.Focus.IsVisible;
        Opacity = Done ? 0.45 : IsBlocked ? 0.65 : 1;
    }

    public bool IsSection { get; }
    public bool IsLeaf => !IsSection;
    public Avalonia.Media.FontWeight TitleWeight => IsSection ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
    public Avalonia.Thickness Indent { get; }
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
    public ObservableCollection<WaitOption> ParentOptions { get; } = [];
    [ObservableProperty] private int _parentIndex;
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
        void Walk(Step step, int depth)
        {
            Steps.Add(new InspectorStepViewModel(_main, goal, step, graph, step.Id == _stepId, depth));
            foreach (var child in goal.ChildrenOf(step))
                Walk(child, depth + 1);
        }
        foreach (var root in goal.Roots())
            Walk(root, 0);
        HasSteps = Steps.Count > 0;
        CanChain = goal.Roots().Count(s => !goal.IsDone(s)) > 1;

        WaitOptions.Clear();
        WaitOptions.Add(new WaitOption("Can start any time", null));
        foreach (var s in goal.Steps.Where(s => !goal.IsDone(s)))
            WaitOptions.Add(new WaitOption($"After “{s.Title}”", s.Id));
        ParentOptions.Clear();
        ParentOptions.Add(new WaitOption("Top level", null));
        foreach (var s in goal.Steps.Where(s => !s.Done))
            ParentOptions.Add(new WaitOption($"Inside “{s.Title}”", s.Id));
        // Replacing a ComboBox's items clears its selection, so put the first option back.
        WaitIndex = 0;
        OnPropertyChanged(nameof(WaitIndex));
        ParentIndex = 0;
        OnPropertyChanged(nameof(ParentIndex));

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
    private void AddStep() => Add(asSection: false);

    /// <summary>A section starts empty; steps go inside it by picking it under "Inside".</summary>
    [RelayCommand]
    private void AddSection() => Add(asSection: true);

    private void Add(bool asSection)
    {
        if (CurrentGoal is not { } goal || string.IsNullOrWhiteSpace(NewStepTitle))
            return;
        var step = new Step { Title = NewStepTitle.Trim(), Energy = (Energy)NewStepEnergy };
        if (ParentIndex > 0 && ParentIndex < ParentOptions.Count)
            step.ParentId = ParentOptions[ParentIndex].Id;
        if (WaitIndex > 0 && WaitIndex < WaitOptions.Count && WaitOptions[WaitIndex].Id is { } waitsFor)
            step.DependsOn.Add(waitsFor);
        step.IsGroup = asSection;
        // Keep a section's children next to it so the stored order reads like the screen.
        var at = goal.Steps.Count;
        if (step.ParentId is { } parentId)
        {
            var last = goal.Steps.FindLastIndex(s => s.ParentId == parentId || s.Id == parentId);
            if (last >= 0)
                at = last + 1;
        }
        goal.Steps.Insert(at, step);
        NewStepTitle = "";
        _main.Changed();
    }

    /// <summary>"Do these in order": every unfinished top-level entry waits for the one before it.</summary>
    [RelayCommand]
    private void ChainSteps()
    {
        if (CurrentGoal is not { } goal)
            return;
        DependencyGraph.Chain(goal.Roots().Where(s => !goal.IsDone(s)).ToList());
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
