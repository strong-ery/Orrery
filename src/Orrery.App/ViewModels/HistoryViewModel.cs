using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
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

public sealed record Stat(string Value, string Label);

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
        Stats.Clear();
        Stats.Add(new Stat(daysWorked.ToString(CultureInfo.InvariantCulture) + " / 7", "days with something done"));
        Stats.Add(new Stat(minutes >= 60 ? $"{minutes / 60:0.#} h" : $"{minutes:0} min", "of focused time"));
        Stats.Add(new Stat(goalsTouched.ToString(CultureInfo.InvariantCulture), goalsTouched == 1 ? "goal moved" : "goals moved"));
        HasAnyActivity = log.Count > 0;

        // --- Fourteen days of minutes, as bars ---
        Bars.Clear();
        var perDay = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13))
            .Select(d => (Day: d, Minutes: log.Where(c => c.Day == d).Sum(c => c.Minutes), Count: log.Count(c => c.Day == d)))
            .ToList();
        var max = Math.Max(30, perDay.Max(x => x.Minutes));
        foreach (var (day, mins, count) in perDay)
        {
            var empty = mins <= 0;
            var height = empty ? (count > 0 ? 6 : 2) : Math.Max(6, mins / max * ChartHeight);
            Bars.Add(new DayBar(height, day.ToString("ddd", CultureInfo.CurrentCulture)[..1],
                empty ? $"{day:ddd MMM d}: " + (count > 0 ? "done, no time recorded" : "nothing") : $"{day:ddd MMM d}: {mins:0} min",
                day == today, empty));
        }
        ChartCaption = $"Minutes of focused time, last 14 days (tallest bar {max:0} min)";

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
    }
}
