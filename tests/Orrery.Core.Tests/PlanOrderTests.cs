using Orrery.Core.Engine;
using Orrery.Core.Models;
using Xunit;

namespace Orrery.Core.Tests;

public class PlanOrderTests
{
    private static readonly Category Duty = new() { Name = "Obligations", Role = CategoryRole.Obligation };
    private static readonly Category Chore = new() { Name = "Routines", Role = CategoryRole.Routine };
    private static readonly Category Want = new() { Name = "Passions", Role = CategoryRole.Passion };

    private static PlanItem Item(string title, Category category, double score) =>
        new(Guid.NewGuid(), null, title, title, category, Energy.Medium, null, 0.5, score, "", Demand.None);

    [Fact]
    public void Responsibilities_come_before_passions_whatever_the_score()
    {
        var game = Item("game", Want, 9);
        var tax = Item("tax", Duty, 1);
        var dishes = Item("dishes", Chore, 2);

        var order = PlanOrder.Arrange([game, tax, dishes], []);

        Assert.Equal(["tax", "dishes", "game"], order.Select(i => i.Title));
    }

    [Fact]
    public void A_manual_order_is_kept_inside_each_group()
    {
        var a = Item("a", Duty, 5);
        var b = Item("b", Duty, 4);
        var c = Item("c", Chore, 3);

        var order = PlanOrder.Arrange([a, b, c], [PlanOrder.KeyOf(c), PlanOrder.KeyOf(b), PlanOrder.KeyOf(a)]);

        Assert.Equal(["c", "b", "a"], order.Select(i => i.Title));
    }

    [Fact]
    public void Items_the_person_has_not_placed_follow_in_planner_order()
    {
        var a = Item("a", Duty, 5);
        var b = Item("b", Duty, 4);
        var fresh = Item("fresh", Duty, 9);

        var order = PlanOrder.Arrange([fresh, a, b], [PlanOrder.KeyOf(b), PlanOrder.KeyOf(a)]);

        Assert.Equal(["b", "a", "fresh"], order.Select(i => i.Title));
    }

    [Fact]
    public void A_responsibility_can_be_moved_later_but_not_past_a_passion()
    {
        var a = Item("a", Duty, 5);
        var b = Item("b", Chore, 4);
        var game = Item("game", Want, 3);
        var arranged = PlanOrder.Arrange([a, b, game], []);

        var swapped = PlanOrder.Move(arranged, PlanOrder.KeyOf(a), +1);
        Assert.NotNull(swapped);
        Assert.Equal([PlanOrder.KeyOf(b), PlanOrder.KeyOf(a), PlanOrder.KeyOf(game)], swapped);

        Assert.Null(PlanOrder.Move(arranged, PlanOrder.KeyOf(b), +1));
        Assert.Equal("Responsibilities stay ahead of passions.", PlanOrder.WhyNot(arranged, PlanOrder.KeyOf(b), +1));
    }

    [Fact]
    public void A_passion_cannot_be_moved_above_a_responsibility()
    {
        var a = Item("a", Duty, 5);
        var game = Item("game", Want, 3);
        var more = Item("more", Want, 2);
        var arranged = PlanOrder.Arrange([a, game, more], []);

        Assert.Null(PlanOrder.Move(arranged, PlanOrder.KeyOf(game), -1));
        Assert.NotNull(PlanOrder.Move(arranged, PlanOrder.KeyOf(more), -1));
    }

    [Fact]
    public void Moving_off_either_end_does_nothing()
    {
        var a = Item("a", Duty, 5);
        var arranged = PlanOrder.Arrange([a], []);

        Assert.Null(PlanOrder.Move(arranged, PlanOrder.KeyOf(a), -1));
        Assert.Null(PlanOrder.Move(arranged, PlanOrder.KeyOf(a), +1));
    }
}

public class PutOffTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    [Fact]
    public void Putting_a_step_off_hides_it_until_tomorrow_and_counts_it()
    {
        var step = new Step { Title = "Write essay" };

        var split = PutOff.ForStep(step, Today);

        Assert.False(split);
        Assert.Equal(1, step.DeferCount);
        Assert.Equal(Today.AddDays(1), step.BlockedUntil);
    }

    [Fact]
    public void The_third_time_suggests_splitting()
    {
        var step = new Step { Title = "Write essay" };

        PutOff.ForStep(step, Today);
        PutOff.ForStep(step, Today);

        Assert.True(PutOff.ForStep(step, Today));
    }

    [Fact]
    public void Splitting_makes_a_section_with_a_small_first_piece()
    {
        var goal = new Goal { Title = "Course" };
        var step = new Step { Title = "Write essay", EstimateMinutes = 120, DeferCount = 3, BlockedUntil = Today.AddDays(1) };
        goal.Steps.Add(step);

        var first = PutOff.Split(goal, step);

        Assert.NotNull(first);
        Assert.True(goal.IsSection(step));
        Assert.Equal(step.Id, first!.ParentId);
        Assert.Equal(Energy.Low, first.Energy);
        Assert.Equal(0, step.DeferCount);
        Assert.Null(step.BlockedUntil);
        Assert.Equal([first], goal.Leaves());
    }

    [Fact]
    public void A_section_cannot_be_split_again()
    {
        var goal = new Goal();
        var section = new Step { IsGroup = true };
        goal.Steps.Add(section);

        Assert.Null(PutOff.Split(goal, section));
    }

    [Fact]
    public void A_snoozed_goal_with_no_steps_leaves_the_plan_until_its_day()
    {
        var data = OrreryData.CreateEmpty();
        var goal = new Goal { Title = "Call the bank", CategoryId = data.CategoryFor(CategoryRole.Obligation).Id, Created = Today };
        data.Goals.Add(goal);
        PutOff.ForGoal(goal, Today);

        Assert.Empty(Planner.Suggest(data, new PlanRequest(Today)));
        Assert.Single(Planner.Suggest(data, new PlanRequest(Today.AddDays(1))));
    }
}

public class MoveToGoalTests
{
    [Fact]
    public void A_step_moves_with_everything_inside_it()
    {
        var from = new Goal { Title = "Biology" };
        var to = new Goal { Title = "Chemistry" };
        var unit = new Step { Title = "Unit 1", IsGroup = true };
        var read = new Step { Title = "Read", ParentId = unit.Id };
        var other = new Step { Title = "Other" };
        from.Steps.AddRange([unit, read, other]);

        Assert.True(from.MoveToGoal(to, unit));

        Assert.Equal(["Other"], from.Steps.Select(s => s.Title));
        Assert.Equal(["Unit 1", "Read"], to.Steps.Select(s => s.Title));
        Assert.Equal(unit.Id, read.ParentId);
    }

    [Fact]
    public void It_can_land_inside_a_section_of_the_other_goal()
    {
        var from = new Goal();
        var to = new Goal();
        var section = new Step { Title = "Week 1", IsGroup = true };
        var after = new Step { Title = "Later" };
        to.Steps.AddRange([section, after]);
        var step = new Step { Title = "Quiz" };
        from.Steps.Add(step);

        Assert.True(from.MoveToGoal(to, step, section.Id));

        Assert.Equal(["Week 1", "Quiz", "Later"], to.Steps.Select(s => s.Title));
        Assert.Equal(section.Id, step.ParentId);
    }

    [Fact]
    public void Dependencies_stay_attached()
    {
        var from = new Goal();
        var to = new Goal();
        var first = new Step { Title = "First" };
        var second = new Step { Title = "Second", DependsOn = [first.Id] };
        from.Steps.AddRange([first, second]);

        from.MoveToGoal(to, second);

        Assert.Equal([first.Id], second.DependsOn);
    }

    [Fact]
    public void Moving_into_the_same_goal_or_a_missing_section_is_refused()
    {
        var from = new Goal();
        var to = new Goal();
        var step = new Step();
        from.Steps.Add(step);

        Assert.False(from.MoveToGoal(from, step));
        Assert.False(from.MoveToGoal(to, step, Guid.NewGuid()));
        Assert.Single(from.Steps);
    }
}
