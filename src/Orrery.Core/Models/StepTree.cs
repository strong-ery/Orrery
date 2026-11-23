namespace Orrery.Core.Models;

/// <summary>
/// Goal &gt; section &gt; step. A section is just a step that other steps name as their parent, so the storage stays
/// one flat list. Only leaves are ever worked on; a section is finished when everything inside it is, takes the
/// total of its children's estimates, and what it waits for, its children wait for too.
/// </summary>
public static class StepTree
{
    public static bool IsSection(this Goal goal, Step step) => step.IsGroup || goal.Steps.Any(s => s.ParentId == step.Id);

    public static IEnumerable<Step> ChildrenOf(this Goal goal, Step step) => goal.Steps.Where(s => s.ParentId == step.Id);

    public static Step? ParentOf(this Goal goal, Step step) =>
        step.ParentId is { } id ? goal.Steps.FirstOrDefault(s => s.Id == id) : null;

    /// <summary>The steps you can actually do: everything that isn't a container.</summary>
    public static IEnumerable<Step> Leaves(this Goal goal) => goal.Steps.Where(s => !goal.IsSection(s));

    /// <summary>Top-level entries in order: sections and loose steps.</summary>
    public static IEnumerable<Step> Roots(this Goal goal) =>
        goal.Steps.Where(s => s.ParentId is null || goal.ParentOf(s) is null);

    public static bool IsDone(this Goal goal, Step step) =>
        goal.IsSection(step) ? goal.ChildrenOf(step).ToList() is { Count: > 0 } kids && kids.All(goal.IsDone) : step.Done;

    /// <summary>Own estimate for a leaf, the sum of its children for a section.</summary>
    public static int? EstimateOf(this Goal goal, Step step)
    {
        if (!goal.IsSection(step))
            return step.EstimateMinutes;
        var parts = goal.ChildrenOf(step).Select(goal.EstimateOf).Where(m => m is not null).ToList();
        return parts.Count == 0 ? null : parts.Sum(m => m!.Value);
    }

    public static (int Done, int Total) CountOf(this Goal goal, Step section)
    {
        var leaves = goal.ChildrenOf(section).SelectMany(c => goal.IsSection(c) ? goal.LeavesUnder(c) : [c]).ToList();
        return (leaves.Count(s => s.Done), leaves.Count);
    }

    private static IEnumerable<Step> LeavesUnder(this Goal goal, Step section) =>
        goal.ChildrenOf(section).SelectMany(c => goal.IsSection(c) ? goal.LeavesUnder(c) : [c]);

    /// <summary>Removes a step and, for a section, everything inside it.</summary>
    public static void RemoveStep(this Goal goal, Guid stepId)
    {
        var doomed = new HashSet<Guid> { stepId };
        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var s in goal.Steps)
                if (s.ParentId is { } p && doomed.Contains(p) && doomed.Add(s.Id))
                    grew = true;
        }
        goal.Steps.RemoveAll(s => doomed.Contains(s.Id));
    }

    /// <summary>The step and everything inside it, in stored order.</summary>
    public static List<Step> BlockOf(this Goal goal, Step step)
    {
        var ids = new HashSet<Guid> { step.Id };
        foreach (var s in goal.Steps)
            if (s.ParentId is { } p && ids.Contains(p))
                ids.Add(s.Id);
        return goal.Steps.Where(s => ids.Contains(s.Id)).ToList();
    }

    /// <summary>Swaps a step (with its contents) with the sibling before it. False if it is already first.</summary>
    public static bool MoveUp(this Goal goal, Step step)
    {
        var siblings = goal.Steps.Where(s => s.ParentId == step.ParentId).ToList();
        var at = siblings.IndexOf(step);
        if (at <= 0)
            return false;
        var block = goal.BlockOf(step);
        goal.Steps.RemoveAll(block.Contains);
        goal.Steps.InsertRange(goal.Steps.IndexOf(siblings[at - 1]), block);
        return true;
    }

    public static bool MoveDown(this Goal goal, Step step)
    {
        var siblings = goal.Steps.Where(s => s.ParentId == step.ParentId).ToList();
        var at = siblings.IndexOf(step);
        return at >= 0 && at < siblings.Count - 1 && goal.MoveUp(siblings[at + 1]);
    }

    /// <summary>Puts a step inside another section (or back at the top with null). Refuses to put a step inside itself.</summary>
    public static bool MoveTo(this Goal goal, Step step, Guid? newParent)
    {
        if (step.ParentId == newParent)
            return true;
        var block = goal.BlockOf(step);
        if (newParent is { } np && block.Any(b => b.Id == np))
            return false;
        goal.Steps.RemoveAll(block.Contains);
        step.ParentId = newParent;
        var at = goal.Steps.Count;
        if (newParent is { } parentId && goal.Steps.FirstOrDefault(s => s.Id == parentId) is { } parent)
        {
            var parentBlock = goal.BlockOf(parent);
            at = goal.Steps.IndexOf(parentBlock[^1]) + 1;
        }
        goal.Steps.InsertRange(at, block);
        return true;
    }
}
