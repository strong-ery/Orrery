using Orrery.Core.Engine;
using Xunit;

namespace Orrery.Core.Tests;

public class StepTextTests
{
    [Theory]
    [InlineData("Write up notes 30m", "Write up notes", 30)]
    [InlineData("Read chapter 1h", "Read chapter", 60)]
    [InlineData("Read chapter 1.5 hours", "Read chapter", 90)]
    [InlineData("Practice 1h30", "Practice", 90)]
    [InlineData("Tidy desk ~15 min", "Tidy desk", 15)]
    public void Reads_a_trailing_time(string typed, string title, int minutes) =>
        Assert.Equal((title, (int?)minutes), StepText.Parse(typed));

    [Theory]
    [InlineData("Call mom at 5")]
    [InlineData("30m")]
    [InlineData("Plan the 3 month review")]
    public void Leaves_everything_else_alone(string typed) =>
        Assert.Equal((typed, (int?)null), StepText.Parse(typed));
}
