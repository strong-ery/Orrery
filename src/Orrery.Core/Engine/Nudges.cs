using Orrery.Core.Models;

namespace Orrery.Core.Engine;

public sealed record Nudge(Guid GoalId, Guid? StepId, string Title, string Message, bool Urgent);

/// <summary>
/// Decides what is worth interrupting you for. The rules are deliberately stingy: nothing in the morning, only
/// what is genuinely due (or about to slip) before the evening, a cool-down per goal, and a cap per day. A nudge
/// you learn to ignore is worse than none.
/// </summary>
public static class Nudges
{
    public static IReadOnlyList<Nudge> Due(OrreryData data, DateTime now)
    {
        var settings = data.Settings;
        var maxPerDay = Math.Max(1, settings.NudgeMaxPerDay);
        var hour = now.Hour + now.Minute / 60.0;
        if (!settings.Nudges || hour < settings.NudgeFromHour || hour >= settings.NudgeUntilHour)
            return [];

        var today = DateOnly.FromDateTime(now);
        if (settings.NudgeLog.Values.Count(t => DateOnly.FromDateTime(t) == today) >= maxPerDay)
            return [];

        var plan = Planner.Suggest(data, new PlanRequest(today, 10, settings.Bias, settings.LowEnergy));
        var result = new List<Nudge>();
        foreach (var item in plan)
        {
            var behind = item.Demand is { IsDemanding: true, IsMet: false };
            var urgent = item.Pressure >= Planner.MustDo || (behind && item.Demand.Enforced);
            if (!urgent && !(behind && hour >= settings.NudgeGentleFromHour))
                continue;
            var role = item.Category?.Role;
            if (urgent ? !settings.NudgeDeadlines : role == CategoryRole.Passion ? !settings.NudgePassions : !settings.NudgeEvening)
                continue;

            var cooldown = TimeSpan.FromHours(urgent ? settings.NudgeRepeatUrgentHours : role == CategoryRole.Passion ? settings.NudgeRepeatPassionHours : settings.NudgeRepeatGentleHours);
            if (settings.NudgeLog.TryGetValue(item.GoalId, out var last) && now - last < cooldown)
                continue;

            var what = item.StepId is null ? item.Title : $"{item.Title} ({item.GoalTitle})";
            var message = (urgent, role) switch
            {
                (true, _) => $"{what}: {item.Reason}.",
                (false, CategoryRole.Passion) => $"A little {item.GoalTitle} today? {Capitalise(item.Reason)}.",
                _ => $"{what} is still on today's list. {Capitalise(item.Reason)}.",
            };
            result.Add(new Nudge(item.GoalId, item.StepId, item.Title, message, urgent));
        }
        return result;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public static class NudgeLogExtensions
{
    /// <summary>Remembers that a goal was just nudged about, and forgets entries from earlier days.</summary>
    public static void NudgeLogFor(this OrreryData data, Guid goalId, DateTime at)
    {
        var log = data.Settings.NudgeLog;
        foreach (var old in log.Where(kv => kv.Value.Date < at.Date.AddDays(-1)).Select(kv => kv.Key).ToList())
            log.Remove(old);
        log[goalId] = at;
    }
}
