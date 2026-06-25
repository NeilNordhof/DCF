using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class DraftSchedulerServiceTests
{
    [Fact]
    public void GetDraftDelay_SubtractsLeadTimeFromStart()
    {
        var now = new DateTimeOffset(2025, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var delay = DraftSchedulerService.GetDraftDelay(start, TimeSpan.FromMinutes(30), now);

        Assert.Equal(TimeSpan.FromMinutes(90), delay);
    }

    [Fact]
    public void GetDraftDelay_ZeroLeadTime_ReturnsTimeToStart()
    {
        var now = new DateTimeOffset(2025, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var delay = DraftSchedulerService.GetDraftDelay(start, TimeSpan.Zero, now);

        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void GetDraftDelay_PastStartTime_ReturnsNegative()
    {
        var now = new DateTimeOffset(2025, 8, 1, 14, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var delay = DraftSchedulerService.GetDraftDelay(start, TimeSpan.Zero, now);

        Assert.True(delay < TimeSpan.Zero);
    }

    [Fact]
    public void GetDraftDelay_LeadTimeLargerThanTimeToStart_ReturnsNegative()
    {
        var now = new DateTimeOffset(2025, 8, 1, 11, 30, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2025, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var delay = DraftSchedulerService.GetDraftDelay(start, TimeSpan.FromHours(1), now);

        Assert.True(delay < TimeSpan.Zero);
    }
}
