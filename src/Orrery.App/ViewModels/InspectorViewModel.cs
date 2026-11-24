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
        var siblings = goal.Steps.Where(o => o.ParentId == step.ParentId).ToList();
        CanMoveUp = siblings.IndexOf(step) > 0;
        CanMoveDown = siblings.IndexOf(step) < siblings.Count - 1;
        CanStart = !IsSection && !Done && !IsBlocked && !main.Focus.IsVisible;
        NeedsSplit = !IsSection && !Done && step.DeferCount >= Orrery.Core.Engine.PutOff.SplitAfter;
        CanPutOff = !IsSection && !Done;
        Opacity = Done ? 0.45 : IsBlocked ? 0.65 : 1;
    }

    // --- Editing a step in place -----------------------------------------------------------------

    public ObservableCollection<WaitOption> ParentChoices { get; } = [];
    public ObservableCollection<WaitOption> GoalChoices { get; } = [];
    [ObservableProperty] private int _editGoalIndex;
    [ObservableProperty] private bool _hasOtherGoals;
    [ObservableProperty] private bool _hasGroups;
    public string[] EnergyNames { get; } = ["Low energy", "Medium energy", "High energy"];

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editMinutes = "";
    [ObservableProperty] private int _editEnergy = 1;
    [ObservableProperty] private int _editParentIndex;
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }

    [RelayCommand]
    private void BeginEdit()
    {
        if (_main.FindGoal(_goalId) is not { } goal || goal.Steps.FirstOrDefault(s => s.Id == _stepId) is not { } step)
            return;
        EditTitle = step.Title;
        EditMinutes = step.EstimateMinutes?.ToString() ?? "";
        EditEnergy = (int)step.Energy;
        var inside = goal.BlockOf(step).Select(b => b.Id).ToHashSet();
        ParentChoices.Clear();
        ParentChoices.Add(new WaitOption("Not in a group", null));
        // Only groups can hold steps, and a nested group is named by its path so equal names stay apart.
        string PathOf(Step s) => s.ParentId is { } p && goal.Steps.FirstOrDefault(x => x.Id == p) is { } up ? $"{PathOf(up)} › {s.Title}" : s.Title;
        foreach (var other in goal.Steps.Where(o => goal.IsSection(o) && !inside.Contains(o.Id) && !o.Done))
            ParentChoices.Add(new WaitOption(PathOf(other), other.Id));
        HasGroups = ParentChoices.Count > 1;
        var at = Math.Max(0, ParentChoices.ToList().FindIndex(c => c.Id == step.ParentId));
        EditParentIndex = -1;
        EditParentIndex = at;

        GoalChoices.Clear();
        GoalChoices.Add(new WaitOption("Stays in this goal", null));
        foreach (var other in _main.Data.Goals.Where(g => g.Id != goal.Id && !g.Archived))
            GoalChoices.Add(new WaitOption($"Move to “{other.Title}”", other.Id));
        HasOtherGoals = GoalChoices.Count > 1;
        EditGoalIndex = -1;
        EditGoalIndex = 0;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void SaveEdit()
    {
        if (_main.FindGoal(_goalId) is not { } goal || goal.Steps.FirstOrDefault(s => s.Id == _stepId) is not { } step)
            return;
        if (!string.IsNullOrWhiteSpace(EditTitle))
            step.Title = EditTitle.Trim();
        step.Energy = (Energy)EditEnergy;
        step.EstimateMinutes = int.TryParse(EditMinutes, out var m) && m > 0 ? m : null;
        // Moving to another goal wins over a new place inside this one.
        if (EditGoalIndex > 0 && EditGoalIndex < GoalChoices.Count && GoalChoices[EditGoalIndex].Id is { } target)
        {
            _main.MoveStepToGoal(_goalId, _stepId, target);
            return;
        }
        var parent = EditParentIndex >= 0 && EditParentIndex < ParentChoices.Count ? ParentChoices[EditParentIndex].Id : null;
        goal.MoveTo(step, parent);
        _main.Changed();
    }

    /// <summary>"Not today" for a step: back tomorrow.</summary>
    [RelayCommand]
    private void PutOff()
    {
        if (_main.FindGoal(_goalId) is { } goal && goal.Steps.FirstOrDefault(s => s.Id == _stepId) is { } step)
        {
            Orrery.Core.Engine.PutOff.ForStep(step, MainViewModel.Today);
            _main.Changed();
        }
    }

    [RelayCommand]
    private void Split() => _main.SplitStep(_goalId, _stepId);

    public bool NeedsSplit { get; private set; }
    public bool CanPutOff { get; private set; }

    [RelayCommand]
    private void MoveUp()
    {
        if (_main.FindGoal(_goalId) is { } goal && goal.Steps.FirstOrDefault(s => s.Id == _stepId) is { } step && goal.MoveUp(step))
            _main.Changed();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (_main.FindGoal(_goalId) is { } goal && goal.Steps.FirstOrDefault(s => s.Id == _stepId) is { } step && goal.MoveDown(step))
            _main.Changed();
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
    [ObservableProperty] private int _newMinutes;
    [ObservableProperty] private bool _inOrder;
    [ObservableProperty] private bool _hasNewText;
    [ObservableProperty] private string _intoLabel = "";
    [ObservableProperty] private bool _isInto;
    private Guid? _into;

    partial void OnNewStepTitleChanged(string value) => HasNewText = !string.IsNullOrWhiteSpace(value);
    partial void OnNewMinutesChanged(int value)
    {
        OnPropertyChanged(nameof(Is15));
        OnPropertyChanged(nameof(Is30));
        OnPropertyChanged(nameof(Is60));
    }

    public bool Is15 => NewMinutes == 15;
    public bool Is30 => NewMinutes == 30;
    public bool Is60 => NewMinutes == 60;

    [RelayCommand]
    private void ToggleInOrder() => InOrder = !InOrder;

    /// <summary>The time chips: tap one to set it, tap it again to clear it.</summary>
    [RelayCommand]
    private void SetMinutes(string minutes)
    {
        var m = int.Parse(minutes, CultureInfo.InvariantCulture);
        NewMinutes = NewMinutes == m ? 0 : m;
    }

    /// <summary>Back to adding at the top level, after working inside a group.</summary>
    [RelayCommand]
    private void LeaveGroup()
    {
        _into = null;
        IsInto = false;
        IntoLabel = "";
    }

    // Things stay folded away until they are needed: adding steps, their extra options, and waiting on another goal.
    [ObservableProperty] private bool _showAddRow;
    [ObservableProperty] private bool _showWaitsSection;
    [ObservableProperty] private bool _showProgress;
    private bool _addRequested;

    public bool CanStart => IsOpen && !_main.Focus.IsVisible;

    public void Open(Guid goalId, Guid? stepId)
    {
        if (_goalId != goalId)
        {
            _addRequested = false;
            _into = null;
            NewMinutes = 0;
            InOrder = false;
        }
        _goalId = goalId;
        _stepId = stepId;
        IsOpen = true;
        Refresh();
    }

    /// <summary>"Break this into steps": reveals the add row on a goal that has none yet.</summary>
    [RelayCommand]
    private void RequestAdd()
    {
        _addRequested = true;
        ShowAddRow = true;
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
        ShowAddRow = HasSteps || _addRequested;
        CanChain = goal.Roots().Count(s => !goal.IsDone(s)) > 1;

        // If the group being filled was removed, go back to the top level.
        if (_into is { } into && goal.Steps.FirstOrDefault(s => s.Id == into) is not { } group)
            _into = null;
        else if (_into is not null)
            IntoLabel = goal.Steps.First(s => s.Id == _into).Title;
        IsInto = _into is not null;

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
        ShowWaitsSection = HasDependencies;
        ShowProgress = Row.HasFraction;

        DependencyCandidates.Clear();
        foreach (var other in data.Goals.Where(g => g.Id != goal.Id && !g.Archived && !goal.DependsOnGoals.Contains(g.Id)))
        {
            if (graph.CanWaitOnGoal(other.Id) && !graph.WouldCreateCycle(goal.Id, other.Id))
                DependencyCandidates.Add(new WaitOption(other.Title, other.Id));
        }
        HasDependencyCandidates = DependencyCandidates.Count > 0;
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
        var (title, typedMinutes) = StepText.Parse(NewStepTitle);
        var step = new Step { Title = title, Energy = Energy.Medium };
        if (!asSection && (NewMinutes > 0 ? NewMinutes : typedMinutes) is { } minutes and > 0)
            step.EstimateMinutes = minutes;
        step.ParentId = _into;
        // "In order": this one waits for the last open entry beside it.
        if (InOrder && goal.Steps.LastOrDefault(s => s.ParentId == step.ParentId && !goal.IsDone(s)) is { } before)
            step.DependsOn.Add(before.Id);
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
        NewMinutes = 0;
        // A new group is where the next steps go, until you step back out.
        if (asSection)
        {
            _into = step.Id;
            IntoLabel = step.Title;
            IsInto = true;
        }
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

    /// <summary>Called from the "Start after…" menu: this goal now waits for the one picked.</summary>
    public void WaitFor(Guid goalId)
    {
        if (CurrentGoal is not { } goal || goal.DependsOnGoals.Contains(goalId))
            return;
        goal.DependsOnGoals.Add(goalId);
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
        if (_goalId is { } id)
            _main.ArchiveGoal(id);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_goalId is { } id)
            _main.DeleteGoal(id);
    }
}
