using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.App.ViewModels;

/// <summary>One line on the calendar or in the agenda. Quiet by default; only deadlines get the brass.</summary>
public sealed partial class CalendarItemViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CalendarEvent _event;

    public CalendarItemViewModel(MainViewModel main, CalendarEvent e, IBrush bucket)
    {
        _main = main;
        _event = e;
        Title = e.Title;
        Detail = e.Detail;
        Bucket = bucket;
        IsDeadline = e.Kind is CalendarKind.Deadline or CalendarKind.Part;
        IsWorked = e.Kind == CalendarKind.Worked;
        IsPlanned = e.Kind == CalendarKind.Planned;
        GoalId = e.GoalId;
        StepId = e.StepId;
        When = e.Day.ToString("ddd MMM d", CultureInfo.CurrentCulture);
        Kind = e.Kind switch
        {
            CalendarKind.Deadline => "Deadline",
            CalendarKind.Due => "Due by",
            CalendarKind.Part => "Part due",
            CalendarKind.Available => "Opens up",
            CalendarKind.Planned => "Planned",
            _ => "Worked on",
        };
    }

    public string Title { get; }
    public string Detail { get; }
    public string Kind { get; }
    public string When { get; }
    public IBrush Bucket { get; }
    public bool IsDeadline { get; }
    public bool IsWorked { get; }
    public bool IsPlanned { get; }
    public Guid GoalId { get; }
    public Guid? StepId { get; }

    [RelayCommand]
    private void Open() => _main.Inspect(_event.GoalId, _event.StepId);
}

/// <summary>Something waiting to be put on a day. Dragged from the tray onto the month.</summary>
public sealed class TrayItemViewModel
{
    private readonly MainViewModel _main;

    public TrayItemViewModel(MainViewModel main, Schedulable item, IBrush bucket)
    {
        _main = main;
        GoalId = item.GoalId;
        StepId = item.StepId;
        Title = item.Title;
        Detail = item.StepId is null ? $"{Planner.Format(item.Minutes)} min" : $"{item.GoalTitle} · {Planner.Format(item.Minutes)} min";
        Bucket = bucket;
    }

    public Guid GoalId { get; }
    public Guid? StepId { get; }
    public string Title { get; }
    public string Detail { get; }
    public IBrush Bucket { get; }

    public void Open() => _main.Inspect(GoalId, StepId);
}

/// <summary>A line in the preview of what Today would look like.</summary>
public sealed record PreviewLine(string Title, string Note);

public sealed partial class DayCellViewModel : ObservableObject
{
    public DayCellViewModel(DateOnly date, bool inMonth, bool isToday, bool isPast,
        IReadOnlyList<CalendarItemViewModel> upcoming, int worked)
    {
        Date = date;
        Number = date.Day.ToString(CultureInfo.InvariantCulture);
        InMonth = inMonth;
        IsToday = isToday;
        IsPast = isPast;
        Chips = upcoming.Take(3).ToList();
        More = upcoming.Count > 3 ? $"+{upcoming.Count - 3} more" : "";
        HasMore = upcoming.Count > 3;
        HasWorked = worked > 0;
        WorkedTip = worked == 1 ? "Worked on 1 goal" : $"Worked on {worked} goals";
        Opacity = inMonth ? 1 : 0.38;
    }

    /// <summary>How full the day is as a share of a usual day (0..1 for the bar), and whether it is over.</summary>
    public double Load { get; set; }
    public bool Over { get; set; }
    public string LoadTip { get; set; } = "";
    public bool ShowLoad { get; set; }

    public DateOnly Date { get; }
    public string Number { get; }
    public bool InMonth { get; }
    public bool IsToday { get; }
    public bool IsPast { get; }
    public IReadOnlyList<CalendarItemViewModel> Chips { get; }
    public string More { get; }
    public bool HasMore { get; }
    public bool HasWorked { get; }
    public string WorkedTip { get; }
    public double Opacity { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// Where you are headed. A month grid for the shape of things, and an agenda beside it for the detail of one day
/// and what comes next. It reads the same data as everything else, so there is nothing extra to maintain.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private DateOnly _month = FirstOfMonth(MainViewModel.Today);
    private DateOnly _selected = MainViewModel.Today;

    private OrreryData? _draft;

    public CalendarViewModel(MainViewModel main) => _main = main;

    public DateOnly Month => _month;
    public DateOnly Selected => _selected;

    /// <summary>Back to the month and day that were showing when the app was closed.</summary>
    public void Restore(DateOnly month, DateOnly day)
    {
        _month = FirstOfMonth(month);
        _selected = day;
        Refresh();
    }

    public ObservableCollection<TrayItemViewModel> Tray { get; } = [];
    public ObservableCollection<PreviewLine> Added { get; } = [];
    public ObservableCollection<PreviewLine> Removed { get; } = [];
    public ObservableCollection<PreviewLine> TodayAfter { get; } = [];

    [ObservableProperty] private bool _isPlanning;
    [ObservableProperty] private bool _showPreview;
    [ObservableProperty] private int _changeCount;
    [ObservableProperty] private string _changeLabel = "";
    [ObservableProperty] private bool _trayIsEmpty;
    [ObservableProperty] private bool _previewIsSame;

    partial void OnIsPlanningChanged(bool value)
    {
        OnPropertyChanged(nameof(NotPlanning));
        OnPropertyChanged(nameof(ShowTray));
    }
    partial void OnShowPreviewChanged(bool value) => OnPropertyChanged(nameof(ShowTray));
    public bool NotPlanning => !IsPlanning;
    public bool HasChanges => ChangeCount > 0;
    public bool HasAdded => Added.Count > 0;
    public bool HasRemoved => Removed.Count > 0;
    public string PreviewButton => ShowPreview ? "Back to the tray" : "Preview Today";
    partial void OnChangeCountChanged(int value) => OnPropertyChanged(nameof(HasChanges));

    [RelayCommand]
    private void TogglePreview()
    {
        ShowPreview = !ShowPreview;
        OnPropertyChanged(nameof(PreviewButton));
    }
    public bool ShowTray => IsPlanning && !ShowPreview;

    [RelayCommand]
    private void StartPlanning()
    {
        _draft = JsonStore.FromJson(JsonStore.ToJson(_main.Data));
        ShowPreview = false;
        IsPlanning = true;
        Refresh();
    }

    [RelayCommand]
    private void Discard()
    {
        _draft = null;
        IsPlanning = false;
        ShowPreview = false;
        Refresh();
    }

    /// <summary>Makes the plan real: copies the planned days onto your goals and steps, and Today picks them up.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (_draft is { } draft)
            foreach (var goal in _main.Data.Goals)
                if (draft.Goals.FirstOrDefault(g => g.Id == goal.Id) is { } planned)
                {
                    goal.PlannedFor = planned.PlannedFor;
                    foreach (var step in goal.Steps)
                        if (planned.Steps.FirstOrDefault(s => s.Id == step.Id) is { } ps)
                            step.PlannedFor = ps.PlannedFor;
                }
        _draft = null;
        IsPlanning = false;
        ShowPreview = false;
        _main.Changed();
    }

    /// <summary>Put an item on a day, or take it off with null. Only while planning, and never in the past.</summary>
    public void PlanOn(Guid goalId, Guid? stepId, DateOnly? day)
    {
        if (_draft is null || day is { } d && d < MainViewModel.Today)
            return;
        Scheduling.Plan(_draft, goalId, stepId, day);
        Refresh();
    }

    public ObservableCollection<DayCellViewModel> Cells { get; } = [];
    public ObservableCollection<CalendarItemViewModel> DayItems { get; } = [];
    public ObservableCollection<CalendarItemViewModel> ComingUp { get; } = [];
    [ObservableProperty] private string[] _weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _dayTitle = "";
    [ObservableProperty] private string _daySubtitle = "";
    [ObservableProperty] private bool _dayIsEmpty;
    [ObservableProperty] private bool _hasComingUp;
    [ObservableProperty] private bool _showingToday = true;

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);

    [RelayCommand]
    private void Previous()
    {
        _month = _month.AddMonths(-1);
        Refresh();
        _main.UiChanged();
    }

    [RelayCommand]
    private void Next()
    {
        _month = _month.AddMonths(1);
        Refresh();
        _main.UiChanged();
    }

    [RelayCommand]
    private void GoToToday()
    {
        _month = FirstOfMonth(MainViewModel.Today);
        _selected = MainViewModel.Today;
        Refresh();
        _main.UiChanged();
    }

    [RelayCommand]
    private void Select(DayCellViewModel cell)
    {
        _selected = cell.Date;
        if (!cell.InMonth)
            _month = FirstOfMonth(cell.Date);
        Refresh();
    }

    public void Refresh()
    {
        var data = IsPlanning && _draft is not null ? _draft : _main.Data;
        var today = MainViewModel.Today;

        // Six weeks cover any month, starting on whichever day the week starts on.
        var first = (int)Orrery.Core.Engine.WeekStart.First;
        Weekdays = Enumerable.Range(0, 7)
            .Select(i => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((first + i) % 7)))
            .ToArray();
        var lead = ((int)_month.DayOfWeek - first + 7) % 7;
        var gridStart = _month.AddDays(-lead);
        var gridEnd = gridStart.AddDays(41);
        var events = CalendarModel.Events(data, gridStart, gridEnd.AddDays(30), today);

        IBrush BucketOf(Guid goalId) => Palette.For(data.Goals.FirstOrDefault(g => g.Id == goalId) is { } g ? data.CategoryOf(g) : null);
        CalendarItemViewModel Item(CalendarEvent e) => new(_main, e, BucketOf(e.GoalId));

        Cells.Clear();
        for (var i = 0; i < 42; i++)
        {
            var day = gridStart.AddDays(i);
            var today1 = events.Where(e => e.Day == day).ToList();
            var upcoming = today1.Where(e => e.Kind != CalendarKind.Worked).Select(Item).ToList();
            var worked = today1.Count(e => e.Kind == CalendarKind.Worked);
            var load = Scheduling.Load(data, day);
            var fullness = Scheduling.Fullness(data, day);
            Cells.Add(new DayCellViewModel(day, day.Month == _month.Month, day == today, day < today, upcoming, worked)
            {
                IsSelected = day == _selected,
                ShowLoad = IsPlanning && day >= today,
                Load = Math.Min(1, fullness),
                Over = fullness > 1.0001,
                LoadTip = load == 0 ? "Nothing planned" : $"{Planner.Format(load)} of {data.Settings.DayCapacityMinutes} min planned",
            });
        }

        MonthTitle = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        ShowingToday = _month == FirstOfMonth(today) && _selected == today;

        DayItems.Clear();
        foreach (var e in events.Where(e => e.Day == _selected))
            DayItems.Add(Item(e));
        DayIsEmpty = DayItems.Count == 0;
        DayTitle = _selected == today ? "Today" : _selected == today.AddDays(1) ? "Tomorrow" : _selected.ToString("dddd", CultureInfo.CurrentCulture);
        DaySubtitle = _selected.ToString("MMMM d", CultureInfo.CurrentCulture);

        // What is coming, regardless of which day is selected, so "where am I headed" is always on screen.
        ComingUp.Clear();
        foreach (var e in events.Where(e => e.Day > today && e.Day <= today.AddDays(30) && e.Kind != CalendarKind.Worked).Take(8))
            ComingUp.Add(Item(e));
        HasComingUp = ComingUp.Count > 0;

        RefreshPlanning(data, today);
    }

    private void RefreshPlanning(OrreryData data, DateOnly today)
    {
        Tray.Clear();
        Added.Clear();
        Removed.Clear();
        TodayAfter.Clear();
        if (!IsPlanning)
        {
            ChangeCount = 0;
            return;
        }

        foreach (var c in Scheduling.Candidates(data, today).Where(c => c.Day is null))
        {
            var goal = data.Goals.First(g => g.Id == c.GoalId);
            Tray.Add(new TrayItemViewModel(_main, c, Palette.For(data.CategoryOf(goal))));
        }
        TrayIsEmpty = Tray.Count == 0;

        var changes = 0;
        var live = _main.Data;
        var before = Scheduling.Candidates(live, today);
        foreach (var c in Scheduling.Candidates(data, today))
            if (before.FirstOrDefault(o => o.GoalId == c.GoalId && o.StepId == c.StepId)?.Day != c.Day)
                changes++;
        ChangeCount = changes;
        ChangeLabel = changes == 0 ? "No changes yet" : changes == 1 ? "1 change" : $"{changes} changes";

        // What Today would be, against what it is now.
        var request = new PlanRequest(today, live.Settings.MaxToday, live.Settings.Bias);
        var now = Planner.Suggest(live, request);
        var after = Planner.Suggest(data, request);
        static string Key(PlanItem p) => $"{p.GoalId}/{p.StepId}";
        static string Name(PlanItem p) => p.Title;
        foreach (var p in after)
            TodayAfter.Add(new PreviewLine(Name(p), p.StepId is null ? p.Reason : p.GoalTitle));
        foreach (var p in after.Where(p => now.All(n => Key(n) != Key(p))))
            Added.Add(new PreviewLine(Name(p), p.Reason));
        foreach (var p in now.Where(p => after.All(n => Key(n) != Key(p))))
            Removed.Add(new PreviewLine(Name(p), "moves off today"));
        PreviewIsSame = Added.Count == 0 && Removed.Count == 0;
        OnPropertyChanged(nameof(HasAdded));
        OnPropertyChanged(nameof(HasRemoved));
    }
}
