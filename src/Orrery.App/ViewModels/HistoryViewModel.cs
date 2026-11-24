using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public sealed record DayCell(IBrush Fill, IBrush Edge, string Tip);

public sealed class RibbonViewModel
{
    public RibbonViewModel(Category category, StreakResult streak, IReadOnlyList<DayCell> cells)
    {
        Name = category.Name;
        Dot = Palette.For(category);
        Current = streak.Current;
        CurrentText = streak.Current == 1 ? "1 day" : $"{streak.Current} days";
        Detail = $"best {streak.Best} · " + (streak.GraceAvailable ? "grace day ready" : $"grace used {streak.LastGraceUsed:MMM d}");
        Cells = cells;
    }

    public string Name { get; }
    public IBrush Dot { get; }
    public int Current { get; }
    public string CurrentText { get; }
    public string Detail { get; }
    public IReadOnlyList<DayCell> Cells { get; }
}

/// <summary>One bar of the 14-day chart: how many minutes were put in that day.</summary>
public sealed record DayBar(double Height, string Day, string Tip, bool IsToday, bool IsEmpty);

public sealed record EffortBar(string Title, IBrush Dot, string Label, double Percent);

public sealed record Stat(string Value, string Label, string Note = "");

/// <summary>One line of the record: a day heading, or something that was done that day.</summary>
public sealed partial class LogItem : ObservableObject
{
    private readonly Action<Guid>? _takeBack;

    public LogItem(string heading, string summary)
    {
        IsHeader = true;
        Title = heading;
        Sub = summary;
    }

    public LogItem(Guid id, string title, string sub, string detail, IBrush dot, Action<Guid> takeBack)
    {
        Id = id;
        Title = title;
        Sub = sub;
        Detail = detail;
        Dot = dot;
        _takeBack = takeBack;
    }

    public Guid Id { get; }
    public bool IsHeader { get; }
    public bool IsEntry => !IsHeader;
    public string Title { get; } = "";
    public string Sub { get; } = "";
    public string Detail { get; } = "";
    public IBrush? Dot { get; }

    [RelayCommand]
    private void TakeBack() => _takeBack?.Invoke(Id);
}

/// <summary>
/// Looking back. One accent colour throughout: brass means "this happened". A bucket's own colour only appears as
/// a small dot beside its name, so the page reads as shapes and numbers before it reads as colours.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private const int Days = 35;
    private const double ChartHeight = 96;

    private static readonly IBrush Brass = Palette.Brush("#E8B64C");
    private static readonly IBrush Faint = Palette.Brush("#1E7C8CFF");
    private static readonly IBrush NoEdge = Brushes.Transparent;
    private static readonly IBrush MissEdge = Palette.Brush("#807C8CFF");

    private readonly MainViewModel _main;

    public HistoryViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<Stat> Stats { get; } = [];
    public ObservableCollection<DayBar> Bars { get; } = [];
    public ObservableCollection<RibbonViewModel> Ribbons { get; } = [];
    public ObservableCollection<EffortBar> Effort { get; } = [];

    [ObservableProperty] private bool _hasEffort;
    [ObservableProperty] private bool _hasAnyActivity;
    [ObservableProperty] private string _chartCaption = "";

    // --- The whole record ---
    private const int FirstDays = 30, MoreDays = 90, SearchCap = 400;
    private int _logDays = FirstDays;

    public ObservableCollection<LogItem> Log { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _hasOlder;
    [ObservableProperty] private bool _logIsEmpty;
    [ObservableProperty] private string _logSummary = "";

    [RelayCommand]
    private void OpenReview() => _main.ShowReview();

    partial void OnSearchChanged(string value) => RefreshLog();

    [RelayCommand]
    private void ShowOlder()
    {
        _logDays += MoreDays;
        RefreshLog();
    }

    public void Refresh()
    {
        var data = _main.Data;
        var today = MainViewModel.Today;
        var log = data.Contributions;

        // --- The last week at a glance ---
        var weekStart = today.AddDays(-6);
        var week = log.Where(c => c.Day >= weekStart && c.Day <= today).ToList();
        var minutes = week.Sum(c => c.Minutes);
        var daysWorked = week.Select(c => c.Day).Distinct().Count();
        var goalsTouched = week.Select(c => c.GoalId).Distinct().Count();
        var before = log.Where(c => c.Day >= weekStart.AddDays(-7) && c.Day < weekStart).ToList();
        static string Time(double m) => m >= 60 ? $"{m / 60:0.#} h" : $"{m:0} min";
        string Prior(string value) => before.Count == 0 ? "" : $"{value} the week before";
        Stats.Clear();
        Stats.Add(new Stat(daysWorked.ToString(CultureInfo.InvariantCulture) + " / 7", "days with something done",
            Prior($"{before.Select(c => c.Day).Distinct().Count()} / 7")));
        Stats.Add(new Stat(Time(minutes), "of focused time", Prior(Time(before.Sum(c => c.Minutes)))));
        Stats.Add(new Stat(goalsTouched.ToString(CultureInfo.InvariantCulture), goalsTouched == 1 ? "goal moved" : "goals moved",
            Prior(before.Select(c => c.GoalId).Distinct().Count().ToString(CultureInfo.InvariantCulture))));
        HasAnyActivity = log.Count > 0;

        // --- Fourteen days of minutes, as bars ---
        Bars.Clear();
        var perDay = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13))
            .Select(d => (Day: d, Minutes: log.Where(c => c.Day == d).Sum(c => c.Minutes), Count: log.Count(c => c.Day == d)))
            .ToList();
        // Time when any was recorded; otherwise just how many things were done, so the chart is never an empty box.
        var byTime = perDay.Any(x => x.Minutes > 0);
        var max = byTime ? Math.Max(30, perDay.Max(x => x.Minutes)) : Math.Max(3, perDay.Max(x => x.Count));
        foreach (var (day, mins, count) in perDay)
        {
            var amount = byTime ? mins : count;
            var empty = amount <= 0;
            var height = empty ? 2 : Math.Max(6, amount / max * ChartHeight);
            var tip = byTime
                ? (empty ? $"{day:ddd MMM d}: " + (count > 0 ? "done, no time recorded" : "nothing") : $"{day:ddd MMM d}: {mins:0} min")
                : (empty ? $"{day:ddd MMM d}: nothing" : $"{day:ddd MMM d}: {count} done");
            Bars.Add(new DayBar(height, day.ToString("ddd", CultureInfo.CurrentCulture)[..1], tip, day == today, empty));
        }
        ChartCaption = byTime ? $"Minutes of focused time a day (tallest bar {max:0} min)" : "Things done a day";

        // --- Streak ribbons, one per bucket ---
        Ribbons.Clear();
        foreach (var category in data.Categories)
        {
            var goals = data.Goals.Where(g => g.CategoryId == category.Id && !g.Archived).ToList();
            var cells = new List<DayCell>();
            for (var i = Days - 1; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var status = Streaks.StatusOn(goals, log, day);
                cells.Add(status switch
                {
                    DayStatus.Met => new DayCell(Brass, NoEdge, $"{day:ddd MMM d}: everything asked was done"),
                    DayStatus.Missed => new DayCell(Brushes.Transparent, MissEdge, $"{day:ddd MMM d}: something was missed"),
                    _ => new DayCell(Faint, NoEdge, $"{day:ddd MMM d}: nothing was asked"),
                });
            }
            Ribbons.Add(new RibbonViewModel(category, Streaks.For(category, data, today), cells));
        }

        // --- Where the week went ---
        Effort.Clear();
        var byGoal = data.Goals
            .Where(g => !g.Archived)
            .Select(g => (Goal: g, Total: Progress.Between(g, log, weekStart, today)))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ToList();
        var top = byGoal.Count == 0 ? 1 : byGoal.Max(x => x.Total);
        foreach (var (goal, total) in byGoal)
            Effort.Add(new EffortBar(
                goal.Title,
                Palette.For(data.CategoryOf(goal)),
                $"{Planner.Format(total)} {goal.Measure.UnitLabel}".Trim(),
                total / top * 100));
        HasEffort = Effort.Count > 0;

        RefreshLog();
    }

    /// <summary>Everything ever done, newest first, grouped by day. Searching looks through all of it.</summary>
    private void RefreshLog()
    {
        var data = _main.Data;
        var today = MainViewModel.Today;
        var query = Search.Trim();
        var searching = query.Length > 0;

        string TitleOf(Contribution c, Goal? g) =>
            g?.Steps.FirstOrDefault(s => s.Id == c.StepId)?.Title ?? g?.Title ?? "(removed goal)";

        var rows = data.Contributions
            .Select(c => (Entry: c, Goal: data.Goals.FirstOrDefault(g => g.Id == c.GoalId)))
            .Where(x => !searching || TitleOf(x.Entry, x.Goal).Contains(query, StringComparison.OrdinalIgnoreCase)
                        || (x.Goal?.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(x => x.Entry.Day).ThenByDescending(x => x.Entry.At)
            .ToList();

        var cutoff = today.AddDays(-_logDays);
        var shown = searching ? rows.Take(SearchCap).ToList() : rows.Where(x => x.Entry.Day >= cutoff).ToList();
        HasOlder = !searching && rows.Count > shown.Count;

        Log.Clear();
        DateOnly? last = null;
        foreach (var (c, goal) in shown)
        {
            if (last != c.Day)
            {
                last = c.Day;
                var thatDay = shown.Where(x => x.Entry.Day == c.Day).ToList();
                var mins = thatDay.Sum(x => x.Entry.Minutes);
                var summary = string.Join(" · ", new[]
                {
                    thatDay.Count == 1 ? "1 thing" : $"{thatDay.Count} things",
                    mins > 0 ? Planner.Format(mins) + " min" : "",
                }.Where(s => s.Length > 0));
                Log.Add(new LogItem(c.Day == today ? "Today" : c.Day == today.AddDays(-1) ? "Yesterday"
                    : c.Day.ToString(c.Day.Year == today.Year ? "dddd, MMMM d" : "dddd, MMMM d, yyyy", CultureInfo.CurrentCulture), summary));
            }
            var title = TitleOf(c, goal);
            var sub = goal is not null && title != goal.Title ? goal.Title : data.CategoryOf(goal!)?.Name ?? "";
            var unit = goal?.Measure.UnitLabel ?? "";
            var detail = c.Minutes > 0 ? $"{Planner.Format(c.Minutes)} min"
                : goal?.Measure.Kind is MeasureKind.Amount && c.Amount > 0 ? $"{Planner.Format(c.Amount)} {unit}".Trim()
                : "";
            Log.Add(new LogItem(c.Id, title, sub, detail, Palette.For(goal is null ? null : data.CategoryOf(goal)), _main.TakeBack));
        }

        LogIsEmpty = Log.Count == 0;
        var total = data.Contributions.Count;
        LogSummary = searching
            ? (rows.Count == 1 ? "1 match" : $"{rows.Count} matches")
            : total == 1 ? "1 thing done, ever" : $"{total} things done, ever";
    }
}
