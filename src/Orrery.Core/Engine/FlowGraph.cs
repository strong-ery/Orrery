using Orrery.Core.Models;

namespace Orrery.Core.Engine;

public enum NodeState { Done, Open, Blocked }

/// <summary>One box on the dependency graph: a section or loose step, or a whole goal that has no steps.</summary>
public sealed record FlowNode(Guid Id, Guid GoalId, Guid? StepId, string Title, string GoalTitle, NodeState State, int Column, int Row,
    double? Fraction = null, string Detail = "", int Holds = 0);

public sealed record FlowEdge(Guid From, Guid To);

public sealed record FlowLayout(IReadOnlyList<FlowNode> Nodes, IReadOnlyList<FlowEdge> Edges)
{
    public int Columns => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Column) + 1;
    public int Rows => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Row) + 1;
}

/// <summary>
/// Turns every "waits for" in the data into a left-to-right graph: what has to finish sits to the left of what
/// waits on it. Only things that take part in a dependency are drawn, so it stays a picture of the waiting.
/// Steps inside a section are folded into the section's box, since the section is what other things wait on.
/// </summary>
public static class FlowGraph
{
    public static FlowLayout Build(OrreryData data, DateOnly today, Func<Goal, bool> isComplete)
    {
        var graph = new DependencyGraph(data.Goals, isComplete);
        var goals = data.Goals.Where(g => !g.Archived).ToList();

        var raw = new List<(Guid Id, Goal Goal, Step? Step)>();
        var owner = new Dictionary<Guid, Guid>(); // any step id -> the node that contains it
        foreach (var goal in goals)
        {
            var roots = goal.Roots().ToList();
            if (roots.Count == 0)
            {
                raw.Add((goal.Id, goal, null));
                continue;
            }
            foreach (var root in roots)
            {
                raw.Add((root.Id, goal, root));
                owner[root.Id] = root.Id;
                foreach (var step in goal.Steps.Where(s => s.Id != root.Id && IsUnder(goal, s, root)))
                    owner[step.Id] = root.Id;
            }
        }

        var edges = new HashSet<(Guid, Guid)>();
        foreach (var goal in goals)
            foreach (var step in goal.Steps)
                foreach (var dep in step.DependsOn)
                    if (owner.TryGetValue(dep, out var from) && owner.TryGetValue(step.Id, out var to) && from != to)
                        edges.Add((from, to));

        // "Goal B waits for goal A": everything at the end of A points at everything at the start of B.
        var nodesOf = raw.GroupBy(n => n.Goal.Id).ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
        foreach (var goal in goals)
            foreach (var depId in goal.DependsOnGoals)
            {
                if (!nodesOf.TryGetValue(depId, out var upstream) || !nodesOf.TryGetValue(goal.Id, out var downstream))
                    continue;
                var sinks = upstream.Where(u => !edges.Any(e => e.Item1 == u && upstream.Contains(e.Item2))).ToList();
                var sources = downstream.Where(d => !edges.Any(e => e.Item2 == d && downstream.Contains(e.Item1))).ToList();
                foreach (var a in sinks)
                    foreach (var b in sources)
                        edges.Add((a, b));
            }

        var connected = edges.SelectMany(e => new[] { e.Item1, e.Item2 }).ToHashSet();
        var nodes = raw.Where(n => connected.Contains(n.Id)).ToList();

        // Column = the longest chain of things that must come first.
        var column = new Dictionary<Guid, int>();
        int ColumnOf(Guid id, HashSet<Guid> path)
        {
            if (column.TryGetValue(id, out var known))
                return known;
            if (!path.Add(id))
                return 0; // a cycle: the dependency checks refuse these, but never hang on bad data
            var depth = 0;
            foreach (var e in edges.Where(e => e.Item2 == id))
                depth = Math.Max(depth, ColumnOf(e.Item1, path) + 1);
            path.Remove(id);
            return column[id] = depth;
        }
        foreach (var n in nodes)
            ColumnOf(n.Id, []);

        // Rows: keep each column in goal order, nudged toward the average row of what it waits on to cut crossings.
        var row = new Dictionary<Guid, double>();
        var result = new List<FlowNode>();
        var order = nodes.Select((n, i) => (n, i)).ToDictionary(x => x.n.Id, x => x.i);
        foreach (var col in nodes.Select(n => column[n.Id]).Distinct().OrderBy(c => c))
        {
            double Wanted(Guid id, int fallback) =>
                edges.Where(e => e.Item2 == id && row.ContainsKey(e.Item1)).Select(e => row[e.Item1]).DefaultIfEmpty(fallback).Average();
            var inColumn = nodes.Where(n => column[n.Id] == col)
                .OrderBy(n => Wanted(n.Id, order[n.Id]))
                .ThenBy(n => order[n.Id])
                .ToList();
            var next = 0;
            for (var i = 0; i < inColumn.Count; i++)
            {
                var n = inColumn[i];
                // Sit level with what it waits on when there is room, so a chain reads as a straight line.
                var at = Math.Max(next, (int)Math.Round(Wanted(n.Id, next)));
                next = at + 1;
                row[n.Id] = at;
                var done = n.Step is null ? isComplete(n.Goal) : n.Goal.IsDone(n.Step);
                var blocked = !done && (n.Step is null ? graph.BlockedReason(n.Goal, today) : graph.BlockedReason(n.Step, today)) is not null;

                // Where this box stands: steps done in a section, how far a goal has got, or what it is waiting for.
                double? fraction = null;
                var detail = "";
                if (n.Step is { } root && n.Goal.IsSection(root))
                {
                    var inside = n.Goal.Steps.Where(x => x.Id != root.Id && IsUnder(n.Goal, x, root) && !n.Goal.IsSection(x)).ToList();
                    if (inside.Count > 0)
                    {
                        var finished = inside.Count(x => x.Done);
                        fraction = (double)finished / inside.Count;
                        detail = $"{finished} of {inside.Count} steps";
                    }
                }
                else if (n.Step is null)
                {
                    fraction = Progress.Fraction(n.Goal, data.Contributions);
                    if (fraction is { } f)
                        detail = $"{Math.Round(f * 100)}%";
                }
                if (blocked)
                    detail = (n.Step is null ? graph.BlockedReason(n.Goal, today) : graph.BlockedReason(n.Step, today)) ?? detail;
                var holds = edges.Count(e => e.Item1 == n.Id);
                result.Add(new FlowNode(n.Id, n.Goal.Id, n.Step?.Id, n.Step?.Title ?? n.Goal.Title, n.Goal.Title,
                    done ? NodeState.Done : blocked ? NodeState.Blocked : NodeState.Open, col, at, done ? 1 : fraction, detail, holds));
            }
        }

        return new FlowLayout(result, edges.Select(e => new FlowEdge(e.Item1, e.Item2)).ToList());
    }

    private static bool IsUnder(Goal goal, Step step, Step root)
    {
        for (var p = goal.ParentOf(step); p is not null; p = goal.ParentOf(p))
            if (p.Id == root.Id)
                return true;
        return false;
    }
}
