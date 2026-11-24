using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>
/// What has to be finished before what. Steps depend on steps (in any goal) and goals depend on goals; a
/// goal depending on a goal means "all of that goal first". Both live in one graph so a cycle is caught
/// wherever it hides, and a dependency that could never be satisfied is refused.
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<Guid, Goal> _goals;
    private readonly Dictionary<Guid, Step> _steps = [];
    private readonly Dictionary<Guid, Goal> _goalOfStep = [];
    private readonly Func<Goal, bool> _isGoalComplete;

    public DependencyGraph(IEnumerable<Goal> goals, Func<Goal, bool> isGoalComplete)
    {
        _goals = goals.ToDictionary(g => g.Id);
        _isGoalComplete = isGoalComplete;
        foreach (var g in _goals.Values)
            foreach (var s in g.Steps)
            {
                _steps[s.Id] = s;
                _goalOfStep[s.Id] = g;
            }
    }

    /// <summary>Everything this node needs first. A goal node needs all of its steps.</summary>
    private IEnumerable<Guid> Prerequisites(Guid node)
    {
        if (_steps.TryGetValue(node, out var step))
        {
            var owner = _goalOfStep[node];
            // A step waits for what it names, and for what every section around it names.
            for (var s = step; s is not null; s = owner.ParentOf(s))
                foreach (var id in s.DependsOn)
                    if (_steps.ContainsKey(id))
                        yield return id;
            // A section is finished when its children are.
            foreach (var child in owner.ChildrenOf(step))
                yield return child.Id;
            foreach (var id in owner.DependsOnGoals)
                if (_goals.ContainsKey(id))
                    yield return id;
        }
        else if (_goals.TryGetValue(node, out var goal))
        {
            foreach (var s in goal.Steps)
                yield return s.Id;
            foreach (var id in goal.DependsOnGoals)
                if (_goals.ContainsKey(id))
                    yield return id;
        }
    }

    /// <summary>True when <paramref name="target"/> is reachable from <paramref name="from"/> by following prerequisites.</summary>
    private bool Requires(Guid from, Guid target)
    {
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!seen.Add(node))
                continue;
            foreach (var next in Prerequisites(node))
            {
                if (next == target)
                    return true;
                stack.Push(next);
            }
        }
        return false;
    }

    /// <summary>Would making <paramref name="dependent"/> wait for <paramref name="prerequisite"/> create a loop? Works for steps and goals alike.</summary>
    public bool WouldCreateCycle(Guid dependent, Guid prerequisite) =>
        dependent == prerequisite || Requires(prerequisite, dependent);

    /// <summary>A goal with no finish line never completes, so nothing can wait on it.</summary>
    public bool CanWaitOnGoal(Guid prerequisiteGoal) =>
        _goals.TryGetValue(prerequisiteGoal, out var g) && g.HasFinishLine && g.Rhythm.Kind == RhythmKind.None;

    /// <summary>Returns the ids on a cycle if the stored data already contains one, otherwise empty.</summary>
    public IReadOnlyList<Guid> FindCycle()
    {
        var state = new Dictionary<Guid, int>(); // 1 = on the current path, 2 = finished
        var path = new List<Guid>();

        IReadOnlyList<Guid>? Visit(Guid node)
        {
            state[node] = 1;
            path.Add(node);
            foreach (var next in Prerequisites(node))
            {
                if (state.GetValueOrDefault(next) == 1)
                    return path.Skip(path.IndexOf(next)).ToList();
                if (!state.ContainsKey(next) && Visit(next) is { } found)
                    return found;
            }
            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            return null;
        }

        foreach (var node in _goals.Keys.Concat(_steps.Keys))
            if (!state.ContainsKey(node) && Visit(node) is { } cycle)
                return cycle;
        return [];
    }

    /// <summary>Why a step can't be started today, or null when it can.</summary>
    public string? BlockedReason(Step step, DateOnly today)
    {
        _goalOfStep.TryGetValue(step.Id, out var goal);
        for (var s = step; s is not null; s = goal?.ParentOf(s))
        {
            if (s.BlockedUntil is { } until && until > today)
                return $"Waiting until {until:MMM d}";
            foreach (var id in s.DependsOn)
                if (_steps.TryGetValue(id, out var pre) && !_goalOfStep[id].IsDone(pre))
                    return $"Needs “{pre.Title}” first";
        }

        return goal is null ? null : BlockedReason(goal, today);
    }

    /// <summary>Why a goal can't be started today, or null when it can.</summary>
    public string? BlockedReason(Goal goal, DateOnly today)
    {
        foreach (var id in goal.DependsOnGoals)
            if (_goals.TryGetValue(id, out var pre) && !_isGoalComplete(pre))
                return $"Needs “{pre.Title}” finished first";
        return null;
    }

    /// <summary>How many unfinished steps are waiting, directly or through others, on this one.</summary>
    /// <summary>The open steps, in any goal, that cannot start until this one is finished.</summary>
    public IEnumerable<Step> Dependents(Guid stepId)
    {
        foreach (var other in _steps.Values)
            if (!other.Done && other.Id != stepId && !_goalOfStep[other.Id].IsSection(other) && Requires(other.Id, stepId))
                yield return other;
    }

    public int UnlockCount(Guid stepId)
    {
        var count = 0;
        foreach (var other in _steps.Values)
            if (!other.Done && other.Id != stepId && !_goalOfStep[other.Id].IsSection(other) && Requires(other.Id, stepId))
                count++;
        return count;
    }

    /// <summary>Makes each step wait for the one before it: the "do these in order" shortcut.</summary>
    public static void Chain(IReadOnlyList<Step> inOrder)
    {
        for (var i = 1; i < inOrder.Count; i++)
            if (!inOrder[i].DependsOn.Contains(inOrder[i - 1].Id))
                inOrder[i].DependsOn.Add(inOrder[i - 1].Id);
    }

    /// <summary>Makes each goal wait for the one before it. Refuses goals that can never finish.</summary>
    public static void Chain(IReadOnlyList<Goal> inOrder)
    {
        for (var i = 1; i < inOrder.Count; i++)
        {
            var prior = inOrder[i - 1];
            if (!prior.HasFinishLine || prior.Rhythm.Kind != RhythmKind.None)
                throw new InvalidOperationException($"“{prior.Title}” never finishes, so nothing can wait on it.");
            if (!inOrder[i].DependsOnGoals.Contains(prior.Id))
                inOrder[i].DependsOnGoals.Add(prior.Id);
        }
    }
}
