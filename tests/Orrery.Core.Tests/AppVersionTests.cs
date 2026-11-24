using Orrery.Core.Updates;
using Xunit;

namespace Orrery.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    [InlineData("nightly", "1.0.0", false)]
    [InlineData(null, "1.0.0", false)]
    public void Only_a_strictly_newer_tag_counts(string? tag, string current, bool newer) =>
        Assert.Equal(newer, AppVersion.IsNewer(tag, current));
}
