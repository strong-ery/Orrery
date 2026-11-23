using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

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
        IsDeadline = e.Kind == CalendarKind.Deadline;
        IsWorked = e.Kind == CalendarKind.Worked;
        When = e.Day.ToString("ddd MMM d", CultureInfo.CurrentCulture);
        Kind = e.Kind switch
        {
            CalendarKind.Deadline => "Deadline",
            CalendarKind.Due => "Due by",
            CalendarKind.Available => "Opens up",
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

    [RelayCommand]
    private void Open() => _main.Inspect(_event.GoalId, _event.StepId);
}

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

    public CalendarViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<DayCellViewModel> Cells { get; } = [];
    public ObservableCollection<CalendarItemViewModel> DayItems { get; } = [];
    public ObservableCollection<CalendarItemViewModel> ComingUp { get; } = [];
    public string[] Weekdays { get; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

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
    }

    [RelayCommand]
    private void Next()
    {
        _month = _month.AddMonths(1);
        Refresh();
    }

    [RelayCommand]
    private void GoToToday()
    {
        _month = FirstOfMonth(MainViewModel.Today);
        _selected = MainViewModel.Today;
        Refresh();
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
        var data = _main.Data;
        var today = MainViewModel.Today;

        // Six Monday-first weeks cover any month.
        var lead = ((int)_month.DayOfWeek + 6) % 7;
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
            Cells.Add(new DayCellViewModel(day, day.Month == _month.Month, day == today, day < today, upcoming, worked)
            {
                IsSelected = day == _selected,
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
    }
}
