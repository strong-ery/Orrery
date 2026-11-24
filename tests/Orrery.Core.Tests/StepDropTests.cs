using Orrery.Core.Models;
using Xunit;

namespace Orrery.Core.Tests;

public class StepDropTests
{
    private static (Goal Goal, Step A, Step B, Step C) Flat()
    {
        Step S(string t) => new() { Title = t };
        var (a, b, c) = (S("A"), S("B"), S("C"));
        return (new Goal { Steps = [a, b, c] }, a, b, c);
    }

    [Fact]
    public void Dropping_onto_a_step_makes_it_a_group()
    {
        var (g, a, b, _) = Flat();
        Assert.True(g.Relocate(a, b, DropPlace.Inside));
        Assert.True(b.IsGroup);
        Assert.Equal(b.Id, a.ParentId);
        Assert.True(g.IsSection(b));
    }

    [Fact]
    public void Before_and_after_reorder()
    {
        var (g, a, b, c) = Flat();
        g.Relocate(c, a, DropPlace.Before);
        Assert.Equal(["C", "A", "B"], g.Steps.Select(s => s.Title));
        g.Relocate(c, b, DropPlace.After);
        Assert.Equal(["A", "B", "C"], g.Steps.Select(s => s.Title));
    }

    [Fact]
    public void An_emptied_group_becomes_a_plain_step_again()
    {
        var (g, a, b, c) = Flat();
        g.Relocate(a, b, DropPlace.Inside);
        g.Relocate(a, c, DropPlace.After);
        Assert.False(b.IsGroup);
        Assert.Null(a.ParentId);
        Assert.False(g.IsSection(b));
    }

    [Fact]
    public void A_group_moves_with_its_contents_and_cannot_enter_itself()
    {
        var (g, a, b, c) = Flat();
        g.Relocate(a, b, DropPlace.Inside);
        Assert.False(g.Relocate(b, a, DropPlace.Inside));
        Assert.True(g.Relocate(b, c, DropPlace.Inside));
        Assert.Equal(["C", "B", "A"], g.Steps.Select(s => s.Title));
        Assert.Equal(b.Id, a.ParentId);
    }
}
