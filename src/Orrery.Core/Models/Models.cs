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

/// <summary>How the groups inside a big goal get their dates.</summary>
public enum GroupDateMode
{
    /// <summary>Groups have no dates.</summary>
    Off,
    /// <summary>You pick a date for each group.</summary>
    Custom,
    /// <summary>The groups still to do are spread evenly from today to the goal's deadline.</summary>
    Even,
}

public sealed class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public Energy Energy { get; set; } = Energy.Medium;
    public int? EstimateMinutes { get; set; }
    public bool Done { get; set; }

    /// <summary>The day you decided to do this, from the calendar. Today's list leans on it; it never blocks anything.</summary>
    public DateOnly? PlannedFor { get; set; }

    /// <summary>The section this step sits in, or null at the top level. A step with children is a section.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>When this group should be finished, in a goal that sets its group dates by hand.</summary>
    public DateOnly? Due { get; set; }

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

    /// <summary>Whether (and how) the groups in this goal have their own dates.</summary>
    public GroupDateMode GroupDates { get; set; }

    /// <summary>Energy used for the goal's own entry when it has no steps.</summary>
    public Energy DefaultEnergy { get; set; } = Energy.Medium;

    public List<Step> Steps { get; set; } = [];

    /// <summary>Goals that must be complete before any step of this one can start.</summary>
    public List<Guid> DependsOnGoals { get; set; } = [];

    public DateOnly Created { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public bool Archived { get; set; }

    /// <summary>For a goal with no steps of its own: the day you decided to work on it.</summary>
    public DateOnly? PlannedFor { get; set; }
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

    /// <summary>How many minutes of planned work make a full day on the calendar.</summary>
    public int DayCapacityMinutes { get; set; } = 240;

    /// <summary>Play a sound when a reminder appears.</summary>
    public bool NotifySound { get; set; } = true;

    /// <summary>File name of a custom sound in the app's sounds folder; empty means the built-in chime.</summary>
    public string NotifySoundFile { get; set; } = "";

    /// <summary>The original name of the custom sound, for display.</summary>
    public string NotifySoundName { get; set; } = "";

    /// <summary>The first day of the week: where the calendar starts its rows and when the weekly review comes round.</summary>
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Monday;

    /// <summary>The first day of the last week you looked back on, so the review is offered once per week.</summary>
    public DateOnly? LastReviewedWeek { get; set; }

    /// <summary>When GitHub was last asked about a new version, and the newest version already announced.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }
    public string AnnouncedUpdate { get; set; } = "";

    /// <summary>Which Goals view was last used: 0 orbit map, 1 list, 2 dependency graph.</summary>
    public int GoalsMode { get; set; }

    /// <summary>Whether Orrery may interrupt you with a nudge.</summary>
    public bool Nudges { get; set; } = true;

    /// <summary>Which reminders you want. Each can be turned off on its own.</summary>
    public bool NudgeDeadlines { get; set; } = true;
    public bool NudgeEvening { get; set; } = true;
    public bool NudgePassions { get; set; } = true;

    /// <summary>How many reminders in a day at most, and how long before the same goal may remind you again.</summary>
    public int NudgeMaxPerDay { get; set; } = 4;
    public double NudgeRepeatUrgentHours { get; set; } = 2;
    public double NudgeRepeatGentleHours { get; set; } = 4;
    public double NudgeRepeatPassionHours { get; set; } = 4;

    /// <summary>Hours of the day (0 to 24) between which reminders are allowed, and when the gentle ones start.</summary>
    public double NudgeFromHour { get; set; } = 10;
    public double NudgeUntilHour { get; set; } = 21.5;
    public double NudgeGentleFromHour { get; set; } = 17;

    /// <summary>When each goal was last nudged about, so the same thing isn't repeated every few minutes.</summary>
    public Dictionary<Guid, DateTime> NudgeLog { get; set; } = [];

    /// <summary>Set once the "Orrery keeps running in the tray" note has been shown.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>The order you arranged today's list in, by step (or goal) id. Only good for <see cref="PlanOrderDay"/>.</summary>
    public List<Guid> PlanOrder { get; set; } = [];
    public DateOnly? PlanOrderDay { get; set; }

    /// <summary>A focus session in progress. Stored so closing the app doesn't lose the timer.</summary>
    public FocusState? Focus { get; set; }

    /// <summary>The page, window and panels as you left them.</summary>
    public UiState Ui { get; set; } = new();
}

/// <summary>Where you left things, so Orrery reopens exactly as it was closed.</summary>
public sealed class UiState
{
    /// <summary>Which page: Today, Goals, Calendar, History or Settings.</summary>
    public string Page { get; set; } = "Today";

    /// <summary>The window, as last seen in its normal (not maximized) state. Null until it has been moved or resized.</summary>
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Maximized { get; set; }

    /// <summary>How far ahead the Goals timeline looks, in days.</summary>
    public int TimelineSpan { get; set; } = 42;

    /// <summary>The goal (and step) whose panel was open.</summary>
    public Guid? OpenGoal { get; set; }
    public Guid? OpenStep { get; set; }

    /// <summary>The month and day the calendar was showing.</summary>
    public DateOnly? CalendarMonth { get; set; }
    public DateOnly? CalendarDay { get; set; }
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
            new Category { Name = "Obligations", Role = CategoryRole.Obligation, AccentHex = "#E0896C" },
            new Category { Name = "Passions", Role = CategoryRole.Passion, AccentHex = "#9F8FE6" },
            new Category { Name = "Routines", Role = CategoryRole.Routine, AccentHex = "#62B8A9" },
        ],
    };

    public Category CategoryFor(CategoryRole role) => Categories.First(c => c.Role == role);
    public Category? CategoryOf(Goal goal) => Categories.FirstOrDefault(c => c.Id == goal.CategoryId);
}
