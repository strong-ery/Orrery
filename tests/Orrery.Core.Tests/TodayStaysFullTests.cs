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
