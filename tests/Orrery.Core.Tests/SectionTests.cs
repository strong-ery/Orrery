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
