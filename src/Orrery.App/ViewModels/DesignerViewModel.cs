using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Design;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public sealed record IssueRow(string Message, bool IsError);

/// <summary>
/// The goal designer. It holds the answers to the few questions every goal comes from, and re-reads what
/// they add up to after every change, so you can see a goal's behaviour before it exists.
/// </summary>
public partial class DesignerViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _applying;

    public DesignerViewModel(MainViewModel main)
    {
        _main = main;
        Reset();
    }

    public IReadOnlyList<Starter> Starters => GoalDesigner.Starters;
    public string[] RoleNames => _main.Data.Categories.Select(c => c.Name).ToArray();
    public string[] MeasureNames { get; } = ["Done or not done", "Steps (a checklist)", "An amount (pages, %, kg…)", "Focused time"];
    public string[] RhythmNames { get; } = ["No rhythm", "Repeats every N days", "Aim for an amount each week"];
    public string[] EnergyNames { get; } = ["Low energy", "Medium energy", "High energy"];

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private int _roleIndex;
    [ObservableProperty] private bool _hasFinishLine = true;
    [ObservableProperty] private int _measureIndex;
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private string _targetText = "";
    [ObservableProperty] private bool _hasDeadline;
    [ObservableProperty] private DateTimeOffset? _deadlineDate;
    [ObservableProperty] private int _rhythmIndex;
    [ObservableProperty] private string _everyDaysText = "7";
    [ObservableProperty] private string _perWeekText = "";
    [ObservableProperty] private int _energyIndex = 1;

    [ObservableProperty] private bool _canCreate;

    public ObservableCollection<string> Summary { get; } = [];
    public ObservableCollection<IssueRow> Issues { get; } = [];

    public bool ShowUnit => MeasureIndex == (int)MeasureKind.Amount;
    public bool ShowTarget => MeasureIndex is (int)MeasureKind.Amount or (int)MeasureKind.Time && RhythmIndex != (int)RhythmKind.Cadence;
    public string TargetLabel => RhythmIndex == (int)RhythmKind.Repeat ? "How much each time" : "Finish line (how much in total)";
    public bool ShowDeadline => HasFinishLine && RhythmIndex == (int)RhythmKind.None;
    public bool ShowEvery => RhythmIndex == (int)RhythmKind.Repeat;
    public bool ShowPerWeek => RhythmIndex == (int)RhythmKind.Cadence;
    public string PerWeekLabel => MeasureIndex switch
    {
        (int)MeasureKind.Time => "Minutes per week",
        (int)MeasureKind.Amount => $"{(string.IsNullOrWhiteSpace(Unit) ? "Amount" : Unit.Trim())} per week",
        _ => "Times per week",
    };

    private Guid? _editingId;

    public bool IsEditing => _editingId is not null;
    public string Heading => IsEditing ? "Edit goal" : "New goal";
    public string SubmitLabel => IsEditing ? "Save changes" : "Create goal";
    public string Intro => IsEditing
        ? "Change any answer. Steps and history are kept."
        : "Every goal is just a few answers. Start from an example, or answer from scratch.";

    /// <summary>Opens an existing goal's answers for changing.</summary>
    public void Load(Goal goal)
    {
        var a = GoalDesigner.AnswersFrom(goal, _main.Data);
        _applying = true;
        _editingId = goal.Id;
        Title = a.Title;
        Notes = a.Notes;
        RoleIndex = (int)a.Role;
        HasFinishLine = a.HasFinishLine;
        MeasureIndex = (int)a.Measure;
        Unit = a.Unit;
        TargetText = a.Target > 0 ? a.Target.ToString("0.#", CultureInfo.InvariantCulture) : "";
        HasDeadline = a.Deadline is not null;
        DeadlineDate = a.Deadline is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        RhythmIndex = (int)a.Rhythm;
        EveryDaysText = a.EveryDays.ToString(CultureInfo.InvariantCulture);
        PerWeekText = a.PerWeek > 0 ? a.PerWeek.ToString("0.#", CultureInfo.InvariantCulture) : "";
        EnergyIndex = (int)a.Energy;
        _applying = false;
        RaiseModeChanged();
        Recompute();
    }

    private void RaiseModeChanged()
    {
        foreach (var name in new[] { nameof(IsEditing), nameof(Heading), nameof(SubmitLabel), nameof(Intro) })
            base.OnPropertyChanged(new PropertyChangedEventArgs(name));
    }

    public void Reset()
    {
        _editingId = null;
        _applying = true;
        Title = Notes = Unit = TargetText = PerWeekText = "";
        RoleIndex = 0;
        HasFinishLine = true;
        MeasureIndex = 0;
        HasDeadline = false;
        DeadlineDate = null;
        RhythmIndex = 0;
        EveryDaysText = "7";
        EnergyIndex = 1;
        _applying = false;
        RaiseModeChanged();
        Recompute();
    }

    [RelayCommand]
    private void ApplyStarter(Starter starter)
    {
        var a = starter.Create(MainViewModel.Today);
        _applying = true;
        RoleIndex = (int)a.Role;
        HasFinishLine = a.HasFinishLine;
        MeasureIndex = (int)a.Measure;
        Unit = a.Unit;
        TargetText = a.Target > 0 ? a.Target.ToString("0.#", CultureInfo.InvariantCulture) : "";
        HasDeadline = a.Deadline is not null;
        DeadlineDate = a.Deadline is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        RhythmIndex = (int)a.Rhythm;
        EveryDaysText = a.EveryDays.ToString(CultureInfo.InvariantCulture);
        PerWeekText = a.PerWeek > 0 ? a.PerWeek.ToString("0.#", CultureInfo.InvariantCulture) : "";
        EnergyIndex = (int)a.Energy;
        _applying = false;
        Recompute();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_applying && e.PropertyName is not (nameof(CanCreate)))
            Recompute();
    }

    private static double Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private DesignAnswers Answers()
    {
        var everyDays = (int)Math.Round(Number(EveryDaysText));
        return new DesignAnswers
        {
            Title = Title,
            Notes = Notes,
            Role = (CategoryRole)RoleIndex,
            HasFinishLine = HasFinishLine,
            Measure = (MeasureKind)MeasureIndex,
            Unit = Unit,
            Target = Number(TargetText),
            Deadline = HasDeadline && DeadlineDate is { } d ? DateOnly.FromDateTime(d.DateTime) : null,
            Rhythm = (RhythmKind)RhythmIndex,
            EveryDays = everyDays,
            PerWeek = Number(PerWeekText),
            Energy = (Energy)EnergyIndex,
        };
    }

    private void Recompute()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowUnit), nameof(ShowTarget), nameof(TargetLabel), nameof(ShowDeadline),
                     nameof(ShowEvery), nameof(ShowPerWeek), nameof(PerWeekLabel),
                 })
            base.OnPropertyChanged(new PropertyChangedEventArgs(name));

        var result = GoalDesigner.Build(_main.Data, Answers(), MainViewModel.Today);
        Summary.Clear();
        foreach (var line in result.Summary)
            Summary.Add(line);
        Issues.Clear();
        // Only nag about the name once something else has been filled in.
        foreach (var issue in result.Issues.Where(i => Title.Length > 0 || i.Message != "Give it a name."))
            Issues.Add(new IssueRow(issue.Message, issue.Severity == IssueSeverity.Error));
        CanCreate = result.IsValid;
    }

    [RelayCommand]
    private void Create()
    {
        var result = GoalDesigner.Build(_main.Data, Answers(), MainViewModel.Today);
        if (result.Goal is not { } goal)
            return;

        if (_editingId is { } id && _main.FindGoal(id) is { } existing)
        {
            GoalDesigner.ApplyTo(existing, goal);
            _editingId = null;
            _main.Changed();
            _main.Inspect(existing.Id);
            _main.ShowGoalsCommand.Execute(null);
            return;
        }
        _main.AddGoal(goal);
    }
}
