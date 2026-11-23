using Orrery.Core.Models;

namespace Orrery.Core.Engine;

public enum CalendarKind
{
    /// <summary>A goal's finish date.</summary>
    Deadline,
    /// <summary>A repeating goal comes due.</summary>
    Due,
    /// <summary>A step that was held back until this day becomes available.</summary>
    Available,
    /// <summary>Something was worked on (a day in the past).</summary>
    Worked,
}

public sealed record CalendarEvent(DateOnly Day, CalendarKind Kind, Guid GoalId, Guid? StepId, string Title, string Detail);

/// <summary>
/// What is on which day, for the calendar. Everything here is derived from the same data the planner reads:
/// deadlines, the day each repeating goal next comes due, the day a held-back step opens up, and, for the
/// past, what was actually worked on.
/// </summary>
public static class CalendarModel
{
    public static IReadOnlyList<CalendarEvent> Events(OrreryData data, DateOnly from, DateOnly to, DateOnly today)
    {
        var events = new List<CalendarEvent>();
        var log = data.Contributions;

        foreach (var goal in data.Goals.Where(g => !g.Archived))
        {
            var complete = Progress.IsComplete(goal, log);

            if (goal.Deadline is { } deadline && deadline >= from && deadline <= to && !complete)
                events.Add(new CalendarEvent(deadline, CalendarKind.Deadline, goal.Id, null, goal.Title, "Deadline"));

            if (goal.Rhythm.Kind == RhythmKind.Repeat)
                foreach (var due in DueDays(goal, log, Max(from, today), to))
                    events.Add(new CalendarEvent(due, CalendarKind.Due, goal.Id, null, goal.Title,
                        goal.Rhythm.EveryDays <= 1 ? "Every day" : $"Every {goal.Rhythm.EveryDays} days"));

            foreach (var step in goal.Leaves())
                if (!step.Done && step.BlockedUntil is { } open && open >= from && open <= to)
                    events.Add(new CalendarEvent(open, CalendarKind.Available, goal.Id, step.Id, step.Title, $"Opens up · {goal.Title}"));
        }

        foreach (var group in log.Where(c => c.Day >= from && c.Day <= to && c.Day <= today).GroupBy(c => (c.Day, c.GoalId)))
        {
            if (data.Goals.FirstOrDefault(g => g.Id == group.Key.GoalId) is not { } goal)
                continue;
            var minutes = group.Sum(c => c.Minutes);
            var detail = minutes > 0 ? $"{Planner.Format(minutes)} min" : "done";
            events.Add(new CalendarEvent(group.Key.Day, CalendarKind.Worked, goal.Id, null, goal.Title, detail));
        }

        return events.OrderBy(e => e.Day).ThenBy(e => e.Kind).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The last day of each window of a repeating goal that has not already been met.</summary>
    private static IEnumerable<DateOnly> DueDays(Goal goal, IReadOnlyList<Contribution> log, DateOnly from, DateOnly to)
    {
        var every = Math.Max(1, goal.Rhythm.EveryDays);
        var target = goal.Measure.Kind is MeasureKind.Amount or MeasureKind.Time && goal.Measure.Target > 0 ? goal.Measure.Target : 1;
        var offset = Math.Max(0, from.DayNumber - goal.Created.DayNumber);
        var windowStart = goal.Created.AddDays(offset - offset % every);
        for (var start = windowStart; start <= to; start = start.AddDays(every))
        {
            var end = start.AddDays(every - 1);
            if (end < from)
                continue;
            if (Progress.Between(goal, log, start, end) >= target - 1e-9)
                continue; // that window is already done
            yield return end;
        }
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
