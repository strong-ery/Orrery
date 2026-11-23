using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;
using Xunit;

namespace Orrery.Core.Tests;

public class TimelineTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Fact]
    public void Each_bucket_gets_a_lane_and_empty_ones_are_left_out()
    {
        var data = SampleData.Create(Monday);

        var lanes = TimelineModel.Build(data, Monday, 60);

        Assert.NotEmpty(lanes);
        Assert.All(lanes, l => Assert.NotEmpty(l.Bars));
        Assert.Equal(lanes.Count, lanes.Select(l => l.Role).Distinct().Count());
    }

    [Fact]
    public void Goals_with_a_deadline_come_before_open_ended_ones_soonest_first()
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Obligation);
        data.Goals.Add(new Goal { Title = "later", CategoryId = cat.Id, Deadline = Monday.AddDays(30), Created = Monday });
        data.Goals.Add(new Goal { Title = "open", CategoryId = cat.Id, Created = Monday });
        data.Goals.Add(new Goal { Title = "soon", CategoryId = cat.Id, Deadline = Monday.AddDays(5), Created = Monday });

        var lane = Assert.Single(TimelineModel.Build(data, Monday, 60));

        Assert.Equal(["soon", "later", "open"], lane.Bars.Select(b => b.Title));
    }

    [Fact]
    public void The_next_step_skips_what_is_blocked_and_planned_days_are_listed()
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Passion);
        var goal = new Goal { Title = "Prism", CategoryId = cat.Id, Created = Monday };
        goal.Steps.Add(new Step { Title = "locked", BlockedUntil = Monday.AddDays(10) });
        goal.Steps.Add(new Step { Title = "go", PlannedFor = Monday.AddDays(2) });
        data.Goals.Add(goal);

        var bar = Assert.Single(Assert.Single(TimelineModel.Build(data, Monday, 60)).Bars);

        Assert.Equal("go", bar.Next);
        Assert.False(bar.NextIsWaiting);
        Assert.Equal([Monday.AddDays(2)], bar.Planned);
    }

    [Fact]
    public void Finished_and_put_away_goals_are_not_on_the_timeline()
    {
        var data = OrreryData.CreateEmpty();
        var cat = data.Categories.First(c => c.Role == CategoryRole.Passion);
        data.Goals.Add(new Goal { Title = "away", CategoryId = cat.Id, Created = Monday, Archived = true });

        Assert.Empty(TimelineModel.Build(data, Monday, 60));
    }
}
