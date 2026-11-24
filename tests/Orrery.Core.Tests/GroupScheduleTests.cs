using Orrery.Core.Engine;
using Orrery.Core.Models;
using Xunit;

namespace Orrery.Core.Tests;

public class GroupScheduleTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    private static Goal Course(GroupDateMode mode, DateOnly? deadline)
    {
        var goal = new Goal { Deadline = deadline, GroupDates = mode };
        foreach (var name in new[] { "Unit 1", "Unit 2", "Unit 3", "Unit 4" })
        {
            var unit = new Step { Title = name, IsGroup = true };
            goal.Steps.Add(unit);
            goal.Steps.Add(new Step { Title = "Read", ParentId = unit.Id });
        }
        return goal;
    }

    [Fact]
    public void Even_dates_spread_the_groups_to_the_deadline()
    {
        var goal = Course(GroupDateMode.Even, Today.AddDays(40));
        var dates = GroupSchedule.Dates(goal, Today).Select(d => d.Due).ToList();
        Assert.Equal([Today.AddDays(10), Today.AddDays(20), Today.AddDays(30), Today.AddDays(40)], dates);
    }

    [Fact]
    public void Finishing_a_group_respreads_what_is_left()
    {
        var goal = Course(GroupDateMode.Even, Today.AddDays(30));
        goal.Steps.First(s => s.Title == "Read").Done = true; // Unit 1 is now complete
        var dates = GroupSchedule.Dates(goal, Today);
        Assert.Equal(3, dates.Count);
        Assert.Equal(Today.AddDays(30), dates[^1].Due);
        Assert.DoesNotContain(dates, d => d.Group.Title == "Unit 1");
    }

    [Fact]
    public void Even_dates_need_a_deadline()
    {
        Assert.Empty(GroupSchedule.Dates(Course(GroupDateMode.Even, null), Today));
    }

    [Fact]
    public void Custom_dates_are_the_ones_you_set_and_off_means_none()
    {
        var goal = Course(GroupDateMode.Custom, Today.AddDays(40));
        goal.Steps.First(s => s.Title == "Unit 2").Due = Today.AddDays(12);
        var only = Assert.Single(GroupSchedule.Dates(goal, Today));
        Assert.Equal(("Unit 2", Today.AddDays(12)), (only.Group.Title, only.Due));
        goal.GroupDates = GroupDateMode.Off;
        Assert.Empty(GroupSchedule.Dates(goal, Today));
    }
}
