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
    public void Dropping_inside_the_same_group_puts_the_item_where_it_was_dropped()
    {
        var a = Item("a", Duty, 5);
        var b = Item("b", Duty, 4);
        var c = Item("c", Chore, 3);
        var game = Item("game", Want, 1);
        var arranged = PlanOrder.Arrange([a, b, c, game], []);

        var keys = PlanOrder.MoveTo(arranged, PlanOrder.KeyOf(c), 0);

        Assert.Equal([PlanOrder.KeyOf(c), PlanOrder.KeyOf(a), PlanOrder.KeyOf(b), PlanOrder.KeyOf(game)], keys);
    }

    [Fact]
    public void Dropping_past_the_other_group_stops_at_the_edge_of_its_own()
    {
        var a = Item("a", Duty, 5);
        var b = Item("b", Duty, 4);
        var game = Item("game", Want, 1);
        var more = Item("more", Want, 0.5);
        var arranged = PlanOrder.Arrange([a, b, game, more], []);

        Assert.Equal(1, PlanOrder.ClampTarget(arranged, PlanOrder.KeyOf(a), 3));
        Assert.Equal(2, PlanOrder.ClampTarget(arranged, PlanOrder.KeyOf(more), 0));
        Assert.Null(PlanOrder.MoveTo(arranged, PlanOrder.KeyOf(game), 0));
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
