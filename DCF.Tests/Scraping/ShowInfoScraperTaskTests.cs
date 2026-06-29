using DCF.Api.Scraping;
using Xunit;

namespace DCF.Tests.Scraping;

public class ShowInfoScraperTaskTests
{
    private sealed class FakeHtmlFetcher(string html) : IHtmlFetcher
    {
        public Task<string> FetchAsync(string url)
        {
            return Task.FromResult(html);
        }
    }

    private sealed class ThrowingFetcher : IHtmlFetcher
    {
        public Task<string> FetchAsync(string url)
        {
            throw new HttpRequestException("network error");
        }
    }

    private static ShowInfoScraperTask CreateScraper(string html)
    {
        return new ShowInfoScraperTask(new FakeHtmlFetcher(html));
    }

    private const string ExhibitionHtml = """
        <html><body>
        <strong>NON-COMPETITION FORMAT: </strong>
        <div class="lineup-times-table">
          <p>All times PT and subject to change</p>
          <table><tbody>
            <tr><td>7:00 PM</td><td><strong>Gates Open</strong></td></tr>
            <tr><td>7:50 PM</td><td><strong>Welcome &amp; National Anthem</strong></td></tr>
            <tr><td>8:00 PM</td><td><strong>Golden Empire</strong> - Bakersfield, CA</td></tr>
            <tr><td>8:25 PM</td><td><strong>Blue Devils</strong> - Concord, CA</td></tr>
            <tr><td>10:00 PM</td><td><strong>Event Concludes</strong></td></tr>
          </tbody></table>
        </div>
        <div class="address-info">
          <address>Camarillo High School Stadium<br>4660 Mission Oaks Blvd<br>Camarillo, CA 93012</address>
        </div>
        <a href="https://maps.google.com/?q=34.2228,-119.0307" target="_blank">Get Directions</a>
        </body></html>
        """;

    private const string CompetitiveHtml = """
        <html><body>
        <div class="lineup-times-table">
          <p>All times ET and subject to change</p>
          <table><tbody>
            <tr><td>7:00 PM</td><td><strong>Gates Open</strong></td></tr>
            <tr><td>8:00 PM</td><td><strong>Blue Devils</strong> - Concord, CA</td></tr>
            <tr><td>8:25 PM</td><td><strong>Bluecoats</strong> - Canton, OH</td></tr>
            <tr><td>9:30 PM</td><td><strong>Retreat</strong></td></tr>
            <tr><td>9:45 PM</td><td><strong>Scores Announced</strong></td></tr>
          </tbody></table>
        </div>
        <div class="address-info">
          <address>Lucas Oil Stadium<br>500 S Capitol Ave<br>Indianapolis, IN 46225</address>
        </div>
        <a href="https://www.google.com/maps/search/?api=1&query=39.7684%2C-86.1581" target="_blank">Map</a>
        </body></html>
        """;

    [Fact]
    public async Task ScrapeAsync_ExhibitionShow_SetsIsExhibitionTrue()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.True(result.IsExhibition);
    }

    [Fact]
    public async Task ScrapeAsync_CompetitiveShow_SetsIsExhibitionFalse()
    {
        var scraper = CreateScraper(CompetitiveHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.NotNull(result);
        Assert.False(result.IsExhibition);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLocationFromAddressElement()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.Contains("Camarillo High School Stadium", result.Location);
        Assert.Contains("Camarillo, CA 93012", result.Location);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_QParam()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.Equal(34.2228, result.Latitude!.Value, precision: 4);
        Assert.Equal(-119.0307, result.Longitude!.Value, precision: 4);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_QueryParam()
    {
        var scraper = CreateScraper(CompetitiveHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.NotNull(result);
        Assert.Equal(39.7684, result.Latitude!.Value, precision: 4);
        Assert.Equal(-86.1581, result.Longitude!.Value, precision: 4);
    }

    [Fact]
    public async Task ScrapeAsync_NoGoogleMapsLink_LatLngNull()
    {
        const string html = """
            <html><body>
            <div class="lineup-times-table">
              <p>All times ET</p>
              <table><tbody>
                <tr><td>8:00 PM</td><td><strong>Blue Devils</strong> - Concord, CA</td></tr>
              </tbody></table>
            </div>
            </body></html>
            """;
        var scraper = CreateScraper(html);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.NotNull(result);
        Assert.Null(result.Latitude);
        Assert.Null(result.Longitude);
    }

    [Fact]
    public async Task ScrapeAsync_DetectsTimezone_FromAllTimesText()
    {
        var scraper = CreateScraper(CompetitiveHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.Equal("ET", result!.Timezone);
    }

    [Fact]
    public async Task ScrapeAsync_ExhibitionShow_StartTimeIsFirstNonGateEntry()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.Equal("19:50", result!.StartTime);
    }

    [Fact]
    public async Task ScrapeAsync_FiltersGatesOpenFromSchedule()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.DoesNotContain(result!.ScheduleEntries, e => e.Label.Equals("Gates Open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScrapeAsync_ScheduleRetainsAllNonGateEntries()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.Equal(4, result!.ScheduleEntries.Count);
        Assert.Equal("Welcome & National Anthem", result.ScheduleEntries[0].Label);
        Assert.Equal("Golden Empire", result.ScheduleEntries[1].Label);
    }

    [Fact]
    public async Task ScrapeAsync_CompetitiveShow_ParsesScoresAnnouncedTime()
    {
        var scraper = CreateScraper(CompetitiveHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.Equal("21:45", result!.ScoresAnnouncedTime);
    }

    [Fact]
    public async Task ScrapeAsync_FetcherThrows_ReturnsNull()
    {
        var scraper = new ShowInfoScraperTask(new ThrowingFetcher());

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.Null(result);
    }

    [Fact]
    public async Task ScrapeAsync_ConvertsPmTimeTo24h()
    {
        var scraper = CreateScraper(CompetitiveHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.Equal("20:00", result!.StartTime);
    }
}
