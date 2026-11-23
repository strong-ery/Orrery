using Orrery.Core.Design;
using Orrery.Core.Engine;
using Orrery.Core.Models;
using Orrery.Core.Storage;

namespace Orrery.Core.Tests;

public class CaptureTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Theory]
    [InlineData("tomorrow", 2026, 11, 24)]
    [InlineData("friday", 2026, 11, 27)]
    [InlineData("monday", 2026, 11, 30)] // today is a Monday, so "monday" means next week
    [InlineData("dec 10", 2026, 12, 10)]
    [InlineData("10 dec", 2026, 12, 10)]
    [InlineData("12/10", 2026, 12, 10)]
    [InlineData("in 2 weeks", 2026, 12, 7)]
    [InlineData("in 3 days", 2026, 11, 26)]
    [InlineData("jan 5", 2027, 1, 5)] // already past this year, so next year
    public void Dates_are_understood(string text, int y, int m, int d)
    {
        Assert.True(DateParser.TryParse(text, Monday, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Fact]
    public void A_step_goes_under_the_named_goal_and_waits_for_the_named_step()
    {
        var data = SampleData.Create(Monday);
        var p = QuickCapture.Parse(data, "Unit 5 in Biology after Unit 4 ~90m", Monday);

        Assert.Equal(CaptureKind.NewStep, p.Kind);
        Assert.Equal("Unit 5", p.Title);
        Assert.Equal("Biology 101", p.TargetGoal!.Title);
        Assert.Equal("Unit 4", p.After!.Title);
        Assert.Equal(90, p.Minutes);

        QuickCapture.Apply(data, p, Monday);
        var bio = data.Goals.First(g => g.Title == "Biology 101");
        Assert.Equal(5, bio.Steps.Count);
        Assert.Equal(bio.Steps[3].Id, Assert.Single(bio.Steps[4].DependsOn));
    }

    [Fact]
    public void The_goal_can_come_first_with_a_colon_and_the_after_step_can_imply_the_goal()
    {
        var data = SampleData.Create(Monday);
        var colon = QuickCapture.Parse(data, "Prism: wire up the thing !low", Monday);
        Assert.Equal("Finish Prism", colon.TargetGoal!.Title);
        Assert.Equal(Energy.Low, colon.Energy);

        var implied = QuickCapture.Parse(data, "Unit 5 after Unit 4", Monday);
        Assert.Equal("Biology 101", implied.TargetGoal!.Title);
    }

    [Fact]
    public void Plain_text_becomes_a_new_goal_and_the_words_decide_its_shape()
    {
        var data = SampleData.Create(Monday);

        var dated = QuickCapture.Parse(data, "Renew passport due dec 20", Monday);
        Assert.Equal(CaptureKind.NewGoal, dated.Kind);
        Assert.Equal(new DateOnly(2026, 12, 20), dated.Deadline);
        Assert.Equal("Renew passport", dated.Title);

        var daily = QuickCapture.Parse(data, "Walk the dog daily", Monday);
        Assert.Equal(CategoryRole.Routine, daily.Role);
        Assert.Equal(RhythmKind.Repeat, daily.Rhythm!.Kind);

        var weekly = QuickCapture.Parse(data, "Practice guitar 3h/week", Monday);
        Assert.Equal(CategoryRole.Passion, weekly.Role);
        Assert.Equal(180, weekly.Rhythm!.PerWeek);

        QuickCapture.Apply(data, dated, Monday);
        var goal = data.Goals.Last();
        Assert.Equal("Renew passport", goal.Title);
        Assert.Equal(new DateOnly(2026, 12, 20), goal.Deadline);
    }

    [Fact]
    public void Misreads_are_reported_rather_than_applied()
    {
        var data = SampleData.Create(Monday);
        Assert.NotNull(QuickCapture.Parse(data, "Nonexistent: do a thing", Monday).Problem);
        Assert.NotNull(QuickCapture.Parse(data, "Unit 9 in Biology after Unit 12", Monday).Problem);
        Assert.False(QuickCapture.Parse(data, "", Monday).CanApply);
        Assert.Throws<InvalidOperationException>(() =>
            QuickCapture.Apply(data, QuickCapture.Parse(data, "Nonexistent: do a thing", Monday), Monday));
    }

    [Fact]
    public void An_in_that_is_not_a_goal_stays_part_of_the_title()
    {
        var data = SampleData.Create(Monday);
        var p = QuickCapture.Parse(data, "Read the chapter in the textbook", Monday);
        Assert.Equal(CaptureKind.NewGoal, p.Kind);
        Assert.Equal("Read the chapter in the textbook", p.Title);
    }

    [Fact]
    public void Rings_follow_when_a_goal_will_want_you()
    {
        var data = SampleData.Create(Monday);
        Horizon Ring(string title) =>
            Horizons.Of(data.Goals.First(g => g.Title == title), data.Contributions, Monday);

        Assert.Equal(Horizon.Now, Ring("Do laundry"));
        Assert.Equal(Horizon.Week, Ring("Finish Prism"));
        Assert.Equal(Horizon.Month, Ring("Catch up on schoolwork"));
        Assert.Equal(Horizon.Month, Ring("Biology 101"));
    }

    [Fact]
    public void Editing_a_goal_keeps_its_steps_and_history()
    {
        var data = SampleData.Create(Monday);
        var bio = data.Goals.First(g => g.Title == "Biology 101");
        var answers = GoalDesigner.AnswersFrom(bio, data);
        answers.Title = "Biology 101 (fall)";
        answers.Deadline = Monday.AddDays(40);

        var designed = GoalDesigner.Build(data, answers, Monday).Goal!;
        GoalDesigner.ApplyTo(bio, designed);

        Assert.Equal("Biology 101 (fall)", bio.Title);
        Assert.Equal(Monday.AddDays(40), bio.Deadline);
        Assert.Equal(4, bio.Steps.Count);
    }

    [Fact]
    public void A_focus_session_counts_paused_time_out()
    {
        var start = new DateTime(2026, 11, 23, 10, 0, 0);
        var focus = new FocusState { StartedAt = start, PausedSeconds = 300 };
        Assert.Equal(TimeSpan.FromMinutes(25), focus.Elapsed(start.AddMinutes(30)));

        focus.PausedAt = start.AddMinutes(30);
        Assert.Equal(TimeSpan.FromMinutes(25), focus.Elapsed(start.AddMinutes(45)));
    }
}
