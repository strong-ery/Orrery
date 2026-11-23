using Orrery.Core.Design;
using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.Core.Tests;

public class SectionTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static (OrreryData Data, Goal Bio, DependencyGraph Graph) Bio()
    {
        var data = SampleData.Create(Monday);
        var bio = data.Goals.First(g => g.Title == "Biology 101");
        return (data, bio, new DependencyGraph(data.Goals, g => Progress.IsComplete(g, data.Contributions)));
    }

    [Fact]
    public void Only_the_steps_inside_sections_are_counted_and_offered()
    {
        var (data, bio, _) = Bio();
        Assert.Equal(12, Progress.Target(bio));
        var plan = Planner.Suggest(data, new PlanRequest(Monday));
        var item = plan.First(i => i.GoalId == bio.Id);
        Assert.Equal("Read the chapter", item.Title);
    }

    [Fact]
    public void A_section_is_done_only_when_everything_inside_it_is()
    {
        var (_, bio, _) = Bio();
        var unit1 = bio.Steps.First(s => s.Title == "Unit 1");
        var kids = bio.ChildrenOf(unit1).ToList();
        kids[0].Done = true; kids[1].Done = true;
        Assert.False(bio.IsDone(unit1));
        kids[2].Done = true;
        Assert.True(bio.IsDone(unit1));
    }

    [Fact]
    public void A_later_unit_stays_blocked_until_the_earlier_unit_is_finished()
    {
        var (_, bio, graph) = Bio();
        var unit2Step = bio.ChildrenOf(bio.Steps.First(s => s.Title == "Unit 2")).First();
        Assert.Contains("Unit 1", graph.BlockedReason(unit2Step, Monday));

        foreach (var s in bio.ChildrenOf(bio.Steps.First(s => s.Title == "Unit 1")))
            s.Done = true;
        Assert.Null(graph.BlockedReason(unit2Step, Monday));
    }

    [Fact]
    public void A_section_estimate_is_the_sum_of_its_steps()
    {
        var (_, bio, _) = Bio();
        Assert.Equal(90, bio.EstimateOf(bio.Steps.First(s => s.Title == "Unit 1")));
    }

    [Fact]
    public void A_step_can_be_added_inside_a_section_by_name()
    {
        var (data, bio, _) = Bio();
        var p = QuickCapture.Parse(data, "Lab write-up in Unit 2 ~50m", Monday);
        Assert.True(p.CanApply, p.Problem);
        QuickCapture.Apply(data, p, Monday);
        var unit2 = bio.Steps.First(s => s.Title == "Unit 2");
        Assert.Contains(bio.ChildrenOf(unit2), s => s.Title == "Lab write-up" && s.EstimateMinutes == 50);
    }

    [Fact]
    public void Removing_a_section_removes_its_steps()
    {
        var (_, bio, _) = Bio();
        bio.RemoveStep(bio.Steps.First(s => s.Title == "Unit 4").Id);
        Assert.Equal(12, bio.Steps.Count);
    }

    [Fact]
    public void An_empty_section_is_not_done_and_not_offered()
    {
        var (_, bio, _) = Bio();
        var empty = new Step { Title = "Final exam", IsGroup = true };
        bio.Steps.Add(empty);
        Assert.False(bio.IsDone(empty));
        Assert.DoesNotContain(empty, bio.Leaves());
    }
}

public class FlowGraphTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void Units_form_a_left_to_right_chain_and_unconnected_goals_are_left_out()
    {
        var data = SampleData.Create(Monday);
        var flow = FlowGraph.Build(data, Monday, g => Progress.IsComplete(g, data.Contributions));

        var bio = flow.Nodes.Where(n => n.GoalTitle == "Biology 101").OrderBy(n => n.Column).ToList();
        Assert.Equal(4, bio.Count);
        Assert.Equal([0, 1, 2, 3], bio.Select(n => n.Column));
        Assert.Equal(NodeState.Open, bio[0].State);
        Assert.All(bio.Skip(1), n => Assert.Equal(NodeState.Blocked, n.State));
        Assert.DoesNotContain(flow.Nodes, n => n.GoalTitle == "Do laundry");
    }

    [Fact]
    public void A_goal_that_waits_for_another_hangs_off_its_last_step()
    {
        var data = SampleData.Create(Monday);
        var bio = data.Goals.First(g => g.Title == "Biology 101");
        var school = data.Goals.First(g => g.Title.StartsWith("Catch up"));
        bio.DependsOnGoals.Add(school.Id);
        var flow = FlowGraph.Build(data, Monday, g => Progress.IsComplete(g, data.Contributions));

        var schoolNode = flow.Nodes.Single(n => n.GoalTitle == school.Title);
        var unit1 = flow.Nodes.Single(n => n.Title == "Unit 1");
        Assert.Contains(flow.Edges, e => e.From == schoolNode.Id && e.To == unit1.Id);
        Assert.True(unit1.Column > schoolNode.Column);
    }
}

public class NudgeTests
{
    private static readonly DateTime Evening = new(2026, 3, 2, 18, 0, 0);

    [Fact]
    public void Nothing_nags_in_the_morning_or_late_at_night()
    {
        var data = SampleData.Create(DateOnly.FromDateTime(Evening));
        Assert.Empty(Nudges.Due(data, Evening.Date.AddHours(8)));
        Assert.Empty(Nudges.Due(data, Evening.Date.AddHours(22)));
    }

    [Fact]
    public void Something_due_today_is_nudged_in_the_evening_then_left_alone()
    {
        var data = SampleData.Create(DateOnly.FromDateTime(Evening));
        var first = Nudges.Due(data, Evening);
        Assert.Contains(first, n => n.Message.Contains("Do laundry"));

        foreach (var n in first)
            data.Settings.NudgeLog[n.GoalId] = Evening;
        Assert.DoesNotContain(Nudges.Due(data, Evening.AddMinutes(30)), n => n.Message.Contains("Do laundry"));
    }

    [Fact]
    public void Turning_nudges_off_silences_everything()
    {
        var data = SampleData.Create(DateOnly.FromDateTime(Evening));
        data.Settings.Nudges = false;
        Assert.Empty(Nudges.Due(data, Evening));
    }

    [Fact]
    public void The_daily_cap_holds()
    {
        var data = SampleData.Create(DateOnly.FromDateTime(Evening));
        for (var i = 0; i < 4; i++)
            data.Settings.NudgeLog[Guid.NewGuid()] = Evening.AddHours(-1);
        Assert.Empty(Nudges.Due(data, Evening));
    }
}

public class StepEditingTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static Goal Bio() => SampleData.Create(Monday).Goals.First(g => g.Title == "Biology 101");

    [Fact]
    public void A_section_moves_up_with_everything_in_it()
    {
        var bio = Bio();
        var unit2 = bio.Steps.First(s => s.Title == "Unit 2");
        Assert.True(bio.MoveUp(unit2));
        var roots = bio.Roots().Select(s => s.Title).ToList();
        Assert.Equal(["Unit 2", "Unit 1", "Unit 3", "Unit 4"], roots);
        Assert.Equal(16, bio.Steps.Count);
        Assert.Equal(3, bio.ChildrenOf(unit2).Count());
        // the children stay directly after their section
        Assert.Equal(unit2.Id, bio.Steps[1].ParentId);
    }

    [Fact]
    public void The_first_step_cannot_move_up_and_the_last_cannot_move_down()
    {
        var bio = Bio();
        Assert.False(bio.MoveUp(bio.Steps.First(s => s.Title == "Unit 1")));
        Assert.False(bio.MoveDown(bio.Steps.First(s => s.Title == "Unit 4")));
    }

    [Fact]
    public void A_step_can_move_into_another_section_but_not_into_itself()
    {
        var bio = Bio();
        var unit1 = bio.Steps.First(s => s.Title == "Unit 1");
        var unit2 = bio.Steps.First(s => s.Title == "Unit 2");
        var notes = bio.ChildrenOf(unit1).First(s => s.Title == "Write up notes");
        Assert.True(bio.MoveTo(notes, unit2.Id));
        Assert.Equal(2, bio.ChildrenOf(unit1).Count());
        Assert.Equal(4, bio.ChildrenOf(unit2).Count());
        Assert.False(bio.MoveTo(unit1, unit1.Id));
        Assert.False(bio.MoveTo(unit1, bio.ChildrenOf(unit1).First().Id));
    }
}

public class CalendarTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void Deadlines_and_repeats_land_on_the_right_days()
    {
        var data = SampleData.Create(Monday);
        var events = CalendarModel.Events(data, Monday, Monday.AddDays(30), Monday);

        var school = data.Goals.First(g => g.Title.StartsWith("Catch up"));
        Assert.Contains(events, e => e.Kind == CalendarKind.Deadline && e.GoalId == school.Id && e.Day == school.Deadline);

        // laundry: created 3 days ago, every 7 days, so due on day 6 of its window (3 days from now), then weekly
        var laundry = events.Where(e => e.Kind == CalendarKind.Due).Select(e => e.Day).ToList();
        Assert.Equal(Monday.AddDays(3), laundry[0]);
        Assert.Equal(7, laundry[1].DayNumber - laundry[0].DayNumber);
    }

    [Fact]
    public void A_repeat_that_is_already_done_this_window_is_not_shown_as_due()
    {
        var data = SampleData.Create(Monday);
        var laundry = data.Goals.First(g => g.Title == "Do laundry");
        data.Contributions.Add(new Contribution { GoalId = laundry.Id, Day = Monday, Amount = 1 });
        var due = CalendarModel.Events(data, Monday, Monday.AddDays(10), Monday).Where(e => e.Kind == CalendarKind.Due).ToList();
        Assert.DoesNotContain(due, e => e.Day == Monday.AddDays(3));
        Assert.Contains(due, e => e.Day == Monday.AddDays(10));
    }

    [Fact]
    public void Past_work_shows_up_as_worked_and_held_back_steps_show_when_they_open()
    {
        var data = SampleData.Create(Monday);
        var prism = data.Goals.First(g => g.Title == "Finish Prism");
        data.Contributions.Add(new Contribution { GoalId = prism.Id, Day = Monday.AddDays(-2), Amount = 40, Minutes = 40 });
        prism.Steps.First(s => s.Title == "Parse the header").BlockedUntil = Monday.AddDays(5);

        var events = CalendarModel.Events(data, Monday.AddDays(-7), Monday.AddDays(7), Monday);
        Assert.Contains(events, e => e.Kind == CalendarKind.Worked && e.Day == Monday.AddDays(-2) && e.Detail == "40 min");
        Assert.Contains(events, e => e.Kind == CalendarKind.Available && e.Day == Monday.AddDays(5) && e.Title == "Parse the header");
    }
}
