using Orrery.Core.Design;
using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.Core.Tests;

public class EngineTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    private static (OrreryData Data, Goal Goal) One(DesignAnswers a, DateOnly? created = null)
    {
        var data = OrreryData.CreateEmpty();
        var result = GoalDesigner.Build(data, a, Monday);
        Assert.True(result.IsValid, string.Join(" ", result.Issues.Select(i => i.Message)));
        var goal = result.Goal!;
        goal.Created = created ?? Monday;
        data.Goals.Add(goal);
        return (data, goal);
    }

    private static void Log(OrreryData data, Goal g, DateOnly day, double amount) =>
        data.Contributions.Add(new Contribution { GoalId = g.Id, Day = day, Amount = amount });

    // --- Designer ----------------------------------------------------------------------------------

    [Fact]
    public void Designer_rejects_a_deadline_without_a_finish_line()
    {
        var r = GoalDesigner.Build(OrreryData.CreateEmpty(),
            new DesignAnswers { Title = "x", HasFinishLine = false, Deadline = Monday.AddDays(5) }, Monday);
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Designer_needs_a_unit_for_amounts_and_a_target_for_finite_goals()
    {
        var r = GoalDesigner.Build(OrreryData.CreateEmpty(),
            new DesignAnswers { Title = "x", Measure = MeasureKind.Amount }, Monday);
        Assert.Contains(r.Issues, i => i.Message.Contains("measured in"));
        Assert.Contains(r.Issues, i => i.Message.Contains("target"));
    }

    [Fact]
    public void Every_starter_produces_a_valid_goal()
    {
        foreach (var starter in GoalDesigner.Starters)
        {
            var a = starter.Create(Monday);
            a.Title = starter.Name;
            var r = GoalDesigner.Build(OrreryData.CreateEmpty(), a, Monday);
            Assert.True(r.IsValid, $"{starter.Name}: {string.Join(" ", r.Issues.Select(i => i.Message))}");
        }
    }

    // --- Demand ------------------------------------------------------------------------------------

    [Fact]
    public void A_quota_with_a_deadline_asks_for_the_remaining_share_each_day()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "School", Role = CategoryRole.Obligation, Measure = MeasureKind.Amount, Unit = "%",
            Target = 75, Deadline = Monday.AddDays(9),
        }, Monday.AddDays(-10));
        Log(data, goal, Monday.AddDays(-10), 35);

        var d = Demands.For(goal, data.Contributions, Monday);
        Assert.Equal(DemandSource.Deadline, d.Source);
        Assert.Equal(4.0, d.Target, 3); // 40 left over 10 days, today included
        Assert.True(d.Enforced);
    }

    [Fact]
    public void The_daily_ask_grows_when_a_day_is_skipped()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "School", Measure = MeasureKind.Amount, Unit = "%", Target = 60, Deadline = Monday.AddDays(5),
        });
        var today = Demands.For(goal, data.Contributions, Monday).Target;
        var tomorrow = Demands.For(goal, data.Contributions, Monday.AddDays(1)).Target;
        Assert.True(tomorrow > today);
    }

    [Fact]
    public void A_weekly_routine_is_only_enforced_on_the_last_day_of_its_window()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "Laundry", Role = CategoryRole.Routine, HasFinishLine = false, Rhythm = RhythmKind.Repeat, EveryDays = 7,
        });
        Assert.False(Demands.For(goal, data.Contributions, Monday).Enforced);
        Assert.True(Demands.For(goal, data.Contributions, Monday.AddDays(6)).Enforced);

        Log(data, goal, Monday.AddDays(2), 1);
        var mid = Demands.For(goal, data.Contributions, Monday.AddDays(3));
        Assert.True(mid.IsMet); // done late in the window still counts
    }

    [Fact]
    public void A_weekly_cadence_only_forces_a_day_once_the_week_is_running_out()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "Prism", Role = CategoryRole.Passion, Measure = MeasureKind.Time, HasFinishLine = false,
            Rhythm = RhythmKind.Cadence, PerWeek = 180,
        });
        Assert.False(Demands.For(goal, data.Contributions, Monday).IsDemanding);
        Assert.True(Demands.For(goal, data.Contributions, Monday.AddDays(3)).IsDemanding);
        Assert.True(Demands.For(goal, data.Contributions, Monday.AddDays(5)).Target
                    > Demands.For(goal, data.Contributions, Monday.AddDays(3)).Target);
    }

    // --- Dependencies ------------------------------------------------------------------------------

    [Fact]
    public void A_chain_of_units_only_offers_the_first_unfinished_one()
    {
        var data = OrreryData.CreateEmpty();
        var goal = GoalDesigner.Build(data, new DesignAnswers { Title = "Bio", Measure = MeasureKind.Steps }, Monday).Goal!;
        goal.Steps = Enumerable.Range(1, 4).Select(n => new Step { Title = $"Unit {n}" }).ToList();
        DependencyGraph.Chain(goal.Steps);
        data.Goals.Add(goal);

        var plan = Planner.Suggest(data, new PlanRequest(Monday));
        Assert.Equal("Unit 1", Assert.Single(plan).Title);

        goal.Steps[0].Done = true;
        plan = Planner.Suggest(data, new PlanRequest(Monday));
        Assert.Equal("Unit 2", Assert.Single(plan).Title);
    }

    [Fact]
    public void Cycles_are_refused_across_steps_and_goals()
    {
        var a = new Goal { Title = "A", Steps = [new Step { Title = "a1" }] };
        var b = new Goal { Title = "B", Steps = [new Step { Title = "b1" }] };
        b.Steps[0].DependsOn.Add(a.Steps[0].Id);
        var graph = new DependencyGraph([a, b], _ => false);

        Assert.True(graph.WouldCreateCycle(a.Steps[0].Id, b.Steps[0].Id));
        Assert.False(graph.WouldCreateCycle(b.Steps[0].Id, a.Steps[0].Id));
        Assert.True(graph.WouldCreateCycle(a.Id, a.Id));

        a.DependsOnGoals.Add(b.Id); // a waits on all of b, and b1 waits on a1: a loop
        Assert.NotEmpty(new DependencyGraph([a, b], _ => false).FindCycle());
    }

    [Fact]
    public void Nothing_can_wait_on_a_goal_that_never_finishes()
    {
        var open = new Goal { Title = "Open", HasFinishLine = false };
        var graph = new DependencyGraph([open], _ => false);
        Assert.False(graph.CanWaitOnGoal(open.Id));
        Assert.Throws<InvalidOperationException>(() =>
            DependencyGraph.Chain(new List<Goal> { open, new Goal { Title = "Next" } }));
    }

    [Fact]
    public void Unlock_count_reaches_through_the_chain()
    {
        var steps = Enumerable.Range(1, 4).Select(n => new Step { Title = $"{n}" }).ToList();
        DependencyGraph.Chain(steps);
        var graph = new DependencyGraph([new Goal { Steps = steps }], _ => false);
        Assert.Equal(3, graph.UnlockCount(steps[0].Id));
        Assert.Equal(0, graph.UnlockCount(steps[3].Id));
    }

    // --- Planner -----------------------------------------------------------------------------------

    [Fact]
    public void Low_energy_mode_keeps_low_steps_from_every_goal_including_passions()
    {
        var data = SampleData.Create(Monday);
        var plan = Planner.Suggest(data, new PlanRequest(Monday, LowEnergy: true));
        Assert.NotEmpty(plan);
        Assert.All(plan, p => Assert.Equal(Energy.Low, p.Energy));
        Assert.Contains(plan, p => p.GoalTitle.StartsWith("Finish Prism")); // the low-energy Prism step
    }

    [Fact]
    public void Bias_moves_passions_up_and_obligations_down()
    {
        var data = SampleData.Create(Monday);
        double ScoreOf(double bias, CategoryRole role) =>
            Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10, Bias: bias))
                .First(p => p.Category?.Role == role).Score;

        Assert.True(ScoreOf(1, CategoryRole.Passion) > ScoreOf(-1, CategoryRole.Passion));
        Assert.True(ScoreOf(1, CategoryRole.Obligation) < ScoreOf(-1, CategoryRole.Obligation));
    }

    [Fact]
    public void A_passion_keeps_a_seat_even_when_obligations_fill_the_list()
    {
        var data = SampleData.Create(Monday);
        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 3));
        Assert.Contains(plan, p => p.Category?.Role == CategoryRole.Passion);
    }

    // --- Streaks -----------------------------------------------------------------------------------

    [Fact]
    public void One_miss_in_a_week_is_forgiven_but_two_break_the_streak()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "Practice", Role = CategoryRole.Passion, Measure = MeasureKind.Time, Target = 30, HasFinishLine = false,
            Rhythm = RhythmKind.Repeat, EveryDays = 1,
        }, Monday.AddDays(-10));
        var passions = data.CategoryFor(CategoryRole.Passion);

        // Days -10..-1 relative to Monday. Miss day -7 and day -4; both fall inside one 7-day window.
        for (var offset = -10; offset <= -1; offset++)
            if (offset != -7 && offset != -4)
                Log(data, goal, Monday.AddDays(offset), 30);

        var broken = Streaks.For(passions, data, Monday);
        Assert.Equal(3, broken.Current); // only -3, -2, -1 since the second miss ended it

        // Remove the second miss: a single forgiven miss keeps the whole run alive.
        Log(data, goal, Monday.AddDays(-4), 30);
        var forgiven = Streaks.For(passions, data, Monday);
        Assert.Equal(9, forgiven.Current);
    }

    [Fact]
    public void Rest_days_neither_count_nor_break()
    {
        var (data, goal) = One(new DesignAnswers
        {
            Title = "Laundry", Role = CategoryRole.Routine, HasFinishLine = false, Rhythm = RhythmKind.Repeat, EveryDays = 3,
        }, Monday.AddDays(-9));
        // Due on days -9, -6, -3, 0. Done on each of the three past ones.
        foreach (var offset in new[] { -9, -6, -3 })
            Log(data, goal, Monday.AddDays(offset), 1);

        var streak = Streaks.For(data.CategoryFor(CategoryRole.Routine), data, Monday);
        Assert.Equal(3, streak.Current);
    }

    // --- Storage -----------------------------------------------------------------------------------

    [Fact]
    public void Data_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orrery-{Guid.NewGuid():N}.json");
        try
        {
            var data = SampleData.Create(Monday);
            JsonStore.Save(data, path);
            var loaded = JsonStore.Load(path)!;
            Assert.Equal(data.Goals.Count, loaded.Goals.Count);
            Assert.Equal(data.Goals[3].Steps[1].DependsOn, loaded.Goals[3].Steps[1].DependsOn);
            Assert.Equal(data.Goals[2].Measure, loaded.Goals[2].Measure);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
