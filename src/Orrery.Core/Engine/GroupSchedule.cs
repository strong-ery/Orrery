using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>
/// When each group in a goal is meant to be finished. Custom dates are the ones you set; even dates are worked out on
/// the spot from the groups still to do, so finishing one early spreads the rest over the time that is left.
/// </summary>
public static class GroupSchedule
{
    /// <summary>The dates for every unfinished group in the goal, in the order the groups appear.</summary>
    public static IReadOnlyList<(Step Group, DateOnly Due)> Dates(Goal goal, DateOnly today)
    {
        var open = goal.Steps.Where(s => goal.IsSection(s) && !goal.IsDone(s)).ToList();
        switch (goal.GroupDates)
        {
            case GroupDateMode.Custom:
                return open.Where(s => s.Due is not null).Select(s => (s, s.Due!.Value)).ToList();
            case GroupDateMode.Even when goal.Deadline is { } deadline:
            {
                var top = open.Where(s => goal.ParentOf(s) is null).ToList();
                var days = Math.Max(0, deadline.DayNumber - today.DayNumber);
                var result = new List<(Step, DateOnly)>();
                for (var i = 0; i < top.Count; i++)
                    result.Add((top[i], today.AddDays((int)Math.Round(days * (i + 1.0) / top.Count))));
                return result;
            }
            default:
                return [];
        }
    }

    public static DateOnly? DueOf(Goal goal, Step group, DateOnly today)
    {
        foreach (var (g, due) in Dates(goal, today))
            if (g.Id == group.Id)
                return due;
        return null;
    }
}
