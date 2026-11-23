using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.App.ViewModels;

public static class Palette
{
    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    public static IBrush For(Category? category) => Brush(category?.AccentHex ?? "#8890B0");
    public static Color ColorFor(Category? category) => Color.Parse(category?.AccentHex ?? "#8890B0");
}

public partial class MainViewModel : ObservableObject
{
    public OrreryData Data { get; }
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public TodayViewModel TodayPage { get; }
    public GoalsViewModel GoalsPage { get; }
    public DesignerViewModel DesignerPage { get; }
    public HistoryViewModel HistoryPage { get; }
    public FocusViewModel Focus { get; }
    public InspectorViewModel Inspector { get; }
    public CaptureViewModel Capture { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday), nameof(IsGoals), nameof(IsDesigner), nameof(IsHistory))]
    private ObservableObject _current;

    public bool IsToday => Current == TodayPage;
    public bool IsGoals => Current == GoalsPage;
    public bool IsDesigner => Current == DesignerPage;
    public bool IsHistory => Current == HistoryPage;

    public MainViewModel()
    {
        Data = JsonStore.Load() ?? SampleData.Create(Today);
        SoftenOldColours(Data);
        JsonStore.Save(Data);

        TodayPage = new TodayViewModel(this);
        GoalsPage = new GoalsViewModel(this);
        DesignerPage = new DesignerViewModel(this);
        HistoryPage = new HistoryViewModel(this);
        Focus = new FocusViewModel(this);
        Inspector = new InspectorViewModel(this);
        Capture = new CaptureViewModel(this);
        _current = TodayPage;

        // Start buttons are only live while no session is running, so lists redraw when one starts or ends.
        Focus.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FocusViewModel.IsVisible))
            {
                TodayPage.Refresh();
                Inspector.Refresh();
            }
        };
        RefreshAll();
    }

    /// <summary>Earlier builds saved brighter bucket colours; move those to the calmer set.</summary>
    private static void SoftenOldColours(OrreryData data)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#FF7A5C"] = "#E0896C", ["#9B7CFF"] = "#9F8FE6", ["#3CC8B4"] = "#62B8A9",
        };
        foreach (var c in data.Categories)
            if (map.TryGetValue(c.AccentHex, out var soft))
                c.AccentHex = soft;
    }

    [RelayCommand]
    private void ShowToday() => Show(TodayPage);

    [RelayCommand]
    private void ShowGoals() => Show(GoalsPage);

    [RelayCommand]
    private void ShowDesigner()
    {
        DesignerPage.Reset();
        Show(DesignerPage);
    }

    [RelayCommand]
    private void ShowHistory() => Show(HistoryPage);

    private void Show(ObservableObject page)
    {
        RefreshAll();
        Current = page;
    }

    public void RefreshAll()
    {
        TodayPage.Refresh();
        GoalsPage.Refresh();
        HistoryPage.Refresh();
        Inspector.Refresh();
    }

    /// <summary>Call after any change to the data: saves and updates every page.</summary>
    public void Changed()
    {
        JsonStore.Save(Data);
        RefreshAll();
    }

    public Goal? FindGoal(Guid id) => Data.Goals.FirstOrDefault(g => g.Id == id);

    // --- Navigation shared by every page ------------------------------------------------------------

    public void Inspect(Guid goalId, Guid? stepId = null) => Inspector.Open(goalId, stepId);

    public void StartFocus(Guid goalId, Guid? stepId = null) => Focus.Start(goalId, stepId);

    public void EditGoal(Guid goalId)
    {
        if (FindGoal(goalId) is not { } goal)
            return;
        DesignerPage.Load(goal);
        Show(DesignerPage);
    }

    public void AddGoal(Goal goal)
    {
        Data.Goals.Add(goal);
        Changed();
        Current = GoalsPage;
        Inspect(goal.Id);
    }

    // --- Changing progress --------------------------------------------------------------------------

    /// <summary>Marks a suggested item done: finishes its step if it has one, and logs the progress.</summary>
    public void Complete(PlanItem item, double amount)
    {
        var goal = Data.Goals.First(g => g.Id == item.GoalId);
        var step = item.StepId is { } id ? goal.Steps.First(s => s.Id == id) : null;
        if (step is not null)
            step.Done = true;

        var kind = goal.Measure.Kind;
        var logged = kind is MeasureKind.Amount or MeasureKind.Time ? amount : 1;
        Data.Contributions.Add(new Contribution
        {
            GoalId = goal.Id,
            StepId = step?.Id,
            Day = Today,
            Amount = logged,
            Minutes = kind == MeasureKind.Time ? amount : 0,
        });
        Changed();
    }

    /// <summary>Ticks or unticks a step. Unticking takes back the progress the tick gave, but keeps time already spent.</summary>
    public void SetStepDone(Guid goalId, Guid stepId, bool done)
    {
        if (FindGoal(goalId) is not { } goal || goal.Steps.FirstOrDefault(s => s.Id == stepId) is not { } step)
            return;
        if (step.Done == done)
            return;

        step.Done = done;
        var counts = goal.Measure.Kind is MeasureKind.Steps or MeasureKind.Done;
        if (done)
        {
            Data.Contributions.Add(new Contribution { GoalId = goal.Id, StepId = step.Id, Day = Today, Amount = counts ? 1 : 0 });
        }
        else if (counts)
        {
            foreach (var c in Data.Contributions.Where(c => c.StepId == step.Id).ToList())
            {
                if (c.Minutes > 0)
                    c.Amount = 0;
                else
                    Data.Contributions.Remove(c);
            }
        }
        Changed();
    }

    /// <summary>Logs progress against a goal directly: units, minutes, or a plain "done".</summary>
    public void Log(Guid goalId, double amount)
    {
        if (FindGoal(goalId) is not { } goal || amount <= 0)
            return;
        Data.Contributions.Add(new Contribution
        {
            GoalId = goal.Id,
            Day = Today,
            Amount = amount,
            Minutes = goal.Measure.Kind == MeasureKind.Time ? amount : 0,
        });
        Changed();
    }

    public void RemoveStep(Guid goalId, Guid stepId)
    {
        if (FindGoal(goalId) is not { } goal)
            return;
        var before = goal.Steps.Select(s => s.Id).ToHashSet();
        goal.RemoveStep(stepId);
        var gone = before.Except(goal.Steps.Select(s => s.Id)).ToHashSet();
        foreach (var other in Data.Goals.SelectMany(g => g.Steps))
            other.DependsOn.RemoveAll(gone.Contains);
        Changed();
    }
}
