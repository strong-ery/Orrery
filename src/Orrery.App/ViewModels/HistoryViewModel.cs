using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Orrery.Core.Engine;
using Orrery.Core.Models;

namespace Orrery.App.ViewModels;

public sealed record DayCell(IBrush Brush, string Tip);

public sealed class RibbonViewModel
{
    public RibbonViewModel(Category category, StreakResult streak, IReadOnlyList<DayCell> cells)
    {
        Name = category.Name;
        Accent = Palette.For(category);
        Current = streak.Current;
        Detail = $"best {streak.Best} · " + (streak.GraceAvailable ? "grace day ready" : $"grace used {streak.LastGraceUsed:MMM d}");
        Cells = cells;
    }

    public string Name { get; }
    public IBrush Accent { get; }
    public int Current { get; }
    public string Detail { get; }
    public IReadOnlyList<DayCell> Cells { get; }
}

public sealed record EffortBar(string Title, IBrush Accent, string Label, double Percent);

public partial class HistoryViewModel : ObservableObject
{
    private const int Days = 35;
    private static readonly IBrush RestBrush = Palette.Brush("#226B7CFF");
    private static readonly IBrush MissBrush = Palette.Brush("#99FF6B7A");

    private readonly MainViewModel _main;

    public HistoryViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<RibbonViewModel> Ribbons { get; } = [];
    public ObservableCollection<EffortBar> Effort { get; } = [];

    [ObservableProperty]
    private bool _hasEffort;

    public void Refresh()
    {
        var data = _main.Data;
        var today = MainViewModel.Today;

        Ribbons.Clear();
        foreach (var category in data.Categories)
        {
            var goals = data.Goals.Where(g => g.CategoryId == category.Id && !g.Archived).ToList();
            var accent = Palette.For(category);
            var cells = new List<DayCell>();
            for (var i = Days - 1; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var status = Streaks.StatusOn(goals, data.Contributions, day);
                cells.Add(new DayCell(
                    status switch { DayStatus.Met => accent, DayStatus.Missed => MissBrush, _ => RestBrush },
                    $"{day:ddd MMM d}: {status.ToString().ToLowerInvariant()}"));
            }
            Ribbons.Add(new RibbonViewModel(category, Streaks.For(category, data, today), cells));
        }

        Effort.Clear();
        var week = data.Goals
            .Where(g => !g.Archived)
            .Select(g => (Goal: g, Total: Progress.Between(g, data.Contributions, today.AddDays(-6), today)))
            .Where(x => x.Total > 0)
            .OrderByDescending(x => x.Total)
            .ToList();
        var max = week.Count == 0 ? 1 : week.Max(x => x.Total);
        foreach (var (goal, total) in week)
            Effort.Add(new EffortBar(
                goal.Title,
                Palette.For(data.CategoryOf(goal)),
                $"{Planner.Format(total)} {goal.Measure.UnitLabel}".Trim(),
                total / max * 100));
        HasEffort = Effort.Count > 0;
    }
}
