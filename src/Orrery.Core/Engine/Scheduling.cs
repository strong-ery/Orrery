using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>One thing that can be put on a day: a step, or a goal that has no steps.</summary>
public sealed record Schedulable(Guid GoalId, Guid? StepId, string Title, string GoalTitle, int Minutes, DateOnly? Day);

/// <summary>
/// Planning on the calendar. A planned day is a promise to yourself, not a lock: Today's list leans on it,
/// and the load of a day is the minutes you have put on it compared with what a full day usually is.
/// </summary>
public static class Scheduling
{
    public const int DefaultMinutes = 30;

    public static int MinutesOf(Goal goal, Step? step) =>
        (step is null ? null : goal.EstimateOf(step)) ?? DefaultMinutes;

    /// <summary>Everything you could plan: the next open step of each live goal, and goals without steps. Already-planned items are included with their day.</summary>
    public static IReadOnlyList<Schedulable> Candidates(OrreryData data, DateOnly today)
    {
        var list = new List<Schedulable>();
        var graph = new DependencyGraph(data.Goals, g => Progress.IsComplete(g, data.Contributions));
        foreach (var goal in data.Goals.Where(g => !g.Archived))
        {
            if (Progress.IsComplete(goal, data.Contributions))
                continue;
            if (goal.Leaves().Any())
            {
                foreach (var step in goal.Leaves().Where(s => !s.Done))
                {
                    // Planned steps always show (so they can be moved); otherwise only what is actually next.
                    var isNext = graph.BlockedReason(step, today) is null && goal.Leaves().First(s => !s.Done && graph.BlockedReason(s, today) is null) == step;
                    if (step.PlannedFor is not null || isNext)
                        list.Add(new Schedulable(goal.Id, step.Id, step.Title, goal.Title, MinutesOf(goal, step), step.PlannedFor));
                }
            }
            else
            {
                list.Add(new Schedulable(goal.Id, null, goal.Title, goal.Title, MinutesOf(goal, null), goal.PlannedFor));
            }
        }
        return list;
    }

    /// <summary>Puts an item on a day, or takes it off when day is null.</summary>
    public static void Plan(OrreryData data, Guid goalId, Guid? stepId, DateOnly? day)
    {
        if (data.Goals.FirstOrDefault(g => g.Id == goalId) is not { } goal)
            return;
        if (stepId is { } id)
        {
            if (goal.Steps.FirstOrDefault(s => s.Id == id) is { } step)
                step.PlannedFor = day;
        }
        else
        {
            goal.PlannedFor = day;
        }
    }

    /// <summary>Minutes planned for a day, counting only things not yet done.</summary>
    public static int Load(OrreryData data, DateOnly day)
    {
        var total = 0;
        foreach (var goal in data.Goals.Where(g => !g.Archived))
        {
            if (goal.Leaves().Any())
                total += goal.Leaves().Where(s => !s.Done && s.PlannedFor == day).Sum(s => MinutesOf(goal, s));
            else if (goal.PlannedFor == day && !Progress.IsComplete(goal, data.Contributions))
                total += MinutesOf(goal, null);
        }
        return total;
    }

    /// <summary>How full a day is: 0 empty, 1 exactly a full day, above 1 more than you usually do.</summary>
    public static double Fullness(OrreryData data, DateOnly day) =>
        data.Settings.DayCapacityMinutes <= 0 ? 0 : (double)Load(data, day) / data.Settings.DayCapacityMinutes;

    /// <summary>Planned days that have passed with the work not done: they roll onto today.</summary>
    public static int Overdue(OrreryData data, DateOnly today) =>
        data.Goals.Where(g => !g.Archived).Sum(g => g.Leaves().Count(s => !s.Done && s.PlannedFor < today));
}
