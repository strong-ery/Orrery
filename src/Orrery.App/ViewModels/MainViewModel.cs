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
    public CalendarViewModel CalendarPage { get; }
    public FocusViewModel Focus { get; }
    public InspectorViewModel Inspector { get; }
    public CaptureViewModel Capture { get; }
    public SettingsViewModel SettingsPage { get; }
    public UpdateViewModel Updates { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday), nameof(IsGoals), nameof(IsDesigner), nameof(IsHistory), nameof(IsCalendar), nameof(IsSettings))]
    private ObservableObject _current;

    public bool IsToday => Current == TodayPage;
    public bool IsGoals => Current == GoalsPage;
    public bool IsDesigner => Current == DesignerPage;
    public bool IsHistory => Current == HistoryPage;
    public bool IsCalendar => Current == CalendarPage;
    public bool IsSettings => Current == SettingsPage;

    // --- Window size: the side panels ease between wide and narrow instead of squeezing the page ----

    [ObservableProperty] private double _sidebarWidth = 214;
    [ObservableProperty] private double _inspectorWidth = 410;
    [ObservableProperty] private bool _compact;

    public bool Wide => !Compact;

    partial void OnCompactChanged(bool value) => OnPropertyChanged(nameof(Wide));

    public void FitToWidth(double width)
    {
        Compact = width < 1120;
        SidebarWidth = Compact ? 62 : 214;
        InspectorWidth = Math.Clamp(width * 0.3, 320, 410);
    }

    /// <summary>Set by the app so Settings can show a sample reminder.</summary>
    public Action? SendTestReminder { get; set; }

    /// <summary>Set by the window: asks the user for a sound file and returns its path, or null if they cancel.</summary>
    public Func<Task<string?>>? PickSoundFile { get; set; }

    public void OpenDataFolder()
    {
        var folder = System.IO.Path.GetDirectoryName(JsonStore.DefaultPath);
        if (folder is not null && System.IO.Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowSettings()
    {
        SettingsPage.Reload();
        Show(SettingsPage);
    }

    [RelayCommand]
    private void ToggleReminders()
    {
        Data.Settings.Nudges = !Data.Settings.Nudges;
        JsonStore.Save(Data);
        SettingsPage.Reload();
        OnPropertyChanged(nameof(RemindersLabel));
    }

    public string RemindersLabel => Data.Settings.Nudges ? "Pause reminders" : "Turn reminders on";

    [RelayCommand]
    private void QuitApp() => (Avalonia.Application.Current as App)?.QuitApp();

    // --- Remembering where you were ------------------------------------------------------------

    private readonly Avalonia.Threading.DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _restoring = true;

    partial void OnCurrentChanged(ObservableObject value) => UiChanged();

    /// <summary>Something about the layout changed; it is written out a moment later, so dragging a window is not a write per pixel.</summary>
    public void UiChanged()
    {
        if (_restoring)
            return;
        _uiTimer.Stop();
        _uiTimer.Start();
    }

    private string PageName => Current == GoalsPage ? "Goals" : Current == CalendarPage ? "Calendar" : Current == HistoryPage ? "History"
        : Current == SettingsPage ? "Settings" : "Today";

    public void SaveUiNow()
    {
        if (_restoring)
            return;
        _uiTimer.Stop();
        var ui = Data.Settings.Ui;
        ui.Page = PageName;
        ui.TimelineSpan = GoalsPage.Span;
        ui.OpenGoal = Inspector.OpenGoalId;
        ui.OpenStep = Inspector.OpenStepId;
        ui.CalendarMonth = CalendarPage.Month;
        ui.CalendarDay = CalendarPage.Selected;
        JsonStore.Save(Data);
    }

    private void RestoreUi()
    {
        var ui = Data.Settings.Ui;
        GoalsPage.Span = ui.TimelineSpan is 42 or 180 ? ui.TimelineSpan : 42;
        if (ui.CalendarMonth is { } month && ui.CalendarDay is { } day)
            CalendarPage.Restore(month, day);
        if (ui.OpenGoal is { } goalId && FindGoal(goalId) is { Archived: false })
            Inspector.Open(goalId, ui.OpenStep);
        RefreshAll();
        Current = ui.Page switch
        {
            "Goals" => GoalsPage,
            "Calendar" => CalendarPage,
            "History" => HistoryPage,
            "Settings" => SettingsPage,
            _ => TodayPage,
        };
        _restoring = false;
    }

    public MainViewModel()
    {
        _uiTimer.Tick += (_, _) => SaveUiNow();
        Data = JsonStore.Load() ?? OrreryData.CreateEmpty();
        SoftenOldColours(Data);
        JsonStore.Save(Data);

        TodayPage = new TodayViewModel(this);
        GoalsPage = new GoalsViewModel(this);
        DesignerPage = new DesignerViewModel(this);
        HistoryPage = new HistoryViewModel(this);
        CalendarPage = new CalendarViewModel(this);
        Focus = new FocusViewModel(this);
        Inspector = new InspectorViewModel(this);
        Capture = new CaptureViewModel(this);
        Updates = new UpdateViewModel(this);
        SettingsPage = new SettingsViewModel(this);
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
        RestoreUi();
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

    [RelayCommand]
    private void ShowCalendar() => Show(CalendarPage);

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
        CalendarPage.Refresh();
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
        var before = JsonStore.ToJson(Data);
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
        _undoSnapshot = before;
        OfferUndo($"Marked “{step?.Title ?? goal.Title}” done");
    }

    /// <summary>Ticks or unticks a step. Unticking takes back the progress the tick gave, but keeps time already spent.</summary>
    public void SetStepDone(Guid goalId, Guid stepId, bool done)
    {
        if (FindGoal(goalId) is not { } goal || goal.Steps.FirstOrDefault(s => s.Id == stepId) is not { } step)
            return;
        if (step.Done == done)
            return;

        var before = done ? JsonStore.ToJson(Data) : null;
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
        if (before is not null)
        {
            _undoSnapshot = before;
            OfferUndo($"Marked “{step.Title}” done");
        }
    }

    /// <summary>Logs progress against a goal directly: units, minutes, or a plain "done".</summary>
    public void Log(Guid goalId, double amount)
    {
        if (FindGoal(goalId) is not { } goal || amount <= 0)
            return;
        var before = JsonStore.ToJson(Data);
        Data.Contributions.Add(new Contribution
        {
            GoalId = goal.Id,
            Day = Today,
            Amount = amount,
            Minutes = goal.Measure.Kind == MeasureKind.Time ? amount : 0,
        });
        Changed();
        _undoSnapshot = before;
        OfferUndo($"Logged progress on “{goal.Title}”");
    }

    /// <summary>Moves a step, and anything inside it, to another goal.</summary>
    public void MoveStepToGoal(Guid fromGoalId, Guid stepId, Guid toGoalId)
    {
        if (FindGoal(fromGoalId) is not { } from || FindGoal(toGoalId) is not { } to
            || from.Steps.FirstOrDefault(s => s.Id == stepId) is not { } step)
            return;
        if (from.MoveToGoal(to, step))
        {
            Changed();
            Inspect(toGoalId, stepId);
        }
    }

    // --- Removing goals, and taking it back ---------------------------------------------------------

    private string? _undoSnapshot;
    private readonly Avalonia.Threading.DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    [ObservableProperty] private bool _hasUndo;
    [ObservableProperty] private string _undoText = "";

    private void OfferUndo(string text)
    {
        _undoTimer.Stop();
        UndoText = text;
        HasUndo = true;
        _undoTimer.Tick -= OnUndoTimeout;
        _undoTimer.Tick += OnUndoTimeout;
        _undoTimer.Start();
    }

    private void OnUndoTimeout(object? sender, EventArgs e) => DismissUndo();

    [RelayCommand]
    public void DismissUndo()
    {
        _undoTimer.Stop();
        HasUndo = false;
        _undoSnapshot = null;
    }

    /// <summary>Puts everything back the way it was before the last delete, put away, or marking something done.</summary>
    [RelayCommand]
    public void Undo()
    {
        if (_undoSnapshot is null || JsonStore.FromJson(_undoSnapshot) is not { } old)
            return;
        Data.Categories = old.Categories;
        Data.Goals = old.Goals;
        Data.Contributions = old.Contributions;
        Data.Settings = old.Settings;
        DismissUndo();
        Changed();
    }

    /// <summary>
    /// Removes one entry from the record of things done. A step that was ticked by it goes back to open; a repeating goal
    /// becomes due again. Undo is offered, but the entry can also simply be found again in History.
    /// </summary>
    public void TakeBack(Guid contributionId)
    {
        if (Data.Contributions.FirstOrDefault(c => c.Id == contributionId) is not { } entry)
            return;
        var goal = FindGoal(entry.GoalId);
        var step = goal?.Steps.FirstOrDefault(s => s.Id == entry.StepId);
        _undoSnapshot = JsonStore.ToJson(Data);
        Data.Contributions.Remove(entry);
        if (step is { Done: true } && !Data.Contributions.Any(c => c.StepId == step.Id))
            step.Done = false;
        Changed();
        OfferUndo($"Took back “{step?.Title ?? goal?.Title ?? "that"}”");
    }

    /// <summary>Deletes a goal for good: its steps, its history, and every link that pointed at it. Undo is offered for ten seconds.</summary>
    public void DeleteGoal(Guid id)
    {
        if (FindGoal(id) is not { } goal)
            return;
        _undoSnapshot = JsonStore.ToJson(Data);
        var stepIds = goal.Steps.Select(s => s.Id).ToHashSet();
        Data.Goals.Remove(goal);
        Data.Contributions.RemoveAll(c => c.GoalId == id);
        foreach (var other in Data.Goals)
        {
            other.DependsOnGoals.Remove(id);
            foreach (var step in other.Steps)
                step.DependsOn.RemoveAll(stepIds.Contains);
        }
        Data.Settings.PlanOrder.RemoveAll(x => x == id || stepIds.Contains(x));
        Data.Settings.NudgeLog.Remove(id);
        if (Data.Settings.Focus?.GoalId == id)
            Focus.Abandon();
        Changed();
        OfferUndo($"Deleted “{goal.Title}”");
    }

    /// <summary>Puts a goal away without losing it. It comes back from the "Put away" list.</summary>
    public void ArchiveGoal(Guid id)
    {
        if (FindGoal(id) is not { } goal)
            return;
        _undoSnapshot = JsonStore.ToJson(Data);
        goal.Archived = true;
        Changed();
        OfferUndo($"Put away “{goal.Title}”");
    }

    public void RestoreGoal(Guid id)
    {
        if (FindGoal(id) is not { } goal)
            return;
        goal.Archived = false;
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
