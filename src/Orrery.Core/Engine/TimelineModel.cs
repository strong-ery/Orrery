using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>One goal on the timeline: when it started, when it must be done, how far along it is, and what to do next.</summary>
public sealed record TimelineBar(
    Guid GoalId,
    string Title,
    CategoryRole Role,
    DateOnly Created,
    DateOnly? Deadline,
    double? Fraction,
    string Next,
    bool NextIsWaiting,
    string Note,
    IReadOnlyList<DateOnly> Planned,
    IReadOnlyList<DateOnly> Due,
    IReadOnlyList<DateOnly> Parts,
    bool Pressing);

public sealed record TimelineLane(CategoryRole Role, string Name, IReadOnlyList<TimelineBar> Bars);

/// <summary>
/// Goals laid out in time: one lane per bucket, soonest deadline first, open-ended goals last. Everything here is read
/// from the same data as the planner and the calendar; nothing is stored.
/// </summary>
public static class TimelineModel
{
    public static IReadOnlyList<TimelineLane> Build(OrreryData data, DateOnly today, int horizonDays)
    {
        var log = data.Contributions;
        var graph = new DependencyGraph(data.Goals, g => Progress.IsComplete(g, log));
        var events = CalendarModel.Events(data, today, today.AddDays(horizonDays), today);

        var bars = new List<TimelineBar>();
        foreach (var goal in data.Goals.Where(g => !g.Archived && !Progress.IsComplete(g, log)))
        {
            var category = data.CategoryOf(goal);
            var mine = events.Where(e => e.GoalId == goal.Id).ToList();

            var next = "";
            var waiting = false;
            var open = goal.Leaves().Where(s => !s.Done).ToList();
            if (open.Count > 0)
            {
                var ready = open.FirstOrDefault(s => graph.BlockedReason(s, today) is null);
                next = ready?.Title ?? open[0].Title;
                waiting = ready is null;
            }
            else if (graph.BlockedReason(goal, today) is not null)
            {
                waiting = true;
            }

            var note = goal.Rhythm.Kind switch
            {
                RhythmKind.Repeat when goal.Rhythm.EveryDays <= 1 => "every day",
                RhythmKind.Repeat => $"every {goal.Rhythm.EveryDays} days",
                RhythmKind.Cadence => $"{Planner.Format(goal.Rhythm.PerWeek)} a week",
                _ => "",
            };

            bars.Add(new TimelineBar(
                goal.Id, goal.Title, category?.Role ?? CategoryRole.Obligation, goal.Created, goal.Deadline,
                Progress.Fraction(goal, log), next, waiting, note,
                mine.Where(e => e.Kind == CalendarKind.Planned).Select(e => e.Day).Distinct().ToList(),
                mine.Where(e => e.Kind == CalendarKind.Due).Select(e => e.Day).Distinct().ToList(),
                mine.Where(e => e.Kind == CalendarKind.Part).Select(e => e.Day).Distinct().ToList(),
                Horizons.Of(goal, log, today) == Horizon.Now));
        }

        return bars
            .GroupBy(b => b.Role)
            .OrderBy(g => g.Key switch { CategoryRole.Obligation => 0, CategoryRole.Routine => 1, _ => 2 })
            .Select(g => new TimelineLane(g.Key, LaneName(data, g.Key),
                g.OrderBy(b => b.Deadline ?? DateOnly.MaxValue).ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    private static string LaneName(OrreryData data, CategoryRole role) =>
        data.Categories.FirstOrDefault(c => c.Role == role)?.Name ?? role.ToString();
}
