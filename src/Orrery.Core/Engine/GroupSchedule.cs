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

/// <summary>When a group, or a step inside one, is wanted, and why.</summary>
/// <param name="Due">The day.</param>
/// <param name="Group">The group whose date it is.</param>
/// <param name="Inherited">True when the date comes from a later group that cannot start until this is done.</param>
public sealed record Need(DateOnly Due, Step Group, bool Inherited);

/// <summary>
/// The one place that answers "when is this wanted?" for a goal with group dates. A step takes the date of the groups
/// it sits in, and also the date of any later group that waits on it, whichever is sooner, so Today, the calendar and
/// the editor all say the same thing.
/// </summary>
public sealed class GroupNeeds
{
    private readonly Goal _goal;
    private readonly DependencyGraph _graph;
    private readonly Dictionary<Guid, DateOnly> _dates;
    private readonly Dictionary<Guid, Need?> _leafCache = [];

    public GroupNeeds(Goal goal, DateOnly today, DependencyGraph graph)
    {
        _goal = goal;
        _graph = graph;
        _dates = GroupSchedule.Dates(goal, today).ToDictionary(d => d.Group.Id, d => d.Due);
    }

    public bool Any => _dates.Count > 0;

    /// <summary>The date this group has been given itself, if any.</summary>
    public DateOnly? OwnDate(Step group) => _dates.TryGetValue(group.Id, out var d) ? d : null;

    private Need? Within(Step leaf, bool inherited)
    {
        Need? best = null;
        for (var group = _goal.ParentOf(leaf); group is not null; group = _goal.ParentOf(group))
            if (_dates.TryGetValue(group.Id, out var due) && (best is null || due < best.Due))
                best = new Need(due, group, inherited);
        return best;
    }

    /// <summary>When this step is wanted: its own groups' dates, or those of later groups waiting on it.</summary>
    public Need? Of(Step leaf)
    {
        if (_leafCache.TryGetValue(leaf.Id, out var cached))
            return cached;
        var best = Within(leaf, false);
        foreach (var later in _graph.Dependents(leaf.Id).Where(d => _goal.Steps.Contains(d)))
            if (Within(later, true) is { } n && (best is null || n.Due < best.Due))
                best = n;
        return _leafCache[leaf.Id] = best;
    }

    /// <summary>
    /// When a group is wanted, worth showing only if it has a date of its own or a later group needs it sooner.
    /// </summary>
    public Need? OfGroup(Step group)
    {
        var own = OwnDate(group);
        Need? best = null;
        foreach (var leaf in _goal.Leaves().Where(l => !_goal.IsDone(l) && IsInside(l, group)))
            if (Of(leaf) is { } n && (best is null || n.Due < best.Due))
                best = n;
        if (best is null)
            return own is { } d ? new Need(d, group, false) : null;
        if (best.Inherited && (own is null || best.Due < own))
            return best;
        return own is { } o ? new Need(o, group, false) : null;
    }

    private bool IsInside(Step leaf, Step group)
    {
        for (var p = _goal.ParentOf(leaf); p is not null; p = _goal.ParentOf(p))
            if (p.Id == group.Id)
                return true;
        return false;
    }
}
