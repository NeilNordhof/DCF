using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class DraftServiceTests
{
    [Fact]
    public void GetCurrentDrafter_Round0Pick0_ReturnsFirstUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 0));
    }

    [Fact]
    public void GetCurrentDrafter_Round0Pick2_ReturnsLastUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 2));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick0_ReturnsLastUser()
    {
        // Round 1 (second round) snakes: C, B, A
        var order = new[] { "a", "b", "c" };
        Assert.Equal("c", DraftService.GetCurrentDrafter(order, 3));
    }

    [Fact]
    public void GetCurrentDrafter_Round1Pick1_ReturnsMiddleUser()
    {
        var order = new[] { "a", "b", "c" };
        Assert.Equal("b", DraftService.GetCurrentDrafter(order, 4));
    }

    [Fact]
    public void GetCurrentDrafter_Round2Pick0_ReturnsFirstUser()
    {
        // Round 2 (third round) snakes forward again: A, B, C
        var order = new[] { "a", "b", "c" };
        Assert.Equal("a", DraftService.GetCurrentDrafter(order, 6));
    }
}
