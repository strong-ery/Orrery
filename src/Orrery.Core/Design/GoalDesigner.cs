using Orrery.Core.Models;

namespace Orrery.Core.Design;

/// <summary>
/// The answers that describe a goal. Every goal Orrery can hold is a point in this small space: which bucket,
/// whether it finishes, what it's measured in, whether it repeats and whether it has a date. There are no goal
/// "types", only these questions, so a new kind of goal never needs new code.
/// </summary>
public sealed class DesignAnswers
{
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public CategoryRole Role { get; set; } = CategoryRole.Obligation;

    public bool HasFinishLine { get; set; } = true;
    public MeasureKind Measure { get; set; } = MeasureKind.Done;
    public string Unit { get; set; } = "";
    public double Target { get; set; }
    public DateOnly? Deadline { get; set; }

    public RhythmKind Rhythm { get; set; } = RhythmKind.None;
    public int EveryDays { get; set; } = 1;
    public double PerWeek { get; set; }

    public Energy Energy { get; set; } = Energy.Medium;
}

public enum IssueSeverity { Warning, Error }

public sealed record DesignIssue(IssueSeverity Severity, string Message);

/// <summary>What the answers add up to: the goal (when valid), anything wrong with it, and a plain-language read-back.</summary>
public sealed record DesignResult(Goal? Goal, IReadOnlyList<DesignIssue> Issues, IReadOnlyList<string> Summary)
{
    public bool IsValid => Goal is not null;
}

/// <summary>A starting point for the designer. Not a type: choosing one just fills in the answers.</summary>
public sealed record Starter(string Name, string Blurb, Func<DateOnly, DesignAnswers> Create);

public static class GoalDesigner
{
    public static DesignResult Build(OrreryData data, DesignAnswers a, DateOnly today)
    {
        var issues = new List<DesignIssue>();
        void Error(string m) => issues.Add(new DesignIssue(IssueSeverity.Error, m));
        void Warn(string m) => issues.Add(new DesignIssue(IssueSeverity.Warning, m));

        if (string.IsNullOrWhiteSpace(a.Title))
            Error("Give it a name.");

        if (a.Deadline is not null && !a.HasFinishLine)
            Error("A deadline needs a finish line. Turn the deadline off, or say it finishes.");
        if (a.Deadline is { } due && due < today)
            Warn("That date has already passed, so it will count as overdue straight away.");

        if (a.Measure == MeasureKind.Amount && string.IsNullOrWhiteSpace(a.Unit))
            Error("Say what the amount is measured in (pages, %, kg…).");

        var needsTarget = a.Measure is MeasureKind.Amount or MeasureKind.Time && a.HasFinishLine && a.Rhythm == RhythmKind.None;
        if (needsTarget && a.Target <= 0)
            Error("A goal with a finish line needs a target to reach.");

        if (a.Rhythm == RhythmKind.Repeat && a.EveryDays < 1)
            Error("“Every N days” needs N to be at least 1.");
        if (a.Rhythm == RhythmKind.Cadence && a.PerWeek <= 0)
            Error("Say how much per week you're aiming for.");
        if (a.Rhythm != RhythmKind.None && a.Deadline is not null)
            Warn("It repeats, so its rhythm sets the pace and the deadline is only a reminder.");

        if (a.Rhythm == RhythmKind.None && !a.HasFinishLine && a.Deadline is null)
            Warn("Nothing says when to work on this, so it will only come up after it's been left alone for a while. A weekly rhythm would keep it alive.");
        if (a.Rhythm != RhythmKind.None && a.HasFinishLine && a.Measure == MeasureKind.Done && a.Deadline is null)
            Warn("It repeats forever, so a finish line won't do anything. Consider turning it off.");

        var summary = Describe(data, a);
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
            return new DesignResult(null, issues, summary);

        var goal = new Goal
        {
            Title = a.Title.Trim(),
            Notes = a.Notes,
            CategoryId = data.CategoryFor(a.Role).Id,
            Measure = new Measure(a.Measure, a.Unit.Trim(), a.Measure == MeasureKind.Done ? 0 : a.Target),
            Rhythm = a.Rhythm switch
            {
                RhythmKind.Repeat => new Rhythm(RhythmKind.Repeat, Math.Max(1, a.EveryDays)),
                RhythmKind.Cadence => new Rhythm(RhythmKind.Cadence, PerWeek: a.PerWeek),
                _ => Rhythm.None,
            },
            HasFinishLine = a.HasFinishLine,
            Deadline = a.HasFinishLine ? a.Deadline : null,
            DefaultEnergy = a.Energy,
            Created = today,
        };
        return new DesignResult(goal, issues, summary);
    }

    /// <summary>The answers an existing goal was made from, so the designer can edit it.</summary>
    public static DesignAnswers AnswersFrom(Goal g, OrreryData data) => new()
    {
        Title = g.Title,
        Notes = g.Notes,
        Role = data.CategoryOf(g)?.Role ?? CategoryRole.Obligation,
        HasFinishLine = g.HasFinishLine,
        Measure = g.Measure.Kind,
        Unit = g.Measure.Unit,
        Target = g.Measure.Target,
        Deadline = g.Deadline,
        Rhythm = g.Rhythm.Kind,
        EveryDays = g.Rhythm.EveryDays,
        PerWeek = g.Rhythm.PerWeek,
        Energy = g.DefaultEnergy,
    };

    /// <summary>Copies a freshly designed goal's settings onto an existing one, keeping its steps, history and identity.</summary>
    public static void ApplyTo(Goal existing, Goal designed)
    {
        existing.Title = designed.Title;
        existing.Notes = designed.Notes;
        existing.CategoryId = designed.CategoryId;
        existing.Measure = designed.Measure;
        existing.Rhythm = designed.Rhythm;
        existing.HasFinishLine = designed.HasFinishLine;
        existing.Deadline = designed.Deadline;
        existing.DefaultEnergy = designed.DefaultEnergy;
    }

    /// <summary>The read-back shown beside the form, so you can see what the answers mean before saving.</summary>
    public static IReadOnlyList<string> Describe(OrreryData data, DesignAnswers a)
    {
        var lines = new List<string>();
        var bucket = data.Categories.FirstOrDefault(c => c.Role == a.Role)?.Name ?? a.Role.ToString();
        lines.Add($"Lives under {bucket}.");

        var unit = a.Measure switch
        {
            MeasureKind.Time => "min",
            MeasureKind.Steps => "steps",
            MeasureKind.Amount => a.Unit.Trim(),
            _ => "",
        };

        lines.Add(a.Measure switch
        {
            MeasureKind.Done => "Done or not done. Nothing in between.",
            MeasureKind.Steps => "Progress is the steps you finish. You list them, and can make them wait on each other.",
            MeasureKind.Time => "Progress is focused time. Sessions are logged and graphed.",
            _ => $"Progress is counted in {(unit.Length > 0 ? unit : "your own unit")}.",
        });

        if (!a.HasFinishLine)
            lines.Add("It never finishes. Success is showing up, so progress is shown as time and sessions rather than a percentage.");
        else if (a.Rhythm == RhythmKind.None)
        {
            lines.Add(a.Measure is MeasureKind.Amount or MeasureKind.Time && a.Target > 0
                ? $"Finish line: {a.Target:0.#} {unit}."
                : "It's finished when it's done.");
            if (a.Deadline is { } due)
                lines.Add($"Due {due:MMM d}. Orrery works out how much to do each day to get there, and warns you as the pace climbs.");
        }

        switch (a.Rhythm)
        {
            case RhythmKind.Repeat when a.EveryDays <= 1:
                lines.Add("Comes due every day.");
                break;
            case RhythmKind.Repeat:
                lines.Add($"Comes due every {a.EveryDays} days, and resets once it's done.");
                break;
            case RhythmKind.Cadence:
                lines.Add($"Aims for {a.PerWeek:0.#} {(unit.Length > 0 ? unit : "times")} a week, spread however you like. It only starts asking when the week is running out.");
                break;
        }

        if (a.Energy == Energy.Low)
            lines.Add("Low energy, so it still shows up on bad days.");
        return lines;
    }

    public static IReadOnlyList<Starter> Starters { get; } =
    [
        new("One-off task", "Something you do once. Done or not done.", _ => new DesignAnswers
        {
            Role = CategoryRole.Obligation, Measure = MeasureKind.Done, HasFinishLine = true,
        }),
        new("Repeating routine", "A chore or habit that comes round again.", _ => new DesignAnswers
        {
            Role = CategoryRole.Routine, Measure = MeasureKind.Done, HasFinishLine = false,
            Rhythm = RhythmKind.Repeat, EveryDays = 7, Energy = Energy.Low,
        }),
        new("Target by a date", "A number to reach before a deadline.", today => new DesignAnswers
        {
            Role = CategoryRole.Obligation, Measure = MeasureKind.Amount, Unit = "%", Target = 100,
            HasFinishLine = true, Deadline = today.AddDays(30),
        }),
        new("Checklist", "Steps you finish, in any order or a fixed one.", today => new DesignAnswers
        {
            Role = CategoryRole.Obligation, Measure = MeasureKind.Steps, HasFinishLine = true,
        }),
        new("Open-ended project", "Never finishes. You put time into it every week.", _ => new DesignAnswers
        {
            Role = CategoryRole.Passion, Measure = MeasureKind.Time, HasFinishLine = false,
            Rhythm = RhythmKind.Cadence, PerWeek = 180,
        }),
        new("Daily practice", "A set amount of time, every day.", _ => new DesignAnswers
        {
            Role = CategoryRole.Passion, Measure = MeasureKind.Time, Target = 30, HasFinishLine = false,
            Rhythm = RhythmKind.Repeat, EveryDays = 1,
        }),
    ];
}
