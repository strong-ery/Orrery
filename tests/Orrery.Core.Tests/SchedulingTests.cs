using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;
using Xunit;

namespace Orrery.Core.Tests;

public class SchedulingTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    /// <summary>A passion with three steps; nothing urgent, so the planner would normally offer only the first.</summary>
    private static (OrreryData Data, Goal Goal) Project()
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Passion);
        var goal = new Goal { Title = "Prism", CategoryId = cat.Id, Created = Monday.AddDays(-10) };
        goal.Steps.Add(new Step { Title = "one", EstimateMinutes = 60 });
        goal.Steps.Add(new Step { Title = "two", EstimateMinutes = 30 });
        goal.Steps.Add(new Step { Title = "three", EstimateMinutes = 90 });
        data.Goals.Add(goal);
        return (data, goal);
    }

    [Fact]
    public void A_step_planned_for_today_is_on_todays_list()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[2].Id, Monday);

        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10));

        Assert.Contains(plan, p => p.StepId == goal.Steps[2].Id);
    }

    [Fact]
    public void A_step_planned_for_a_later_day_is_held_back_today()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[0].Id, Monday.AddDays(3));

        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10));

        Assert.DoesNotContain(plan, p => p.StepId == goal.Steps[0].Id);
    }

    [Fact]
    public void A_missed_planned_day_rolls_onto_today()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[1].Id, Monday.AddDays(-2));

        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10));

        Assert.Contains(plan, p => p.StepId == goal.Steps[1].Id);
        Assert.Equal(1, Scheduling.Overdue(data, Monday));
    }

    [Fact]
    public void Unplanning_restores_the_normal_order()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[0].Id, Monday.AddDays(3));
        Scheduling.Plan(data, goal.Id, goal.Steps[0].Id, null);

        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10));

        Assert.Contains(plan, p => p.StepId == goal.Steps[0].Id);
    }

    [Fact]
    public void Day_load_adds_up_planned_minutes_and_ignores_done_steps()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[0].Id, Monday);
        Scheduling.Plan(data, goal.Id, goal.Steps[2].Id, Monday);
        goal.Steps[2].Done = true;

        Assert.Equal(60, Scheduling.Load(data, Monday));
        data.Settings.DayCapacityMinutes = 120;
        Assert.Equal(0.5, Scheduling.Fullness(data, Monday), 3);
    }

    [Fact]
    public void Calendar_shows_planned_steps_on_their_day()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[1].Id, Monday.AddDays(2));

        var events = CalendarModel.Events(data, Monday, Monday.AddDays(7), Monday);

        var planned = Assert.Single(events, e => e.Kind == CalendarKind.Planned);
        Assert.Equal(Monday.AddDays(2), planned.Day);
        Assert.Equal("two", planned.Title);
    }

    [Fact]
    public void Candidates_offer_the_next_step_and_anything_already_planned()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[2].Id, Monday.AddDays(1));

        var candidates = Scheduling.Candidates(data, Monday);

        Assert.Contains(candidates, c => c.StepId == goal.Steps[0].Id && c.Day is null);
        Assert.Contains(candidates, c => c.StepId == goal.Steps[2].Id && c.Day == Monday.AddDays(1));
        Assert.DoesNotContain(candidates, c => c.StepId == goal.Steps[1].Id);
    }

    [Fact]
    public void Planned_days_survive_saving()
    {
        var (data, goal) = Project();
        Scheduling.Plan(data, goal.Id, goal.Steps[0].Id, Monday.AddDays(4));

        var copy = JsonStore.FromJson(JsonStore.ToJson(data))!;

        Assert.Equal(Monday.AddDays(4), copy.Goals[0].Steps[0].PlannedFor);
    }
}
