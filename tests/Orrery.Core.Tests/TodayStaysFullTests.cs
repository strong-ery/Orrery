using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;
using Xunit;

namespace Orrery.Core.Tests;

public class TodayStaysFullTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Fact]
    public void A_deadline_goal_stays_on_the_list_after_todays_share_is_done()
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Obligation);
        var goal = new Goal { Title = "Course", CategoryId = cat.Id, Created = Monday.AddDays(-20), Deadline = Monday.AddDays(20), Measure = Measure.Steps };
        for (var i = 1; i <= 6; i++)
            goal.Steps.Add(new Step { Title = $"Lesson {i}" });
        data.Goals.Add(goal);

        goal.Steps[0].Done = true;
        data.Contributions.Add(new Contribution { GoalId = goal.Id, StepId = goal.Steps[0].Id, Day = Monday, Amount = 1 });

        var plan = Planner.Suggest(data, new PlanRequest(Monday));
        Assert.Contains(plan, p => p.GoalId == goal.Id && p.StepId == goal.Steps[1].Id);
    }
}

public class TodaySuggestionTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    private static (OrreryData Data, Goal Goal) Course(GroupDateMode mode = GroupDateMode.Off)
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Obligation);
        var goal = new Goal { Title = "Course", CategoryId = cat.Id, Created = Monday.AddDays(-5), Deadline = Monday.AddDays(60), Measure = Measure.Steps, GroupDates = mode };
        foreach (var unit in new[] { "Unit 1", "Unit 2" })
        {
            var g = new Step { Title = unit, IsGroup = true };
            goal.Steps.Add(g);
            for (var i = 1; i <= 5; i++)
                goal.Steps.Add(new Step { Title = $"{unit} part {i}", ParentId = g.Id });
        }
        data.Goals.Add(goal);
        return (data, goal);
    }

    [Fact]
    public void Every_step_that_can_start_now_is_offered_up_to_a_few_per_goal()
    {
        var (data, goal) = Course();
        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 10)).Where(p => p.GoalId == goal.Id).ToList();
        Assert.Equal(Planner.PerGoal, plan.Count);
        Assert.Equal(["Unit 1 part 1", "Unit 1 part 2", "Unit 1 part 3"], plan.Select(p => p.Title).Order());
    }

    [Fact]
    public void A_late_group_outranks_a_calm_goal_and_says_so()
    {
        var (data, goal) = Course(GroupDateMode.Custom);
        goal.Steps.First(s => s.Title == "Unit 2").Due = Monday.AddDays(-2);
        var plan = Planner.Suggest(data, new PlanRequest(Monday, MaxItems: 5));
        var top = plan.First(p => p.GoalId == goal.Id);
        Assert.StartsWith("Unit 2 part", top.Title);
        Assert.Contains("Unit 2 was due", top.Reason);
        Assert.True(top.Pressure >= Planner.MustDo);
    }
}
