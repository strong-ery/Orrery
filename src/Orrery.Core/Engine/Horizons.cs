using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>How soon a goal is going to want you. This is what the rings of the orbit map mean.</summary>
public enum Horizon { Now, Week, Month, Later }

public static class Horizons
{
    public static string Label(Horizon h) => h switch
    {
        Horizon.Now => "Now",
        Horizon.Week => "This week",
        Horizon.Month => "This month",
        _ => "Later",
    };

    public static Horizon Of(Goal g, IReadOnlyList<Contribution> log, DateOnly today)
    {
        var demand = Demands.For(g, log, today);
        var pressure = Planner.Pressure(g, log, today, demand);
        if (pressure >= Planner.MustDo)
            return Horizon.Now;

        switch (g.Rhythm.Kind)
        {
            case RhythmKind.Repeat:
                if (demand.IsDemanding && !demand.IsMet)
                    return Horizon.Now;
                var every = Math.Max(1, g.Rhythm.EveryDays);
                var offset = Math.Max(0, today.DayNumber - g.Created.DayNumber);
                var next = g.Created.AddDays((offset / every + 1) * every);
                return next.DayNumber - today.DayNumber <= 7 ? Horizon.Week : Horizon.Month;

            case RhythmKind.Cadence:
                return demand.IsDemanding && !demand.IsMet ? Horizon.Now : Horizon.Week;
        }

        if (g.Deadline is { } due)
        {
            var days = due.DayNumber - today.DayNumber;
            return days <= 1 ? Horizon.Now : days <= 7 ? Horizon.Week : days <= 30 ? Horizon.Month : Horizon.Later;
        }
        return Horizon.Later;
    }
}
