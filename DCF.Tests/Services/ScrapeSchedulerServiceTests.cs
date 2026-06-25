using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class ScrapeSchedulerServiceTests
{
    [Fact]
    public void GetScrapeDelay_AddsDelayMinutesToAnnouncedTime()
    {
        var now = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 10, now);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void GetScrapeDelay_FutureShow_ReturnsPositiveDelay()
    {
        var now = new DateTimeOffset(2025, 7, 1, 20, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 5, now);

        Assert.Equal(TimeSpan.FromMinutes(125), delay);
    }

    [Fact]
    public void GetScrapeDelay_PastShow_ReturnsNegativeDelay()
    {
        var now = new DateTimeOffset(2025, 7, 1, 23, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 0, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 5, now);

        Assert.True(delay < TimeSpan.Zero);
    }

    [Fact]
    public void GetScrapeDelay_ZeroDelayMinutes_ReturnsExactTimeToAnnouncement()
    {
        var now = new DateTimeOffset(2025, 7, 1, 20, 0, 0, TimeSpan.Zero);
        var announced = new DateTimeOffset(2025, 7, 1, 22, 30, 0, TimeSpan.Zero);

        var delay = ScrapeSchedulerService.GetScrapeDelay(announced, 0, now);

        Assert.Equal(TimeSpan.FromMinutes(150), delay);
    }
}
