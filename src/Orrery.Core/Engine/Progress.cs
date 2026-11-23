using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>Where today's demand on a goal comes from.</summary>
public enum DemandSource { None, Repeat, Cadence, Deadline }

/// <summary>
/// What a goal asks of you on one day, in the goal's own unit. Everything downstream (the planner, the
/// nudges, the streaks) reads this and never looks at what kind of goal it came from.
/// </summary>
/// <param name="Target">How much is wanted today. Zero means nothing is asked.</param>
/// <param name="DoneToday">How much of it is already done.</param>
/// <param name="Enforced">Whether missing it counts against a streak. A weekly chore is wanted all week but only enforced on its last day.</param>
public readonly record struct Demand(double Target, double DoneToday, DemandSource Source, string Unit, bool Enforced)
{
    private const double Eps = 1e-9;

    public static Demand None { get; } = new(0, 0, DemandSource.None, "", false);

    public bool IsDemanding => Target > Eps;
    public bool IsMet => DoneToday >= Target - Eps;
    public double Remaining => Math.Max(0, Target - DoneToday);
}

/// <summary>Counting progress from the contribution log. Pure functions: pass in the day you are asking about.</summary>
public static class Progress
{
    public static double Target(Goal g) => g.Measure.Kind switch
    {
        MeasureKind.Done => 1,
        MeasureKind.Steps => g.Steps.Count,
        _ => g.Measure.Target,
    };

    public static bool IsCountable(Goal g) => g.Measure.Kind is MeasureKind.Done or MeasureKind.Steps;

    public static double Between(Goal g, IEnumerable<Contribution> log, DateOnly from, DateOnly to)
    {
        double sum = 0;
        foreach (var c in log)
            if (c.GoalId == g.Id && c.Day >= from && c.Day <= to)
                sum += c.Amount;
        return sum;
    }

    public static double Through(Goal g, IEnumerable<Contribution> log, DateOnly day) =>
        Between(g, log, DateOnly.MinValue, day);

    public static double OnDay(Goal g, IEnumerable<Contribution> log, DateOnly day) => Between(g, log, day, day);

    /// <summary>Total progress right now. Steps count what is ticked, so editing the list stays honest.</summary>
    public static double Current(Goal g, IEnumerable<Contribution> log) =>
        g.Measure.Kind == MeasureKind.Steps
            ? g.Steps.Count(s => s.Done)
            : Through(g, log, DateOnly.MaxValue);

    public static bool IsComplete(Goal g, IEnumerable<Contribution> log)
    {
        if (!g.HasFinishLine || g.Rhythm.Kind != RhythmKind.None)
            return false;
        var target = Target(g);
        return target > 0 && Current(g, log) >= target - 1e-9;
    }

    /// <summary>0..1 for goals with a finish line, otherwise null (an open-ended goal has no fraction).</summary>
    public static double? Fraction(Goal g, IEnumerable<Contribution> log)
    {
        if (!g.HasFinishLine || g.Rhythm.Kind != RhythmKind.None)
            return null;
        var target = Target(g);
        return target <= 0 ? 0 : Math.Clamp(Current(g, log) / target, 0, 1);
    }

    public static DateOnly? LastWorked(Goal g, IEnumerable<Contribution> log)
    {
        DateOnly? last = null;
        foreach (var c in log)
            if (c.GoalId == g.Id && (last is null || c.Day > last))
                last = c.Day;
        return last;
    }

    /// <summary>Average per day over the trailing window ending yesterday. Zero when nothing was logged.</summary>
    public static double RecentDailyAverage(Goal g, IEnumerable<Contribution> log, DateOnly today, int days = 14) =>
        Between(g, log, today.AddDays(-days), today.AddDays(-1)) / days;
}

/// <summary>Turns a goal's measure, rhythm and deadline into a demand for a given day.</summary>
public static class Demands
{
    /// <summary>You can't sensibly do more than this many times the average in a single day.</summary>
    public const double MaxDailyFactor = 2.0;

    public static Demand For(Goal g, IReadOnlyList<Contribution> log, DateOnly day)
    {
        if (g.Archived || day < g.Created)
            return Demand.None;

        var unit = g.Measure.UnitLabel;
        switch (g.Rhythm.Kind)
        {
            case RhythmKind.Repeat:
                return ForRepeat(g, log, day, unit);
            case RhythmKind.Cadence:
                return ForCadence(g, log, day, unit);
            default:
                return g.HasFinishLine && g.Deadline is { } deadline ? ForDeadline(g, log, day, deadline, unit) : Demand.None;
        }
    }

    private static Demand ForRepeat(Goal g, IReadOnlyList<Contribution> log, DateOnly day, string unit)
    {
        var every = Math.Max(1, g.Rhythm.EveryDays);
        var offset = day.DayNumber - g.Created.DayNumber;
        var windowStart = g.Created.AddDays(offset - offset % every);
        var windowEnd = windowStart.AddDays(every - 1);

        var target = g.Measure.Kind is MeasureKind.Amount or MeasureKind.Time && g.Measure.Target > 0 ? g.Measure.Target : 1;
        // Already done earlier in this window: nothing more is asked until the next one opens.
        if (day > windowStart && Progress.Between(g, log, windowStart, day.AddDays(-1)) >= target - 1e-9)
            return Demand.None;

        var done = Progress.Between(g, log, windowStart, day);
        var met = done >= target - 1e-9;
        return new Demand(target, done, DemandSource.Repeat, unit, Enforced: met || day == windowEnd);
    }

    private static Demand ForCadence(Goal g, IReadOnlyList<Contribution> log, DateOnly day, string unit)
    {
        var perWeek = g.Rhythm.PerWeek;
        if (perWeek <= 0)
            return Demand.None;

        var index = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        var daysLeft = 7 - index;
        var weekStart = day.AddDays(-index);
        var remaining = Math.Max(0, perWeek - Progress.Between(g, log, weekStart, day.AddDays(-1)));
        var maxDaily = Progress.IsCountable(g) ? 1.0 : perWeek / 7 * MaxDailyFactor;
        var forced = Math.Max(0, remaining - (daysLeft - 1) * maxDaily);
        return new Demand(forced, Progress.OnDay(g, log, day), DemandSource.Cadence, unit, Enforced: true);
    }

    private static Demand ForDeadline(Goal g, IReadOnlyList<Contribution> log, DateOnly day, DateOnly deadline, string unit)
    {
        var target = Progress.Target(g);
        if (target <= 0)
            return Demand.None;

        // Finishing a step logs a contribution of 1, so the log is the history for every measure.
        var before = Progress.Through(g, log, day.AddDays(-1));
        var remaining = target - before;
        if (remaining <= 1e-9)
            return Demand.None;

        var daysLeft = Math.Max(1, deadline.DayNumber - day.DayNumber + 1);
        var perDay = remaining / daysLeft;
        var done = Progress.OnDay(g, log, day);
        // Done and Steps can't be done in fractions: nothing is enforced until a whole unit is due today.
        var enforced = !Progress.IsCountable(g) || perDay >= 1 - 1e-9;
        return new Demand(perDay, done, DemandSource.Deadline, unit, enforced);
    }
}
