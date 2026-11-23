namespace Orrery.Core.Models;

/// <summary>The three buckets a goal can live in. The role drives balancing and streaks, nothing else.</summary>
public enum CategoryRole
{
    /// <summary>Things that have to happen.</summary>
    Obligation,
    /// <summary>Things you want to do.</summary>
    Passion,
    /// <summary>Things that repeat and are simply done or not done.</summary>
    Routine,
}

/// <summary>How progress on a goal is counted.</summary>
public enum MeasureKind
{
    /// <summary>Done or not done. Nothing in between.</summary>
    Done,
    /// <summary>A list of steps; progress is how many are finished.</summary>
    Steps,
    /// <summary>A quantity in a unit the goal names (pages, %, kg, ...).</summary>
    Amount,
    /// <summary>Minutes of focused time.</summary>
    Time,
}

/// <summary>Whether a goal repeats, and how.</summary>
public enum RhythmKind
{
    /// <summary>No rhythm. It is worked on whenever.</summary>
    None,
    /// <summary>Comes due every N days, and resets after it is done.</summary>
    Repeat,
    /// <summary>Wants a total per week, spread however you like.</summary>
    Cadence,
}

/// <summary>How much of you a step takes. Low-energy mode only offers Low steps, from any goal.</summary>
public enum Energy { Low, Medium, High }

/// <summary>What one goal is measured in. A goal's whole shape comes from this and <see cref="Rhythm"/>.</summary>
public sealed record Measure(MeasureKind Kind, string Unit = "", double Target = 0)
{
    public static Measure Done { get; } = new(MeasureKind.Done);
    public static Measure Steps { get; } = new(MeasureKind.Steps);

    /// <summary>The unit shown next to numbers for this measure.</summary>
    public string UnitLabel => Kind switch
    {
        MeasureKind.Done => "",
        MeasureKind.Steps => "steps",
        MeasureKind.Time => "min",
        _ => Unit,
    };
}

/// <summary>How a goal repeats. <see cref="EveryDays"/> applies to Repeat, <see cref="PerWeek"/> to Cadence.</summary>
public sealed record Rhythm(RhythmKind Kind = RhythmKind.None, int EveryDays = 1, double PerWeek = 0)
{
    public static Rhythm None { get; } = new();
}

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AccentHex { get; set; } = "#FFFFFF";
    public CategoryRole Role { get; set; }
}

public sealed class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public Energy Energy { get; set; } = Energy.Medium;
    public int? EstimateMinutes { get; set; }
    public bool Done { get; set; }
    public int DeferCount { get; set; }

    /// <summary>The section this step sits in, or null at the top level. A step with children is a section.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Marks a section explicitly, so one with nothing in it yet is still a section.</summary>
    public bool IsGroup { get; set; }

    /// <summary>Not available before this day, for things like "waiting for a reply" or "opens Friday".</summary>
    public DateOnly? BlockedUntil { get; set; }

    /// <summary>Steps (in any goal) that must be finished before this one can start.</summary>
    public List<Guid> DependsOn { get; set; } = [];
}

public sealed class Goal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public Guid CategoryId { get; set; }

    public Measure Measure { get; set; } = Measure.Done;
    public Rhythm Rhythm { get; set; } = Rhythm.None;

    /// <summary>False for things that never finish, like a big creative project.</summary>
    public bool HasFinishLine { get; set; } = true;
    public DateOnly? Deadline { get; set; }

    /// <summary>Energy used for the goal's own entry when it has no steps.</summary>
    public Energy DefaultEnergy { get; set; } = Energy.Medium;

    public List<Step> Steps { get; set; } = [];

    /// <summary>Goals that must be complete before any step of this one can start.</summary>
    public List<Guid> DependsOnGoals { get; set; } = [];

    public DateOnly Created { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Archived { get; set; }
}

/// <summary>One piece of progress: minutes worked, units logged, a step finished, a chore done.</summary>
public sealed class Contribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoalId { get; set; }
    public Guid? StepId { get; set; }
    public DateOnly Day { get; set; }
    public DateTime At { get; set; } = DateTime.Now;

    /// <summary>In the goal's own measure: minutes, units, or 1 for a step or a done.</summary>
    public double Amount { get; set; } = 1;

    /// <summary>Time spent, whatever the goal is measured in. For a Time goal this equals <see cref="Amount"/>.</summary>
    public double Minutes { get; set; }

    /// <summary>What the session was about, for graphs of where the time went.</summary>
    public string Focus { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class Settings
{
    /// <summary>-1 favours obligations, +1 favours passions.</summary>
    public double Bias { get; set; }
    public bool LowEnergy { get; set; }
    public int MaxToday { get; set; } = 5;

    /// <summary>Which Goals view was last used: 0 orbit map, 1 list, 2 dependency graph.</summary>
    public int GoalsMode { get; set; }

    /// <summary>Whether Orrery may interrupt you with a nudge.</summary>
    public bool Nudges { get; set; } = true;

    /// <summary>When each goal was last nudged about, so the same thing isn't repeated every few minutes.</summary>
    public Dictionary<Guid, DateTime> NudgeLog { get; set; } = [];

    /// <summary>Set once the "Orrery keeps running in the tray" note has been shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>A focus session in progress. Stored so closing the app doesn't lose the timer.</summary>
    public FocusState? Focus { get; set; }
}

public sealed class FocusState
{
    public Guid GoalId { get; set; }
    public Guid? StepId { get; set; }
    public DateTime StartedAt { get; set; }
    public double PausedSeconds { get; set; }
    public DateTime? PausedAt { get; set; }
    public string Tag { get; set; } = "";

    public TimeSpan Elapsed(DateTime now)
    {
        var total = (now - StartedAt).TotalSeconds - PausedSeconds - (PausedAt is { } p ? (now - p).TotalSeconds : 0);
        return TimeSpan.FromSeconds(Math.Max(0, total));
    }
}

/// <summary>Everything Orrery stores. One file, no accounts.</summary>
public sealed class OrreryData
{
    public List<Category> Categories { get; set; } = [];
    public List<Goal> Goals { get; set; } = [];
    public List<Contribution> Contributions { get; set; } = [];
    public Settings Settings { get; set; } = new();

    public static OrreryData CreateEmpty() => new()
    {
        Categories =
        [
            new Category { Name = "Obligations", Role = CategoryRole.Obligation, AccentHex = "#FF7A5C" },
            new Category { Name = "Passions", Role = CategoryRole.Passion, AccentHex = "#9B7CFF" },
            new Category { Name = "Routines", Role = CategoryRole.Routine, AccentHex = "#3CC8B4" },
        ],
    };

    public Category CategoryFor(CategoryRole role) => Categories.First(c => c.Role == role);
    public Category? CategoryOf(Goal goal) => Categories.FirstOrDefault(c => c.Id == goal.CategoryId);
}
