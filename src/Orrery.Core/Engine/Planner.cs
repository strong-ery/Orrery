using Orrery.Core.Models;

namespace Orrery.Core.Engine;

public sealed record PlanRequest(DateOnly Today, int MaxItems = 5, double Bias = 0, bool LowEnergy = false);

/// <summary>One thing suggested for today: a step, or a goal's own entry when it has no steps.</summary>
public sealed record PlanItem(
    Guid GoalId,
    Guid? StepId,
    string Title,
    string GoalTitle,
    Category? Category,
    Energy Energy,
    int? EstimateMinutes,
    double Pressure,
    double Score,
    string Reason,
    Demand Demand,
    string DemandNote = "",
    int Unlocks = 0,
    int DeferCount = 0);

/// <summary>
/// Picks today's list. It knows nothing about what a goal is: it reads each goal's pressure and demand, the
/// dependency graph, and the two dials (bias and low-energy) and nothing else.
/// </summary>
public static class Planner
{
    /// <summary>Anything this pressing goes on the list whatever the bias says.</summary>
    public const double MustDo = 0.85;

    public static IReadOnlyList<PlanItem> Suggest(OrreryData data, PlanRequest request)
    {
        var today = request.Today;
        var log = data.Contributions;
        var graph = new DependencyGraph(data.Goals, g => Progress.IsComplete(g, log));

        var candidates = new List<PlanItem>();
        foreach (var goal in data.Goals)
        {
            if (goal.Archived || Progress.IsComplete(goal, log))
                continue;

            if (goal.SnoozedUntil is { } snoozed && snoozed > today && !goal.Leaves().Any())
                continue; // put off until another day

            var stepless = !goal.Leaves().Any();
            if (stepless && goal.PlannedFor is { } later && later > today && Pressure(goal, log, today, Demands.For(goal, log, today)) < MustDo)
                continue; // you planned it for another day

            var demand = Demands.For(goal, log, today);
            // Today's share of a repeating or weekly goal being done means it is done. A goal working toward a deadline
            // stays on the list with the rest of its steps, quietly, so the bigger picture is never hidden.
            var met = demand.IsDemanding && demand.IsMet;
            if (met && demand.Source != DemandSource.Deadline)
                continue;
            PlanItem Soft(PlanItem i) => met
                ? i with { Pressure = Math.Min(i.Pressure, 0.45), Score = Math.Min(i.Score, 3.5), Reason = "today's share is done · more if you want" }
                : i;

            if (goal.Rhythm.Kind == RhythmKind.Repeat && !demand.IsDemanding)
                continue; // between two due dates, a repeating goal stays out of the way

            var pressure = Pressure(goal, log, today, demand);
            var category = data.CategoryOf(goal);

            // A goal with unfinished steps offers its next available step; otherwise it offers itself.
            bool Open(Step s) => !s.Done && graph.BlockedReason(s, today) is null && (!request.LowEnergy || s.Energy == Energy.Low);
            if (goal.Leaves().Any(s => !s.Done))
            {
                // Steps you planned for today (or earlier, and never got to) come first, all of them.
                var planned = goal.Leaves().Where(s => Open(s) && s.PlannedFor is { } d && d <= today).ToList();
                if (planned.Count > 0)
                {
                    foreach (var step in planned)
                        candidates.Add(Soft(AsPlanned(Make(goal, step, category, pressure, graph.UnlockCount(step.Id), demand, today), step.PlannedFor!.Value, today)));
                    continue;
                }
                // Otherwise the next step, skipping any you planned for a later day unless the goal is truly pressing.
                var next = goal.Leaves().FirstOrDefault(s =>
                    Open(s) && (s.PlannedFor is null || pressure >= MustDo));
                if (next is null)
                    continue;
                candidates.Add(Soft(Make(goal, next, category, pressure, graph.UnlockCount(next.Id), demand, today)));
            }
            else
            {
                if (graph.BlockedReason(goal, today) is not null)
                    continue;
                var item = Make(goal, null, category, pressure, 0, demand, today);
                candidates.Add(Soft(goal.PlannedFor is { } day && day <= today ? AsPlanned(item, day, today) : item));
            }
        }

        if (request.LowEnergy)
            candidates.RemoveAll(c => c.Energy != Energy.Low);

        var bias = Math.Clamp(request.Bias, -1, 1);
        candidates = candidates
            .Select(c => c with { Score = c.Score * RoleWeight(c.Category?.Role, bias) })
            .OrderByDescending(c => c.Score)
            .ToList();

        var picked = candidates
            .Where(c => c.Pressure >= MustDo)
            .Take(request.MaxItems)
            .ToList();
        foreach (var c in candidates)
        {
            if (picked.Count >= request.MaxItems)
                break;
            if (!picked.Contains(c))
                picked.Add(c);
        }

        // A passion always gets a seat unless the bias says otherwise, so the list never turns into pure obligation.
        var hasPassion = picked.Any(c => c.Category?.Role == CategoryRole.Passion);
        if (!hasPassion && bias > -0.75 && request.MaxItems >= 3)
        {
            var passion = candidates.FirstOrDefault(c => c.Category?.Role == CategoryRole.Passion);
            if (passion is not null)
            {
                var drop = picked.LastOrDefault(c => c.Pressure < MustDo);
                if (drop is not null)
                    picked.Remove(drop);
                if (picked.Count < request.MaxItems)
                    picked.Add(passion with { Reason = passion.Reason + " · your protected passion slot" });
            }
        }

        return picked.OrderByDescending(c => c.Score).ToList();
    }

    /// <summary>A plan you made yourself outranks the planner's own guess: it goes on the list and says why.</summary>
    private static PlanItem AsPlanned(PlanItem item, DateOnly day, DateOnly today)
    {
        var note = day == today ? "planned for today" : $"planned for {day:MMM d}";
        return item with
        {
            Pressure = Math.Max(item.Pressure, MustDo),
            Score = Math.Max(item.Score, MustDo * 10 + 1),
            Reason = $"{note} · {item.Reason}",
        };
    }

    private static double RoleWeight(CategoryRole? role, double bias) => role switch
    {
        CategoryRole.Obligation => 1 - 0.5 * bias,
        CategoryRole.Passion => 1 + 0.5 * bias,
        _ => 1,
    };

    private static PlanItem Make(Goal goal, Step? step, Category? category, double pressure, int unlocks, Demand demand, DateOnly today)
    {
        var reasons = new List<string>();
        var demandNote = demand.IsDemanding ? DescribeDemand(goal, demand) : "";
        if (demand.IsDemanding)
            reasons.Add(demandNote);
        if (unlocks > 0)
            reasons.Add(unlocks == 1 ? "1 step is waiting on this" : $"{unlocks} steps are waiting on this");
        if (step is { DeferCount: >= 3 })
            reasons.Add("put off a few times, so it's worth making smaller");
        if (reasons.Count == 0)
            reasons.Add(pressure > 0.3 ? "it's been a while" : "whenever you're ready");

        return new PlanItem(
            goal.Id,
            step?.Id,
            step?.Title ?? goal.Title,
            step is not null && goal.ParentOf(step) is { } section ? $"{goal.Title} › {section.Title}" : goal.Title,
            category,
            step?.Energy ?? goal.DefaultEnergy,
            step?.EstimateMinutes,
            pressure,
            pressure * 10 + Math.Min(unlocks, 5) * 0.3,
            string.Join(" · ", reasons),
            demand,
            demandNote,
            unlocks,
            step?.DeferCount ?? 0);
    }

    /// <summary>"Needs 2.1 % today", "Due today" and so on, in the goal's own words.</summary>
    public static string DescribeDemand(Goal goal, Demand d)
    {
        var amount = d.Unit.Length == 0 ? "" : $"{Format(d.Remaining)} {d.Unit}";
        return d.Source switch
        {
            DemandSource.Repeat when d.Unit.Length == 0 => "due today",
            DemandSource.Repeat => $"{amount} due today",
            DemandSource.Cadence => $"{amount} to stay on this week's rhythm".Trim(),
            DemandSource.Deadline when goal.Deadline is { } due && Progress.IsCountable(goal) => $"finish by {due:MMM d}",
            DemandSource.Deadline when goal.Deadline is { } due => $"{amount} today to finish by {due:MMM d}",
            _ => "",
        };
    }

    public static string Format(double value) => value >= 10 ? $"{Math.Round(value):0}" : $"{value:0.#}";

    /// <summary>How hard a goal is pulling on you today, 0 to 1. Fully met demands pull nothing.</summary>
    public static double Pressure(Goal goal, IReadOnlyList<Contribution> log, DateOnly today, Demand demand)
    {
        var staleness = Staleness(goal, log, today);

        if (!demand.IsDemanding)
            return Math.Max(staleness, 0.1);

        double pressure;
        if (demand.Source == DemandSource.Repeat)
            pressure = demand.Enforced ? 0.8 : 0.45;
        else if (demand.Source == DemandSource.Cadence)
            pressure = demand.Enforced ? 0.9 : 0.5;
        else if (goal.Deadline is { } deadline)
        {
            var daysLeft = deadline.DayNumber - today.DayNumber;
            pressure = daysLeft < 0
                ? 1
                : 0.25 + 0.75 * (1 - Math.Clamp(daysLeft / 30.0, 0, 1));

            // Falling behind what you actually manage makes it worse than the calendar alone says.
            var typical = Progress.RecentDailyAverage(goal, log, today);
            if (typical > 0)
                pressure += Math.Clamp(demand.Target / typical - 1, 0, 1) * 0.3;
        }
        else
            pressure = 0.4;

        return Math.Clamp(Math.Max(pressure, staleness), 0, 1);
    }

    /// <summary>How long a goal has been left alone, as a small pull that never reaches "must do".</summary>
    public static double Staleness(Goal goal, IReadOnlyList<Contribution> log, DateOnly today)
    {
        var last = Progress.LastWorked(goal, log) ?? goal.Created;
        var idle = Math.Max(0, today.DayNumber - last.DayNumber);
        return 0.15 + 0.4 * Math.Clamp(idle / 14.0, 0, 1);
    }
}
