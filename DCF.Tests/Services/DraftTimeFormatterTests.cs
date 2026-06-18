using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class DraftTimeFormatterTests
{
    [Fact]
    public void Format_NullTimezone_ReturnsUtcString()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, null);

        Assert.Equal("Tuesday, June 16 at 11:00 PM UTC", result);
    }

    [Fact]
    public void Format_EmptyTimezone_ReturnsUtcString()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "");

        Assert.Equal("Tuesday, June 16 at 11:00 PM UTC", result);
    }

    [Fact]
    public void Format_EasternInSummer_ReturnsDaylightAbbreviation()
    {
        // 2026-06-16 23:00 UTC = 2026-06-16 19:00 EDT (UTC-4, DST active)
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "America/New_York");

        Assert.Equal("Tuesday, June 16 at 7:00 PM EDT", result);
    }

    [Fact]
    public void Format_EasternInWinter_ReturnsStandardAbbreviation()
    {
        // 2026-01-16 23:00 UTC = 2026-01-16 18:00 EST (UTC-5, no DST)
        var utcTime = new DateTimeOffset(2026, 1, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "America/New_York");

        Assert.Equal("Friday, January 16 at 6:00 PM EST", result);
    }

    [Fact]
    public void Format_InvalidTimezone_ReturnsUtcFallback()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "Not/A/Real/Zone");

        Assert.Equal("Tuesday, June 16 at 11:00 PM UTC", result);
    }
}
