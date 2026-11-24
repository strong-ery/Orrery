using Orrery.Core.Models;

namespace Orrery.Core.Engine;

/// <summary>Which day the week starts on. One setting, read by the calendar and the weekly review.</summary>
public static class WeekStart
{
    public static DayOfWeek First { get; set; } = DayOfWeek.Monday;

    public static DateOnly Of(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek - (int)First + 7) % 7));
}

/// <summary>One line of the review: what happened, and a little more about it.</summary>
public sealed record ReviewNote(string Text, string Detail = "", Guid? GoalId = null, DateOnly? Day = null);

/// <summary>Looking back at a finished week, and ahead at the one that has just started.</summary>
public sealed record WeekReview(
    DateOnly From,
    DateOnly To,
    int DaysShowedUp,
    int ThingsDone,
    double Minutes,
    int GoalsMoved,
    IReadOnlyList<ReviewNote> WentWell,
    IReadOnlyList<ReviewNote> Slipped,
    IReadOnlyList<ReviewNote> Ahead,
    DateOnly NextFrom,
    DateOnly NextTo)
{
    public bool WasQuiet => ThingsDone == 0;
}

public static class WeeklyReview
{
    private const int MostShown = 6;

    /// <summary>True when this week has not been looked back on yet and there is a last week worth looking at.</summary>
    public static bool IsDue(OrreryData data, DateOnly today)
    {
        var thisWeek = WeekStart.Of(today);
        if (data.Settings.LastReviewedWeek is { } seen && seen >= thisWeek)
            return false;
        if (!data.Goals.Any(g => !g.Archived))
            return false;
        // Nothing to look back on in a brand new install.
        return data.Contributions.Count > 0 && data.Contributions.Min(c => c.Day) < thisWeek;
    }

    public static void MarkSeen(OrreryData data, DateOnly today) => data.Settings.LastReviewedWeek = WeekStart.Of(today);

    /// <summary>The week before the current one, and what the current one holds.</summary>
    public static WeekReview Build(OrreryData data, DateOnly today)
    {
        var thisStart = WeekStart.Of(today);
        var from = thisStart.AddDays(-7);
        var to = thisStart.AddDays(-1);
        var nextTo = thisStart.AddDays(6);
        var log = data.Contributions;
        var week = log.Where(c => c.Day >= from && c.Day <= to).ToList();
        var goals = data.Goals.Where(g => !g.Archived).ToList();

        var good = new List<ReviewNote>();
        var bad = new List<ReviewNote>();

        // --- The good ---
        var byDay = week.GroupBy(c => c.Day).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        if (byDay.Count > 0)
            good.Add(new ReviewNote(byDay.Count == 1 ? "You showed up on 1 day" : $"You showed up on {byDay.Count} days",
                byDay[0].Count() > 1 ? $"most on {byDay[0].Key:dddd}, {byDay[0].Count()} things" : ""));

        foreach (var goal in goals.Where(g => Progress.IsComplete(g, log)
                                              && week.Any(c => c.GoalId == g.Id)
                                              && Progress.LastWorked(g, log) is { } last && last >= from && last <= to))
            good.Add(new ReviewNote($"Finished {goal.Title}", "", goal.Id));

        foreach (var category in data.Categories)
        {
            var streak = Streaks.For(category, data, today);
            if (streak.Current >= 3)
                good.Add(new ReviewNote($"{category.Name}: {streak.Current} days in a row", streak.Best > streak.Current ? $"best {streak.Best}" : "your best"));
        }

        foreach (var (goal, total) in goals
                     .Select(g => (Goal: g, Total: Progress.Between(g, log, from, to)))
                     .Where(x => x.Total > 0 && !Progress.IsComplete(x.Goal, log))
                     .OrderByDescending(x => x.Total).Take(3))
            good.Add(new ReviewNote($"Made progress on {goal.Title}", $"{Planner.Format(total)} {goal.Measure.UnitLabel} last week".Replace("  ", " "), goal.Id));

        // --- The bad ---
        foreach (var goal in goals.Where(g => g.Deadline is { } d && d >= from && d <= to && !Progress.IsComplete(g, log)))
            bad.Add(new ReviewNote($"{goal.Title} was due {goal.Deadline:dddd, MMM d}", "not finished", goal.Id));

        foreach (var goal in goals)
            foreach (var step in goal.Leaves().Where(s => !s.Done && s.PlannedFor is { } d && d >= from && d <= to))
                bad.Add(new ReviewNote($"{step.Title}", $"planned for {step.PlannedFor:dddd}, not done · {goal.Title}", goal.Id));

        foreach (var category in data.Categories)
        {
            var mine = goals.Where(g => g.CategoryId == category.Id).ToList();
            var missed = Enumerable.Range(0, 7).Select(i => from.AddDays(i))
                .Count(d => Streaks.StatusOn(mine, log, d) == DayStatus.Missed);
            if (missed > 0)
                bad.Add(new ReviewNote($"{category.Name}: something was missed on {(missed == 1 ? "1 day" : $"{missed} days")}"));
        }

        var passions = goals.Where(g => data.CategoryOf(g)?.Role == CategoryRole.Passion
                                         && !Progress.IsComplete(g, log)
                                         && !week.Any(c => c.GoalId == g.Id)).ToList();
        if (passions.Count > 0)
            bad.Add(new ReviewNote(passions.Count == 1 ? $"Nothing on {passions[0].Title}" : "Nothing on " + string.Join(", ", passions.Take(3).Select(g => g.Title)) + (passions.Count > 3 ? $" and {passions.Count - 3} more" : ""),
                "the things you do for yourself"));

        // --- Ahead ---
        var ahead = CalendarModel.Events(data, thisStart, nextTo, today)
            .Where(e => e.Day >= thisStart && e.Day <= nextTo)
            .Where(e => e.Kind is CalendarKind.Deadline or CalendarKind.Part or CalendarKind.Planned or CalendarKind.Due)
            .OrderBy(e => e.Day)
            .Select(e => new ReviewNote(e.Title, WhatAndWhere(data, e), e.GoalId, e.Day))
            .ToList();

        return new WeekReview(from, to, byDay.Count, week.Count, week.Sum(c => c.Minutes), week.Select(c => c.GoalId).Distinct().Count(),
            good, bad.Take(MostShown + 2).ToList(), ahead.Take(MostShown + 2).ToList(), thisStart, nextTo);
    }

    private static string WhatAndWhere(OrreryData data, CalendarEvent e)
    {
        var what = Label(e);
        what = char.ToUpperInvariant(what[0]) + what[1..];
        var goal = data.Goals.FirstOrDefault(g => g.Id == e.GoalId)?.Title;
        return goal is null || goal == e.Title ? what : $"{what} · {goal}";
    }

    private static string Label(CalendarEvent e) => e.Kind switch
    {
        CalendarKind.Deadline => "deadline",
        CalendarKind.Part => e.Detail.StartsWith("Needed", StringComparison.Ordinal) ? "needed" : "due",
        CalendarKind.Planned => "planned",
        _ => "comes due",
    };
}
