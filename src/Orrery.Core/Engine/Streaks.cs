using Orrery.Core.Models;

namespace Orrery.Core.Engine;

public enum DayStatus
{
    /// <summary>Nothing was asked, so the streak neither grows nor breaks.</summary>
    Rest,
    /// <summary>Everything that was asked got done.</summary>
    Met,
    /// <summary>Something that was asked didn't get done.</summary>
    Missed,
}

public sealed record StreakResult(int Current, int Best, bool TodayMet, bool GraceAvailable, DateOnly? LastGraceUsed);

/// <summary>
/// Streaks are kept per category. A day counts when everything the category's goals asked of you that day was
/// done; days that asked for nothing are skipped. One miss in any seven days is forgiven.
/// </summary>
public static class Streaks
{
    public const int GraceWindowDays = 7;
    private const int MaxLookbackDays = 400;

    public static DayStatus StatusOn(IEnumerable<Goal> goalsInCategory, IReadOnlyList<Contribution> log, DateOnly day)
    {
        var anyDemand = false;
        var allMet = true;
        foreach (var goal in goalsInCategory)
        {
            var demand = Demands.For(goal, log, day);
            if (!demand.IsDemanding)
                continue;
            anyDemand = true;
            if (demand.IsMet)
                continue;
            allMet = false;
            if (demand.Enforced)
                return DayStatus.Missed;
        }
        return anyDemand && allMet ? DayStatus.Met : DayStatus.Rest;
    }

    public static StreakResult For(Category category, OrreryData data, DateOnly today)
    {
        var goals = data.Goals.Where(g => g.CategoryId == category.Id && !g.Archived).ToList();
        if (goals.Count == 0)
            return new StreakResult(0, 0, false, true, null);

        var start = goals.Min(g => g.Created);
        var earliest = today.AddDays(-MaxLookbackDays);
        if (start < earliest)
            start = earliest;

        var run = 0;
        var best = 0;
        DateOnly? lastGrace = null;

        for (var day = start; day < today; day = day.AddDays(1))
        {
            switch (StatusOn(goals, data.Contributions, day))
            {
                case DayStatus.Met:
                    run++;
                    best = Math.Max(best, run);
                    break;
                case DayStatus.Missed:
                    if (lastGrace is null || day.DayNumber - lastGrace.Value.DayNumber >= GraceWindowDays)
                        lastGrace = day; // forgiven, the run carries on
                    else
                    {
                        run = 0;
                        lastGrace = null;
                    }
                    break;
            }
        }

        var todayMet = StatusOn(goals, data.Contributions, today) == DayStatus.Met;
        var current = todayMet ? run + 1 : run;
        best = Math.Max(best, current);
        var graceAvailable = lastGrace is null || today.DayNumber - lastGrace.Value.DayNumber >= GraceWindowDays;
        return new StreakResult(current, best, todayMet, graceAvailable, lastGrace);
    }
}
