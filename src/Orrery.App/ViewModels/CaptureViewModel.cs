using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Design;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

/// <summary>One bucket as a pill in the quick-add form.</summary>
public sealed partial class BucketPick : ObservableObject
{
    public BucketPick(Category category)
    {
        Category = category;
        Name = category.Name;
        Dot = Palette.For(category);
    }

    public Category Category { get; }
    public string Name { get; }
    public IBrush Dot { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Quick add, as a small form instead of a line of magic syntax: pick what you are adding, name it, and choose the
/// rest from lists. Everything it offers is real (goals, sections and steps that exist), so there is nothing to
/// misspell and nothing to remember. It reuses <see cref="QuickCapture.Apply"/> so the result matches the typed path.
/// </summary>
public partial class CaptureViewModel : ObservableObject
{
    private static readonly (string Label, int? Minutes)[] Times =
        [("No estimate", null), ("15 min", 15), ("30 min", 30), ("45 min", 45), ("1 hour", 60), ("90 min", 90), ("2 hours", 120)];

    private static readonly (string Label, int Days)[] StartDays =
        [("Any time", 0), ("Tomorrow", 1), ("In 3 days", 3), ("Next week", 7), ("In 2 weeks", 14), ("Next month", 30)];

    private static readonly (string Label, int Days)[] DeadlineDays =
        [("No deadline", 0), ("In a week", 7), ("In 2 weeks", 14), ("In a month", 30), ("In 3 months", 90)];

    private static readonly (string Label, RhythmKind Kind, int Every)[] Repeats =
        [("Doesn't repeat", RhythmKind.None, 1), ("Every day", RhythmKind.Repeat, 1), ("Every 2 days", RhythmKind.Repeat, 2),
         ("Every week", RhythmKind.Repeat, 7), ("A few hours a week", RhythmKind.Cadence, 1)];

    private static readonly double[] HoursPerWeek = [1, 2, 3, 5, 8];

    private readonly MainViewModel _main;
    private List<Goal> _goals = [];
    private List<Step?> _sections = [];
    private List<Step?> _afters = [];
    private bool _rebuilding;

    public CaptureViewModel(MainViewModel main)
    {
        _main = main;
        TimeNames = Times.Select(t => t.Label).ToList();
        StartNames = StartDays.Select(t => t.Label).ToList();
        DeadlineNames = DeadlineDays.Select(t => t.Label).ToList();
        RepeatNames = Repeats.Select(t => t.Label).ToList();
        HourNames = HoursPerWeek.Select(h => h == 1 ? "1 hour a week" : $"{h:0.#} hours a week").ToList();
    }

    public IReadOnlyList<string> TimeNames { get; }
    public IReadOnlyList<string> StartNames { get; }
    public IReadOnlyList<string> DeadlineNames { get; }
    public IReadOnlyList<string> RepeatNames { get; }
    public IReadOnlyList<string> HourNames { get; }
    public IReadOnlyList<string> EnergyNames { get; } = ["Light", "Medium", "Heavy"];

    public ObservableCollection<string> GoalNames { get; } = [];
    public ObservableCollection<string> SectionNames { get; } = [];
    public ObservableCollection<string> AfterNames { get; } = [];
    public ObservableCollection<BucketPick> Buckets { get; } = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGoal))] private bool _isStep = true;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private int _goalIndex;
    [ObservableProperty] private int _sectionIndex;
    [ObservableProperty] private int _afterIndex;
    [ObservableProperty] private int _timeIndex;
    [ObservableProperty] private int _startIndex;
    [ObservableProperty] private int _energyIndex = 1;
    [ObservableProperty] private int _deadlineIndex;
    [ObservableProperty] private int _repeatIndex;
    [ObservableProperty] private int _hoursIndex = 1;
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private bool _canCommit;
    [ObservableProperty] private string _commitLabel = "Add step";

    public bool IsGoal => !IsStep;
    public bool HasSections => SectionNames.Count > 1;
    public bool HasNoGoals => _goals.Count == 0;
    public bool ShowHours => IsGoal && Repeats[Math.Clamp(RepeatIndex, 0, Repeats.Length - 1)].Kind == RhythmKind.Cadence;
    public bool ShowDeadline => IsGoal && Repeats[Math.Clamp(RepeatIndex, 0, Repeats.Length - 1)].Kind == RhythmKind.None;

    /// <summary>Opens the form, with a fresh name and the goal you used last (or the first one).</summary>
    [RelayCommand]
    public void Open()
    {
        var data = _main.Data;
        var keep = GoalIndex < _goals.Count ? _goals[GoalIndex].Id : (Guid?)null;
        _goals = data.Goals.Where(g => !g.Archived).ToList();
        _rebuilding = true;
        GoalNames.Clear();
        foreach (var g in _goals)
            GoalNames.Add(g.Title);
        GoalIndex = -1;
        GoalIndex = Math.Max(0, keep is { } k ? _goals.FindIndex(g => g.Id == k) : 0);
        Buckets.Clear();
        foreach (var c in data.Categories)
            Buckets.Add(new BucketPick(c));
        if (Buckets.Count > 0)
            Buckets[0].IsSelected = true;
        _rebuilding = false;

        Title = "";
        IsStep = _goals.Count > 0;
        TimeIndex = StartIndex = DeadlineIndex = RepeatIndex = 0;
        EnergyIndex = 1;
        RebuildSections();
        OnPropertyChanged(nameof(HasNoGoals));
        OnPropertyChanged(nameof(ShowHours));
        OnPropertyChanged(nameof(ShowDeadline));
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void PickKind(string kind)
    {
        IsStep = kind == "step";
        OnPropertyChanged(nameof(ShowHours));
        OnPropertyChanged(nameof(ShowDeadline));
        Refresh();
    }

    [RelayCommand]
    private void PickBucket(BucketPick pick)
    {
        foreach (var b in Buckets)
            b.IsSelected = b == pick;
        Refresh();
    }

    partial void OnTitleChanged(string value) => Refresh();
    partial void OnTimeIndexChanged(int value) => Refresh();
    partial void OnStartIndexChanged(int value) => Refresh();
    partial void OnEnergyIndexChanged(int value) => Refresh();
    partial void OnDeadlineIndexChanged(int value) => Refresh();
    partial void OnHoursIndexChanged(int value) => Refresh();
    partial void OnAfterIndexChanged(int value) => Refresh();
    partial void OnSectionIndexChanged(int value) => Refresh();

    partial void OnGoalIndexChanged(int value)
    {
        if (_rebuilding)
            return;
        RebuildSections();
        Refresh();
    }

    partial void OnRepeatIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowHours));
        OnPropertyChanged(nameof(ShowDeadline));
        Refresh();
    }

    /// <summary>The sections and steps on offer depend on which goal is picked.</summary>
    private void RebuildSections()
    {
        _sections = [null];
        _afters = [null];
        SectionNames.Clear();
        AfterNames.Clear();
        SectionNames.Add("Not in a section");
        AfterNames.Add("Nothing, it can start now");
        if (GoalIndex >= 0 && GoalIndex < _goals.Count)
        {
            var goal = _goals[GoalIndex];
            foreach (var s in goal.Steps.Where(goal.IsSection))
            {
                _sections.Add(s);
                SectionNames.Add(s.Title);
            }
            foreach (var s in goal.Leaves().Where(s => !s.Done))
            {
                _afters.Add(s);
                AfterNames.Add(s.Title);
            }
        }
        // Refilling a list clears its ComboBox's selection, so the index has to visibly change to put it back.
        SectionIndex = -1;
        AfterIndex = -1;
        SectionIndex = 0;
        AfterIndex = 0;
        OnPropertyChanged(nameof(HasSections));
    }

    private void Refresh()
    {
        var hasTitle = !string.IsNullOrWhiteSpace(Title);
        CommitLabel = IsStep ? "Add step" : "Add goal";
        CanCommit = hasTitle && (IsGoal || (GoalIndex >= 0 && GoalIndex < _goals.Count));
        Hint = !hasTitle ? "Give it a name to add it." : "";
    }

    [RelayCommand]
    private void Commit()
    {
        if (!CanCommit)
            return;
        var today = MainViewModel.Today;
        var title = Title.Trim();
        var time = Times[Math.Clamp(TimeIndex, 0, Times.Length - 1)].Minutes;
        var energy = (Energy)Math.Clamp(EnergyIndex, 0, 2);

        CapturePreview preview;
        if (IsStep)
        {
            var goal = _goals[GoalIndex];
            var days = StartDays[Math.Clamp(StartIndex, 0, StartDays.Length - 1)].Days;
            preview = new CapturePreview(CaptureKind.NewStep, title, "", [], null, goal,
                _afters.ElementAtOrDefault(AfterIndex), days > 0 ? today.AddDays(days) : null, null, energy, time,
                null, null, 0, _sections.ElementAtOrDefault(SectionIndex));
        }
        else
        {
            var repeat = Repeats[Math.Clamp(RepeatIndex, 0, Repeats.Length - 1)];
            var perWeek = repeat.Kind == RhythmKind.Cadence ? HoursPerWeek[Math.Clamp(HoursIndex, 0, HoursPerWeek.Length - 1)] * 60 : 0;
            var rhythm = repeat.Kind switch
            {
                RhythmKind.Repeat => new Rhythm(RhythmKind.Repeat, repeat.Every),
                RhythmKind.Cadence => new Rhythm(RhythmKind.Cadence, PerWeek: perWeek),
                _ => null,
            };
            var days = DeadlineDays[Math.Clamp(DeadlineIndex, 0, DeadlineDays.Length - 1)].Days;
            var role = Buckets.FirstOrDefault(b => b.IsSelected)?.Category.Role ?? CategoryRole.Obligation;
            preview = new CapturePreview(CaptureKind.NewGoal, title, "", [], null, null, null, null,
                rhythm is null && days > 0 ? today.AddDays(days) : null, energy, time, role, rhythm, perWeek);
        }

        var landed = QuickCapture.Apply(_main.Data, preview, today);
        var step = preview.Kind == CaptureKind.NewStep ? landed.Steps.LastOrDefault() : null;
        IsOpen = false;
        _main.Changed();
        _main.Inspect(landed.Id, step?.Id);
    }
}
