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
        <div class="inner-hero-inner">
          <p>Thursday, July 2, 2026 7:50 PM</p>
          <h1>MidCal Showcase</h1>
          <span class="location">Camarillo, CA</span>
        </div>
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
        <a href="https://www.google.com/maps/dir/Current+Location/34.2228,-119.0307" target="_blank">Get Directions</a>
        </body></html>
        """;

    private const string CompetitiveHtml = """
        <html><body>
        <div class="inner-hero-inner">
          <p>Saturday, August 15, 2026 7:00 PM</p>
          <h1>Test Show</h1>
          <span class="location">Indianapolis, IN</span>
        </div>
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
        <a href="https://www.google.com/maps/dir/Current+Location/39.7684,-86.1581" target="_blank">Get Directions</a>
        </body></html>
        """;

    private const string CompetitiveWithTbdHtml = """
        <html><body>
        <div class="inner-hero-inner">
          <p>Saturday, August 15, 2026 1:30 PM</p>
          <h1>Test Championship</h1>
          <span class="location">San Antonio, TX</span>
        </div>
        <div class="lineup-times-table">
          <p>All times CT and subject to change</p>
          <table><tbody>
            <tr><td>12:00 PM</td><td><strong>Gates Open</strong></td></tr>
            <tr><td>1:40 PM</td><td><strong>Guardians</strong> - McKinney, TX</td></tr>
            <tr><td>10:11 PM</td><td><strong>Scores Announced</strong></td></tr>
            <tr><td>TBD</td><td><strong>Blue Devils</strong> - Concord, CA</td></tr>
            <tr><td>TBD</td><td><strong>Bluecoats</strong> - Canton, OH</td></tr>
          </tbody></table>
        </div>
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
    public async Task ScrapeAsync_ParsesLocationFromHeroSection()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.Equal("Camarillo, CA", result.Location);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_QParam()
    {
        const string html = """
            <html><body>
            <a href="https://maps.google.com/?q=34.2228,-119.0307">Directions</a>
            </body></html>
            """;
        var scraper = CreateScraper(html);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.NotNull(result);
        Assert.Equal(34.2228, result.Latitude!.Value, precision: 4);
        Assert.Equal(-119.0307, result.Longitude!.Value, precision: 4);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_QueryParam()
    {
        const string html = """
            <html><body>
            <a href="https://www.google.com/maps/search/?api=1&query=39.7684%2C-86.1581">Map</a>
            </body></html>
            """;
        var scraper = CreateScraper(html);

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
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_DirFormat()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.Equal(34.2228, result.Latitude!.Value, precision: 4);
        Assert.Equal(-119.0307, result.Longitude!.Value, precision: 4);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesLatLngFromGoogleMapsLink_AtFormat()
    {
        const string html = """
            <html><body>
            <a href="https://www.google.com/maps/place/Lucas+Oil+Stadium/@39.7684,-86.1581,17z">Stadium</a>
            </body></html>
            """;
        var scraper = CreateScraper(html);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test/");

        Assert.NotNull(result);
        Assert.Equal(39.7684, result.Latitude!.Value, precision: 4);
        Assert.Equal(-86.1581, result.Longitude!.Value, precision: 4);
    }

    [Fact]
    public async Task ScrapeAsync_ParsesDateFromHeroSection()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.NotNull(result);
        Assert.Equal("2026-07-02", result.Date);
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

    [Fact]
    public async Task ScrapeAsync_TbdRows_AreKeptInScheduleNotDropped()
    {
        var scraper = CreateScraper(CompetitiveWithTbdHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test-championship/");

        Assert.NotNull(result);
        Assert.Equal(4, result!.ScheduleEntries.Count);
        Assert.Contains(result.ScheduleEntries, e => e.Label == "Blue Devils");
        Assert.Contains(result.ScheduleEntries, e => e.Label == "Bluecoats");
    }

    [Fact]
    public async Task ScrapeAsync_TbdRows_HaveNullTime24h()
    {
        var scraper = CreateScraper(CompetitiveWithTbdHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-test-championship/");

        var tbdEntry = result!.ScheduleEntries.Single(e => e.Label == "Blue Devils");
        var timedEntry = result.ScheduleEntries.Single(e => e.Label == "Guardians");

        Assert.Null(tbdEntry.Time24h);
        Assert.Equal("13:40", timedEntry.Time24h);
    }

    [Fact]
    public async Task ScrapeAsync_ExhibitionShow_ScoresAnnouncedTimeParsesFromEventConcludesLabel()
    {
        var scraper = CreateScraper(ExhibitionHtml);

        var result = await scraper.ScrapeAsync("https://www.dci.org/events/2026-midcal-showcase/");

        Assert.Equal("22:00", result!.ScoresAnnouncedTime);
    }
}
