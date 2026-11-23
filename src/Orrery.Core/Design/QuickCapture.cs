using System.Globalization;
using System.Text.RegularExpressions;
using Orrery.Core.Models;

namespace Orrery.Core.Design;

public enum CaptureKind { None, NewStep, NewGoal }

/// <summary>What a line of quick-add text would do, shown before anything is changed.</summary>
public sealed record CapturePreview(
    CaptureKind Kind,
    string Title,
    string Summary,
    IReadOnlyList<string> Chips,
    string? Problem,
    Goal? TargetGoal = null,
    Step? After = null,
    DateOnly? BlockedUntil = null,
    DateOnly? Deadline = null,
    Energy? Energy = null,
    int? Minutes = null,
    CategoryRole? Role = null,
    Rhythm? Rhythm = null,
    double PerWeekMinutes = 0,
    Step? Parent = null)
{
    public bool CanApply => Kind != CaptureKind.None && Problem is null;
}

/// <summary>
/// One box that adds anything. A line like <c>Unit 5 in Biology after Unit 4 ~90m</c> becomes a step in the
/// right goal waiting on the right step, and <c>Renew passport due dec 20</c> becomes a new goal. The
/// preview says exactly what will happen, so a misread is visible before Enter.
/// <list type="bullet">
/// <item><c>in X</c> or <c>X:</c> — which goal it goes under</item>
/// <item><c>after X</c> — wait for that step</item>
/// <item><c>due/by DATE</c> — deadline for a new goal; <c>from DATE</c> — a step can't start before it</item>
/// <item><c>~45m</c>, <c>~1.5h</c> — time estimate; <c>!low</c> <c>!high</c> — energy</item>
/// <item><c>#passion</c> <c>#routine</c> <c>#obligation</c> — bucket; <c>daily</c> <c>weekly</c> <c>every 3 days</c> <c>3h/week</c> — rhythm</item>
/// </list>
/// </summary>
public static class QuickCapture
{
    public static CapturePreview Parse(OrreryData data, string input, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new CapturePreview(CaptureKind.None, "", "", [], null);

        var text = " " + Regex.Replace(input.Replace(',', ' '), @"\s+", " ").Trim() + " ";
        var chips = new List<string>();

        int? minutes = null;
        text = Take(text, @"\s~(\d+(?:\.\d+)?)\s?(h|m)\b", m =>
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            minutes = (int)Math.Round(m.Groups[2].Value == "h" ? n * 60 : n);
            chips.Add($"~{minutes} min");
        });

        Energy? energy = null;
        text = Take(text, @"\s!(low|easy|med|medium|high|hard)\b", m =>
        {
            energy = m.Groups[1].Value switch { "low" or "easy" => Models.Energy.Low, "high" or "hard" => Models.Energy.High, _ => Models.Energy.Medium };
            chips.Add($"{energy!.Value.ToString().ToLowerInvariant()} energy");
        });

        CategoryRole? role = null;
        text = Take(text, @"\s#(obligation|duty|passion|want|routine|chore)s?\b", m =>
        {
            role = m.Groups[1].Value switch
            {
                "passion" or "want" => CategoryRole.Passion,
                "routine" or "chore" => CategoryRole.Routine,
                _ => CategoryRole.Obligation,
            };
        });

        double perWeek = 0;
        text = Take(text, @"\s(\d+(?:\.\d+)?)\s?(h|m)\s?(?:/|a |per )\s?week\b", m =>
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            perWeek = m.Groups[2].Value == "h" ? n * 60 : n;
            chips.Add($"{perWeek / 60:0.#} h a week");
        });

        Rhythm? rhythm = perWeek > 0 ? new Rhythm(RhythmKind.Cadence, PerWeek: perWeek) : null;
        text = Take(text, @"\severy (\d+) ?(?:d|days?)\b", m =>
        {
            var n = int.Parse(m.Groups[1].Value);
            rhythm = new Rhythm(RhythmKind.Repeat, Math.Max(1, n));
            chips.Add($"every {n} days");
        });
        text = Take(text, @"\s(daily|weekly)\b", m =>
        {
            var daily = m.Groups[1].Value == "daily";
            rhythm = new Rhythm(RhythmKind.Repeat, daily ? 1 : 7);
            chips.Add(m.Groups[1].Value);
        });

        string? problem = null;
        DateOnly? deadline = null, blockedUntil = null;
        text = Take(text, $@"\s(due|by|from)\s+({DateParser.Pattern})(?=\s)", m =>
        {
            if (!DateParser.TryParse(m.Groups[2].Value, today, out var date))
            {
                problem = $"I couldn't read “{m.Groups[2].Value}” as a date.";
                return;
            }
            if (m.Groups[1].Value == "from")
                blockedUntil = date;
            else
                deadline = date;
            chips.Add($"{(m.Groups[1].Value == "from" ? "from" : "due")} {date:MMM d}");
        });

        // "X: rest" names the goal first.
        string? goalName = null;
        var colon = Regex.Match(text, @"^\s*([^:]+?):\s+");
        if (colon.Success)
        {
            goalName = colon.Groups[1].Value;
            text = " " + text[colon.Length..];
        }

        string? afterName = null;
        text = Take(text, @"\safter (.+?)(?=\s+in\s|\s*$)", m => afterName = m.Groups[1].Value.Trim());

        var inGoal = Regex.Match(text, @"\sin (.+?)\s*$");
        Step? parent = null;
        if (goalName is null && inGoal.Success)
        {
            var name = inGoal.Groups[1].Value;
            if (FindGoal(data, name) is not null)
            {
                goalName = name;
                text = text[..inGoal.Index] + " ";
            }
            else if (FindStep(data, null, name) is { } section)
            {
                // "Quiz in Unit 2": no goal by that name, but a step by that name becomes the section.
                parent = section;
                text = text[..inGoal.Index] + " ";
            }
        }

        var title = text.Trim();
        Goal? target = parent is null ? null : data.Goals.First(g => g.Steps.Contains(parent));
        if (goalName is not null)
        {
            target = FindGoal(data, goalName);
            if (parent is null && target is null)
                problem ??= $"No goal matches “{goalName}”.";
        }

        Step? after = null;
        if (afterName is not null)
        {
            after = FindStep(data, target, afterName);
            if (after is null)
                problem ??= $"No step matches “{afterName}”.";
            else
                target ??= data.Goals.First(g => g.Steps.Contains(after));
        }

        if (title.Length == 0)
            problem ??= "Add a name for it.";

        if (target is not null)
        {
            if (deadline is not null)
                chips.Remove(chips.FirstOrDefault(c => c.StartsWith("due ")) ?? "");
            var parts = new List<string> { parent is null
                ? $"Add step “{title}” to {target.Title}"
                : $"Add step “{title}” inside “{parent.Title}” ({target.Title})" };
            if (after is not null)
                parts.Add($"after “{after.Title}”");
            if (blockedUntil is { } from)
                parts.Add($"not before {from:MMM d}");
            return new CapturePreview(CaptureKind.NewStep, title, string.Join(" · ", parts), chips, problem,
                target, after, blockedUntil, null, energy, minutes, null, null, 0, parent);
        }

        var finalRole = role ?? rhythm?.Kind switch
        {
            RhythmKind.Repeat => CategoryRole.Routine,
            RhythmKind.Cadence => CategoryRole.Passion,
            _ => CategoryRole.Obligation,
        };
        var bucket = data.Categories.FirstOrDefault(c => c.Role == finalRole)?.Name ?? finalRole.ToString();
        return new CapturePreview(CaptureKind.NewGoal, title, $"New goal “{title}” under {bucket}", chips, problem,
            null, null, blockedUntil, deadline, energy, minutes, finalRole, rhythm, perWeek);
    }

    /// <summary>Makes the change a preview described. Returns the goal it landed in.</summary>
    public static Goal Apply(OrreryData data, CapturePreview p, DateOnly today)
    {
        if (!p.CanApply)
            throw new InvalidOperationException(p.Problem ?? "Nothing to add.");

        if (p.Kind == CaptureKind.NewStep)
        {
            var goal = p.TargetGoal!;
            var step = new Step
            {
                Title = p.Title,
                Energy = p.Energy ?? Models.Energy.Medium,
                EstimateMinutes = p.Minutes,
                BlockedUntil = p.BlockedUntil,
                ParentId = p.Parent?.Id,
            };
            if (p.After is { } after)
                step.DependsOn.Add(after.Id);
            goal.Steps.Add(step);
            return goal;
        }

        var cadence = p.Rhythm?.Kind == RhythmKind.Cadence;
        var answers = new DesignAnswers
        {
            Title = p.Title,
            Role = p.Role ?? CategoryRole.Obligation,
            HasFinishLine = p.Rhythm is null,
            Measure = cadence ? MeasureKind.Time : MeasureKind.Done,
            Deadline = p.Rhythm is null ? p.Deadline : null,
            Rhythm = p.Rhythm?.Kind ?? RhythmKind.None,
            EveryDays = p.Rhythm?.EveryDays ?? 1,
            PerWeek = p.PerWeekMinutes,
            Energy = p.Energy ?? Models.Energy.Medium,
        };
        var result = GoalDesigner.Build(data, answers, today);
        var created = result.Goal ?? throw new InvalidOperationException(string.Join(" ", result.Issues.Select(i => i.Message)));
        data.Goals.Add(created);
        return created;
    }

    private static string Take(string text, string pattern, Action<Match> onMatch)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!m.Success)
            return text;
        onMatch(m);
        return text.Remove(m.Index, m.Length).Insert(m.Index, " ");
    }

    /// <summary>Exact title beats prefix beats substring beats "every word appears".</summary>
    public static Goal? FindGoal(OrreryData data, string name) =>
        BestMatch(data.Goals.Where(g => !g.Archived), g => g.Title, name);

    public static Step? FindStep(OrreryData data, Goal? within, string name)
    {
        var steps = (within is null ? data.Goals.Where(g => !g.Archived).SelectMany(g => g.Steps) : within.Steps).ToList();
        return BestMatch(steps.Where(s => !s.Done), s => s.Title, name) ?? BestMatch(steps, s => s.Title, name);
    }

    private static T? BestMatch<T>(IEnumerable<T> items, Func<T, string> title, string query) where T : class
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0)
            return null;
        var list = items.ToList();
        return list.FirstOrDefault(i => title(i).ToLowerInvariant() == q)
            ?? list.FirstOrDefault(i => title(i).ToLowerInvariant().StartsWith(q, StringComparison.Ordinal))
            ?? list.FirstOrDefault(i => title(i).ToLowerInvariant().Contains(q, StringComparison.Ordinal))
            ?? list.FirstOrDefault(i =>
            {
                var t = title(i).ToLowerInvariant();
                return q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => t.Contains(w, StringComparison.Ordinal));
            });
    }
}
