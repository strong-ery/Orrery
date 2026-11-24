using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>
/// The order of today's list. The planner decides what is on it; the person decides the order, with one rule that
/// never bends: responsibilities (obligations and routines) come before passions. A responsibility may sit late in
/// the day if that is when it suits, and moves freely among the other responsibilities, but a passion can never be
/// placed above one.
/// </summary>
public static class PlanOrder
{
    public static Guid KeyOf(PlanItem item) => item.StepId ?? item.GoalId;

    public static bool IsResponsibility(PlanItem item) => item.Category?.Role != CategoryRole.Passion;

    /// <summary>
    /// Responsibilities first, then passions. Inside each group, items the person has placed come in the order they
    /// placed them, and anything new follows in the planner's own order.
    /// </summary>
    public static List<PlanItem> Arrange(IEnumerable<PlanItem> items, IReadOnlyList<Guid> manual)
    {
        var rank = new Dictionary<Guid, int>();
        for (var i = 0; i < manual.Count; i++)
            rank.TryAdd(manual[i], i);

        return items
            .Select((item, plannerIndex) => (item, plannerIndex))
            .OrderBy(x => IsResponsibility(x.item) ? 0 : 1)
            .ThenBy(x => rank.TryGetValue(KeyOf(x.item), out var r) ? r : int.MaxValue)
            .ThenBy(x => x.plannerIndex)
            .Select(x => x.item)
            .ToList();
    }

    /// <summary>
    /// The list's order after moving one item up (-1) or down (+1), as keys to store. Null when that would break the
    /// rule or run off an end, so a refused move changes nothing.
    /// </summary>
    public static List<Guid>? Move(IReadOnlyList<PlanItem> arranged, Guid key, int delta)
    {
        var at = IndexOf(arranged, key);
        var to = at + delta;
        if (at < 0 || to < 0 || to >= arranged.Count)
            return null;
        if (IsResponsibility(arranged[at]) != IsResponsibility(arranged[to]))
            return null;
        var keys = arranged.Select(KeyOf).ToList();
        (keys[at], keys[to]) = (keys[to], keys[at]);
        return keys;
    }

    /// <summary>
    /// Where a dragged item may land: the position asked for, held inside the run of items of its own kind. Dropping
    /// a passion above every responsibility lands it just below the last one, and the other way round.
    /// </summary>
    public static int ClampTarget(IReadOnlyList<PlanItem> arranged, Guid key, int target)
    {
        var at = IndexOf(arranged, key);
        if (at < 0)
            return target;
        var kind = IsResponsibility(arranged[at]);
        var first = Enumerable.Range(0, arranged.Count).First(i => IsResponsibility(arranged[i]) == kind);
        var last = Enumerable.Range(0, arranged.Count).Last(i => IsResponsibility(arranged[i]) == kind);
        return Math.Clamp(target, first, last);
    }

    /// <summary>The order after dropping an item at a position (clamped by <see cref="ClampTarget"/>), or null if nothing changes.</summary>
    public static List<Guid>? MoveTo(IReadOnlyList<PlanItem> arranged, Guid key, int target)
    {
        var at = IndexOf(arranged, key);
        if (at < 0)
            return null;
        target = ClampTarget(arranged, key, target);
        if (target == at)
            return null;
        var keys = arranged.Select(KeyOf).ToList();
        keys.RemoveAt(at);
        keys.Insert(target, key);
        return keys;
    }

    /// <summary>Why a move was refused, or null if it is allowed.</summary>
    public static string? WhyNot(IReadOnlyList<PlanItem> arranged, Guid key, int delta)
    {
        var at = IndexOf(arranged, key);
        var to = at + delta;
        if (at < 0 || to < 0 || to >= arranged.Count)
            return null; // an end of the list is not worth explaining
        return IsResponsibility(arranged[at]) != IsResponsibility(arranged[to])
            ? (IsResponsibility(arranged[at]) ? "Responsibilities stay ahead of passions." : "Passions stay behind responsibilities.")
            : null;
    }

    private static int IndexOf(IReadOnlyList<PlanItem> arranged, Guid key)
    {
        for (var i = 0; i < arranged.Count; i++)
            if (KeyOf(arranged[i]) == key)
                return i;
        return -1;
    }
}

