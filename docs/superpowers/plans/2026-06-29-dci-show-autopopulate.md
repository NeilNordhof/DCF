# DCI Show Auto-Populate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Fetch from DCI" button to the admin show creation form that scrapes the DCI events page, extracts event times, competing corps, location, and lat/lng from the embedded Google Maps link, and pre-populates show fields — including a new full schedule, location, and exhibition flag.

**Architecture:** A new `ShowInfoScraperTask` scrapes `https://www.dci.org/events/{year}-{slug}/` using existing `IHtmlFetcher`/HtmlAgilityPack infrastructure. It also extracts lat/lng coordinates from the Google Maps link embedded in the DCI events page — no external geocoding API needed. A new prefill endpoint `GET /api/admin/seasons/{seasonId}/shows/prefill?name={name}` orchestrates scraping + corps matching and returns pre-populated data to the frontend. `ShowEntity` gains nullable `Url`, nullable `ScoresAnnouncedTime`, `IsExhibition`, `Location`, `Latitude`, `Longitude`, and a new `ShowScheduleEntryEntity` join table.

**Tech Stack:** .NET 10 / EF Core / HtmlAgilityPack / xUnit (backend); React 19 / TypeScript / Vite (frontend).

## Global Constraints

- C#: curly brackets on new line; no lambdas for methods; wrap single-line blocks; 1 blank line before `return`, before/after `await`, before/after code blocks; never more than 1 blank line in a row
- TypeScript: `const` by default; template literals; destructure when clearer; 1 blank line before `return`, before/after blocks and `await`; never more than 1 blank line in a row
- Follow existing file/class conventions exactly — match the project's existing patterns
- TDD: write the failing test first, run it, then implement

---

## File Map

**Created:**
- `DCF.Data/Entities/ShowScheduleEntryEntity.cs`
- `DCF.Api/Scraping/IShowInfoScraperTask.cs`
- `DCF.Api/Scraping/ShowInfoScraperTask.cs`
- `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs`

**Modified:**
- `DCF.Data/Entities/ShowEntity.cs` — new fields + nullable `Url` and `ScoresAnnouncedTime`
- `DCF.Data/DcfDbContext.cs` — new DbSet + index
- `DCF.Api/Models/AdminRequests.cs` — updated request DTOs + new response DTOs
- `DCF.Api/Services/IAdminService.cs` — updated signatures + new `PrefillShowAsync`
- `DCF.Api/Services/AdminService.cs` — updated `CreateShowAsync`, `UpdateShowAsync`, `DeleteShowAsync`, new `PrefillShowAsync`, new constructor dep
- `DCF.Api/Controllers/AdminController.cs` — updated routes + new prefill endpoint
- `DCF.Api/Services/ScrapeSchedulerService.cs` — exhibition + null guards
- `DCF.Api/Program.cs` — register new services
- `DCF.Web/src/types/api.ts` — updated `Show` type + new types
- `DCF.Web/src/api/client.ts` — updated `adminCreateShow`, `adminUpdateShow`, new `adminPrefillShow`
- `DCF.Web/src/pages/SeasonDetail.tsx` — new form state, Fetch button, new fields, schedule display

---

## Task 1: Data model — ShowEntity + ShowScheduleEntryEntity + EF migration

**Files:**
- Modify: `DCF.Data/Entities/ShowEntity.cs`
- Create: `DCF.Data/Entities/ShowScheduleEntryEntity.cs`
- Modify: `DCF.Data/DcfDbContext.cs`
- Test: `DCF.Tests/Services/AdminServiceTests.cs` (append new test)

**Interfaces:**
- Produces: `ShowScheduleEntryEntity` class; updated `ShowEntity` with `IsExhibition`, `Location`, `Latitude`, `Longitude`, nullable `Url`, nullable `ScoresAnnouncedTime`, and `Schedule` nav prop; `DcfDbContext.ShowScheduleEntries` DbSet

- [ ] **Step 1: Write the failing test**

Append to `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
[Fact]
public async Task ShowScheduleEntryEntity_CanPersistAndRetrieve()
{
    using var db = CreateDb("schedule_entity_persist");

    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(),
        Year = 2030,
        StartDate = new DateOnly(2030, 6, 1),
        EndDate = new DateOnly(2030, 8, 31)
    };
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Test Corps" };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(),
        Name = "Test Show",
        Date = new DateOnly(2030, 7, 4),
        ScoresAnnouncedTime = null,
        IsExhibition = true,
        Location = "Test Venue, City, ST",
        Latitude = 39.7684,
        Longitude = -86.1581,
        SeasonId = season.Id
    };

    db.Seasons.Add(season);
    db.Corps.Add(corps);
    db.Shows.Add(show);
    db.ShowScheduleEntries.AddRange(
    [
        new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SortOrder = 0,
            Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
            Label = "Test Corps",
            CorpsId = corps.Id
        },
        new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SortOrder = 1,
            Time = new DateTimeOffset(2030, 7, 5, 0, 30, 0, TimeSpan.Zero),
            Label = "Awards",
            CorpsId = null
        }
    ]);

    await db.SaveChangesAsync();

    var entries = db.ShowScheduleEntries
        .Where(e => e.ShowId == show.Id)
        .OrderBy(e => e.SortOrder)
        .ToList();

    Assert.Equal(2, entries.Count);
    Assert.Equal("Test Corps", entries[0].Label);
    Assert.Equal(corps.Id, entries[0].CorpsId);
    Assert.Equal("Awards", entries[1].Label);
    Assert.Null(entries[1].CorpsId);

    var savedShow = await db.Shows.FindAsync(show.Id);
    Assert.True(savedShow!.IsExhibition);
    Assert.Equal("Test Venue, City, ST", savedShow.Location);
    Assert.Equal(39.7684, savedShow.Latitude);
    Assert.Null(savedShow.ScoresAnnouncedTime);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowScheduleEntryEntity_CanPersistAndRetrieve" -v n
```

Expected: FAIL — `ShowScheduleEntryEntity` not found, `IsExhibition` not found.

- [ ] **Step 3: Create ShowScheduleEntryEntity.cs**

```csharp
namespace DCF.Data.Entities;

public class ShowScheduleEntryEntity
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public int SortOrder { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Label { get; set; } = string.Empty;
    public Guid? CorpsId { get; set; }
    public CorpsEntity? Corps { get; set; }
}
```

- [ ] **Step 4: Update ShowEntity.cs**

Replace entire file:

```csharp
using DCF.Data.Models;

namespace DCF.Data.Entities;

public class ShowEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? ScoresAnnouncedTime { get; set; }
    public string? Timezone { get; set; }
    public bool IsExhibition { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public ScrapeStatus ScrapeStatus { get; set; } = ScrapeStatus.NotStarted;
    public DateTimeOffset? LastScrapeAttemptAt { get; set; }
    public string? ScrapeError { get; set; }
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
    public List<ShowScheduleEntryEntity> Schedule { get; set; } = [];
}
```

- [ ] **Step 5: Update DcfDbContext.cs**

Add DbSet after `ComputedScores`:

```csharp
public DbSet<ShowScheduleEntryEntity> ShowScheduleEntries => Set<ShowScheduleEntryEntity>();
```

Add in `OnModelCreating` after the last `HasIndex` call:

```csharp
mb.Entity<ShowScheduleEntryEntity>().HasIndex(e => e.ShowId);
```

- [ ] **Step 6: Run test to verify it passes**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowScheduleEntryEntity_CanPersistAndRetrieve" -v n
```

Expected: PASS.

- [ ] **Step 7: Add EF Core migration**

```
dotnet ef migrations add AddShowLocationAndSchedule --project DCF.Data --startup-project DCF.Api
```

Expected: migration file created in `DCF.Data/Migrations/`. Verify the generated migration adds: `is_exhibition`, `location`, `latitude`, `longitude` columns on `shows`; makes `url` and `scores_announced_time` nullable; creates `show_schedule_entries` table with `id`, `show_id`, `sort_order`, `time`, `label`, `corps_id`.

- [ ] **Step 8: Build to verify no other compile errors**

```
dotnet build DCF.slnx
```

Fix any compilation errors from nullability changes (e.g. `ScrapeSchedulerService` line that passes `show.ScoresAnnouncedTime` as non-nullable — change to `.Value` or null-conditional). Do not fix `AdminService` yet; those changes come in Task 3.

- [ ] **Step 9: Commit**

```
git add DCF.Data/Entities/ShowScheduleEntryEntity.cs DCF.Data/Entities/ShowEntity.cs DCF.Data/DcfDbContext.cs DCF.Data/Migrations/ DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: add ShowScheduleEntryEntity and show location/exhibition fields"
```

---

## Task 2: DCI events page scraper (ShowInfoScraperTask)

**Files:**
- Create: `DCF.Api/Scraping/IShowInfoScraperTask.cs`
- Create: `DCF.Api/Scraping/ShowInfoScraperTask.cs`
- Modify: `DCF.Api/Program.cs`
- Create: `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs`

**Interfaces:**
- Consumes: `IHtmlFetcher.FetchAsync(string url)` (already registered)
- Produces: `IShowInfoScraperTask.ScrapeAsync(string url) → Task<ShowPrefillData?>` where `ShowPrefillData` contains `IsExhibition`, `Location?`, `Latitude?`, `Longitude?`, `StartTime?` (24h "HH:MM"), `ScoresAnnouncedTime?` (24h "HH:MM"), `Timezone?`, and `ScheduleEntries` (list of `ShowPrefillScheduleEntry(string Time24h, string Label)`) — excludes "Gates Open" entry. Lat/lng are parsed from the Google Maps link embedded in the DCI events page.

**HTML structure on DCI events pages** (verified against `https://www.dci.org/events/2026-midcal-showcase/`):

```html
<!-- Schedule table -->
<div class="lineup-times-table">
  <p>All times PT and subject to change</p>
  <table>
    <tbody>
      <tr><td>7:00 PM</td><td><strong>Gates Open</strong></td></tr>
      <tr><td>7:50 PM</td><td><strong>Welcome &amp; National Anthem</strong></td></tr>
      <tr><td>8:00 PM</td><td><strong>Golden Empire</strong> - Bakersfield, CA</td></tr>
      <tr><td>9:40 PM</td><td><strong>Blue Devils</strong> - Concord, CA</td></tr>
      <tr><td>10:00 PM</td><td><strong>Event Concludes</strong></td></tr>
    </tbody>
  </table>
</div>

<!-- Address -->
<div class="address-info">
  <address>Adolfo Camarillo High School Stadium<br>4660 Mission Oaks Blvd<br>Camarillo, CA 93012</address>
</div>

<!-- Google Maps link (contains lat/lng) -->
<a href="https://maps.google.com/?q=34.2228,-119.0307" target="_blank">Get Directions</a>

<!-- Non-competitive marker (exhibition shows only) -->
<strong>NON-COMPETITION FORMAT: </strong>
```

**Google Maps URL lat/lng extraction:** The page contains a `<a>` tag whose `href` includes `maps.google.com` or `google.com/maps`. The coordinates appear as:
- `?q=LAT,LNG` (most common): `maps.google.com/?q=34.2228,-119.0307`
- `query=LAT%2CLNG`: `google.com/maps/search/?api=1&query=34.2228%2C-119.0307`
- `@LAT,LNG` (place URLs): `google.com/maps/place/.../@34.2228,-119.0307,...`

The scraper tries all three patterns and returns the first match.

- [ ] **Step 1: Write the failing tests**

Create `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs`:

```csharp
using DCF.Api.Scraping;
using DCF.Api.Services;
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
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowInfoScraperTaskTests" -v n
```

Expected: FAIL — `ShowInfoScraperTask` not found.

- [ ] **Step 3: Create IShowInfoScraperTask.cs**

```csharp
namespace DCF.Api.Scraping;

public record ShowPrefillScheduleEntry(string Time24h, string Label);

public record ShowPrefillData(
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    string? StartTime,
    string? ScoresAnnouncedTime,
    string? Timezone,
    IReadOnlyList<ShowPrefillScheduleEntry> ScheduleEntries
);

public interface IShowInfoScraperTask
{
    Task<ShowPrefillData?> ScrapeAsync(string url);
}
```

- [ ] **Step 4: Create ShowInfoScraperTask.cs**

```csharp
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace DCF.Api.Scraping;

public class ShowInfoScraperTask(IHtmlFetcher fetcher) : IShowInfoScraperTask
{
    private static readonly Regex TimePattern =
        new(@"(\d{1,2}):(\d{2})\s*(AM|PM)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TzPattern =
        new(@"All times\s+(ET|CT|MT|PT)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LatLngQPattern =
        new(@"[?&]q=(-?\d+\.?\d*),(-?\d+\.?\d*)", RegexOptions.Compiled);

    private static readonly Regex LatLngQueryPattern =
        new(@"query=(-?\d+\.?\d*)%2C(-?\d+\.?\d*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LatLngAtPattern =
        new(@"@(-?\d+\.\d+),(-?\d+\.\d+)", RegexOptions.Compiled);

    public async Task<ShowPrefillData?> ScrapeAsync(string url)
    {
        string html;

        try
        {
            html = await fetcher.FetchAsync(url);
        }
        catch
        {
            return null;
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var isExhibition = doc.DocumentNode.InnerText.Contains(
            "NON-COMPETITION FORMAT", StringComparison.OrdinalIgnoreCase);

        var location = ParseLocation(doc);
        var (lat, lng) = ParseLatLng(doc);
        var (timezone, allEntries) = ParseScheduleEntries(doc);

        var filteredEntries = allEntries
            .Where(e => !e.Label.Equals("Gates Open", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var startTime = filteredEntries.FirstOrDefault()?.Time24h;

        var scoresAnnouncedTime = filteredEntries
            .FirstOrDefault(e =>
                e.Label.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("recap", StringComparison.OrdinalIgnoreCase))
            ?.Time24h;

        return new ShowPrefillData(
            isExhibition,
            location,
            lat,
            lng,
            startTime,
            scoresAnnouncedTime,
            timezone,
            filteredEntries.AsReadOnly());
    }

    private static string? ParseLocation(HtmlDocument doc)
    {
        var addressNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'address-info')]//address");

        if (addressNode is null)
        {
            return null;
        }

        var text = addressNode.InnerText
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");

        return Regex.Replace(text, @"\s{2,}", " ").Trim();
    }

    private static (double? Lat, double? Lng) ParseLatLng(HtmlDocument doc)
    {
        var mapsNodes = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'maps.google.com') or contains(@href,'google.com/maps')]");

        if (mapsNodes is null)
        {
            return (null, null);
        }

        foreach (var node in mapsNodes)
        {
            var href = node.GetAttributeValue("href", string.Empty);

            var m = LatLngQPattern.Match(href);

            if (!m.Success)
            {
                m = LatLngQueryPattern.Match(href);
            }

            if (!m.Success)
            {
                m = LatLngAtPattern.Match(href);
            }

            if (m.Success &&
                double.TryParse(m.Groups[1].Value, out var lat) &&
                double.TryParse(m.Groups[2].Value, out var lng))
            {
                return (lat, lng);
            }
        }

        return (null, null);
    }

    private static (string? Timezone, List<ShowPrefillScheduleEntry> Entries) ParseScheduleEntries(HtmlDocument doc)
    {
        var containerNode = doc.DocumentNode
            .SelectSingleNode("//div[contains(@class,'lineup-times-table')]");

        if (containerNode is null)
        {
            return (null, []);
        }

        string? timezone = null;
        var tzMatch = TzPattern.Match(containerNode.InnerText);

        if (tzMatch.Success)
        {
            timezone = tzMatch.Groups[1].Value.ToUpper();
        }

        var tableNode = containerNode.SelectSingleNode(".//table");

        if (tableNode is null)
        {
            return (timezone, []);
        }

        var rows = tableNode.SelectNodes(".//tr");

        if (rows is null)
        {
            return (timezone, []);
        }

        var entries = new List<ShowPrefillScheduleEntry>();

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("td");

            if (cells is null || cells.Count < 2)
            {
                continue;
            }

            var rawTime = cells[0].InnerText.Trim();
            var rawLabel = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
            var label = StripCity(rawLabel);
            var time24h = ConvertTo24h(rawTime);

            if (time24h is null)
            {
                continue;
            }

            entries.Add(new ShowPrefillScheduleEntry(time24h, label));
        }

        return (timezone, entries);
    }

    private static string? ConvertTo24h(string raw)
    {
        var m = TimePattern.Match(raw);

        if (!m.Success)
        {
            return null;
        }

        var hour = int.Parse(m.Groups[1].Value);
        var minute = int.Parse(m.Groups[2].Value);
        var isPm = m.Groups[3].Value.Equals("PM", StringComparison.OrdinalIgnoreCase);

        if (isPm && hour != 12)
        {
            hour += 12;
        }
        else if (!isPm && hour == 12)
        {
            hour = 0;
        }

        return $"{hour:D2}:{minute:D2}";
    }

    private static string StripCity(string label)
    {
        var dashIndex = label.IndexOf(" - ", StringComparison.Ordinal);

        return dashIndex >= 0 ? label[..dashIndex].Trim() : label.Trim();
    }
}
```

- [ ] **Step 5: Register in Program.cs**

Add after `builder.Services.AddTransient<IRecapScraperTask, RecapScraperTask>();`:

```csharp
builder.Services.AddTransient<IShowInfoScraperTask, ShowInfoScraperTask>();
```

- [ ] **Step 6: Run test to verify it passes**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowInfoScraperTaskTests" -v n
```

Expected: all 13 tests PASS.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Scraping/IShowInfoScraperTask.cs DCF.Api/Scraping/ShowInfoScraperTask.cs DCF.Api/Program.cs DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs
git commit -m "feat: add ShowInfoScraperTask for DCI events page incl. lat/lng from Maps link"
```

---

## Task 3: Admin API — DTOs, service, controller, scraper guard

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`
- Test: `DCF.Tests/Services/AdminServiceTests.cs` (append new tests)

**Interfaces:**
- Consumes: `IShowInfoScraperTask` (Task 2), `ShowScheduleEntryEntity` (Task 1)
- Produces: updated `CreateShowAsync`, `UpdateShowAsync`, `DeleteShowAsync`; new `PrefillShowAsync`; updated `ShowSummary` record; `ShowPrefillResponse` DTO; exhibition guard in `ScrapeSchedulerService`

- [ ] **Step 1: Write failing tests**

Append to `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
[Fact]
public async Task CreateShowAsync_PersistsScheduleEntries()
{
    using var db = CreateDb("admin_create_show_with_schedule");

    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(),
        Year = 2030,
        StartDate = new DateOnly(2030, 6, 1),
        EndDate = new DateOnly(2030, 8, 31)
    };
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };

    db.Seasons.Add(season);
    db.Corps.Add(corps);

    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
    var schedule = new List<ShowScheduleEntryRequest>
    {
        new(new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero), "Blue Devils", corps.Id),
        new(new DateTimeOffset(2030, 7, 5, 0, 0, 0, TimeSpan.Zero), "Awards", null)
    };

    await svc.CreateShowAsync(
        season.Id, "Test Show", null, new DateOnly(2030, 7, 4),
        null, null, "PT", true, "Test Venue", null, null,
        [corps.Id], schedule);

    var entries = db.ShowScheduleEntries
        .Where(e => true)
        .OrderBy(e => e.SortOrder)
        .ToList();

    Assert.Equal(2, entries.Count);
    Assert.Equal("Blue Devils", entries[0].Label);
    Assert.Equal(corps.Id, entries[0].CorpsId);
    Assert.Null(entries[1].CorpsId);
}

[Fact]
public async Task UpdateShowAsync_ReplacesScheduleEntries()
{
    using var db = CreateDb("admin_update_show_schedule");

    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(),
        Year = 2030,
        StartDate = new DateOnly(2030, 6, 1),
        EndDate = new DateOnly(2030, 8, 31)
    };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(),
        Name = "Test Show",
        Date = new DateOnly(2030, 7, 4),
        SeasonId = season.Id
    };

    db.Seasons.Add(season);
    db.Shows.Add(show);
    db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
    {
        Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 0,
        Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
        Label = "Old Entry"
    });

    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);
    var newSchedule = new List<ShowScheduleEntryRequest>
    {
        new(new DateTimeOffset(2030, 7, 4, 23, 30, 0, TimeSpan.Zero), "New Entry", null)
    };

    await svc.UpdateShowAsync(
        show.Id, "Test Show", null, new DateOnly(2030, 7, 4),
        null, null, "PT", false, null, null, null, [], newSchedule);

    var entries = db.ShowScheduleEntries.Where(e => e.ShowId == show.Id).ToList();

    Assert.Single(entries);
    Assert.Equal("New Entry", entries[0].Label);
}

[Fact]
public async Task DeleteShowAsync_AlsoDeletesScheduleEntries()
{
    using var db = CreateDb("admin_delete_show_schedule");

    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2030,
        StartDate = new DateOnly(2030, 6, 1), EndDate = new DateOnly(2030, 8, 31)
    };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Test Show",
        Date = new DateOnly(2030, 7, 4), SeasonId = season.Id
    };

    db.Seasons.Add(season);
    db.Shows.Add(show);
    db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
    {
        Id = Guid.NewGuid(), ShowId = show.Id, SortOrder = 0,
        Time = new DateTimeOffset(2030, 7, 4, 23, 0, 0, TimeSpan.Zero),
        Label = "Entry"
    });

    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

    await svc.DeleteShowAsync(show.Id);

    Assert.Empty(db.ShowScheduleEntries.Where(e => e.ShowId == show.Id).ToList());
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~CreateShowAsync_PersistsScheduleEntries|UpdateShowAsync_ReplacesScheduleEntries|DeleteShowAsync_AlsoDeletesScheduleEntries" -v n
```

Expected: FAIL — constructor mismatch, missing types.

- [ ] **Step 3: Update AdminRequests.cs**

Replace entire file:

```csharp
namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
public record CreateCorpsRequest(string Name);
public record ShowScheduleEntryRequest(DateTimeOffset Time, string Label, Guid? CorpsId);
public record CreateShowRequest(
    string Name,
    string? Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    List<Guid> CorpsIds,
    List<ShowScheduleEntryRequest> Schedule);
public record UpdateShowRequest(
    string Name,
    string? Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    string? Location,
    double? Latitude,
    double? Longitude,
    List<Guid> CorpsIds,
    List<ShowScheduleEntryRequest> Schedule);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
public record CorpsOrderItem(Guid CorpsId, int? SortOrder);
public record SetCorpsOrderRequest(List<CorpsOrderItem> Orders);
public record RenameCorpsRequest(string Name);
public record UpdateSeasonDatesRequest(DateOnly StartDate, DateOnly EndDate);
public record ShowScheduleEntryResponse(DateTimeOffset Time, string Label, Guid? CorpsId);
public record ShowPrefillScheduleEntryResponse(string Time, string Label, Guid? CorpsId);
public record ShowPrefillResponse(
    string? Location,
    double? Latitude,
    double? Longitude,
    string? StartTime,
    string? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    List<Guid> CorpsIds,
    List<ShowPrefillScheduleEntryResponse> Schedule);
```

- [ ] **Step 4: Update AdminService.cs**

**Update `ShowSummary` record** (defined at top of AdminService.cs):

```csharp
public record ShowSummary(
    Guid Id, string Name, string? Url, DateOnly Date, DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime, string? Timezone, bool IsExhibition,
    string? Location, double? Latitude, double? Longitude,
    ScrapeStatus ScrapeStatus, DateTimeOffset? LastScrapeAttemptAt, string? ScrapeError,
    IEnumerable<Guid> CorpsIds, IEnumerable<ShowScheduleEntryResponse> Schedule);
```

**Update constructor** to add `IShowInfoScraperTask`:

```csharp
public class AdminService(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttService mqtt,
    ISeasonStatusService seasonStatus,
    IShowInfoScraperTask showInfoScraper) : IAdminService
```

**Replace `GetShowsAsync`**:

```csharp
public async Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId)
{
    var shows = await db.Shows
        .Where(s => s.SeasonId == seasonId)
        .Include(s => s.ShowCorps)
        .Include(s => s.Schedule)
        .OrderBy(s => s.Date)
        .ThenBy(s => s.StartTime)
        .ToListAsync();

    return shows.Select(s => new ShowSummary(
        s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime, s.Timezone,
        s.IsExhibition, s.Location, s.Latitude, s.Longitude,
        s.ScrapeStatus, s.LastScrapeAttemptAt, s.ScrapeError,
        s.ShowCorps.Select(sc => sc.CorpsId),
        s.Schedule.OrderBy(e => e.SortOrder)
            .Select(e => new ShowScheduleEntryResponse(e.Time, e.Label, e.CorpsId))))
        .ToList();
}
```

**Replace `CreateShowAsync`** signature and body:

```csharp
public async Task<ShowBrief> CreateShowAsync(
    Guid seasonId, string name, string? url, DateOnly date,
    DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
    bool isExhibition, string? location, double? latitude, double? longitude,
    List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule)
{
    var season = await db.Seasons.FindAsync(seasonId)
        ?? throw new InvalidOperationException("Season not found.");

    if (date < season.StartDate || date > season.EndDate)
    {
        throw new InvalidOperationException($"Show date must be within the season range ({season.StartDate}–{season.EndDate}).");
    }

    if (date < DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-10)))
    {
        throw new InvalidOperationException("Show date cannot be in the past.");
    }

    var show = new ShowEntity
    {
        Id = Guid.NewGuid(),
        Name = name,
        Url = url,
        Date = date,
        StartTime = startTime,
        ScoresAnnouncedTime = scoresAnnouncedTime,
        Timezone = timezone,
        IsExhibition = isExhibition,
        Location = location,
        Latitude = latitude,
        Longitude = longitude,
        SeasonId = seasonId
    };

    db.Shows.Add(show);
    db.ShowCorps.AddRange(corpsIds.Select(cId =>
        new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));
    db.ShowScheduleEntries.AddRange(schedule.Select((entry, i) =>
        new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SortOrder = i,
            Time = entry.Time,
            Label = entry.Label,
            CorpsId = entry.CorpsId
        }));

    await db.SaveChangesAsync();

    scrapeScheduler.ScheduleScrape(show);

    return new ShowBrief(show.Id, show.Name);
}
```

**Replace `UpdateShowAsync`** signature and body:

```csharp
public async Task<bool> UpdateShowAsync(
    Guid id, string name, string? url, DateOnly date,
    DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
    bool isExhibition, string? location, double? latitude, double? longitude,
    List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule)
{
    var show = await db.Shows.FindAsync(id);

    if (show is null)
    {
        return false;
    }

    if (show.ScrapeStatus == ScrapeStatus.Succeeded)
    {
        return false;
    }

    show.Name = name;
    show.Url = url;
    show.Date = date;
    show.StartTime = startTime;
    show.ScoresAnnouncedTime = scoresAnnouncedTime;
    show.Timezone = timezone;
    show.IsExhibition = isExhibition;
    show.Location = location;
    show.Latitude = latitude;
    show.Longitude = longitude;

    var existingCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
    db.ShowCorps.RemoveRange(existingCorps);
    db.ShowCorps.AddRange(corpsIds.Select(cId =>
        new ShowCorpsEntity { ShowId = id, CorpsId = cId }));

    var existingSchedule = await db.ShowScheduleEntries.Where(e => e.ShowId == id).ToListAsync();
    db.ShowScheduleEntries.RemoveRange(existingSchedule);
    db.ShowScheduleEntries.AddRange(schedule.Select((entry, i) =>
        new ShowScheduleEntryEntity
        {
            Id = Guid.NewGuid(),
            ShowId = id,
            SortOrder = i,
            Time = entry.Time,
            Label = entry.Label,
            CorpsId = entry.CorpsId
        }));

    await db.SaveChangesAsync();

    var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
    scrapeScheduler.ScheduleScrape(updatedShow);

    return true;
}
```

**Update `DeleteShowAsync`** to remove schedule entries:

```csharp
public async Task<bool> DeleteShowAsync(Guid id)
{
    var show = await db.Shows.FindAsync(id);

    if (show is null)
    {
        return false;
    }

    var showCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
    var scheduleEntries = await db.ShowScheduleEntries.Where(e => e.ShowId == id).ToListAsync();
    db.ShowCorps.RemoveRange(showCorps);
    db.ShowScheduleEntries.RemoveRange(scheduleEntries);
    db.Shows.Remove(show);

    await db.SaveChangesAsync();

    return true;
}
```

**Add `PrefillShowAsync`** and helpers after `DeleteShowAsync`:

```csharp
public async Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId)
{
    var season = await db.Seasons.FindAsync(seasonId);

    if (season is null)
    {
        return null;
    }

    var slug = Slugify(showName);
    var eventsUrl = $"https://www.dci.org/events/{season.Year}-{slug}/";
    var prefillData = await showInfoScraper.ScrapeAsync(eventsUrl);

    if (prefillData is null)
    {
        return null;
    }

    var seasonCorpsList = await db.Corps
        .Where(c => db.SeasonCorps.Any(sc => sc.SeasonId == seasonId && sc.CorpsId == c.Id))
        .ToListAsync();

    var corpsIds = new List<Guid>();
    var scheduleEntries = new List<ShowPrefillScheduleEntryResponse>();

    foreach (var entry in prefillData.ScheduleEntries)
    {
        var corpsName = StripCity(entry.Label);
        var corpsMatch = seasonCorpsList.FirstOrDefault(c =>
            c.Name.Equals(corpsName, StringComparison.OrdinalIgnoreCase));

        var corpsId = corpsMatch?.Id;

        if (corpsId.HasValue && !corpsIds.Contains(corpsId.Value))
        {
            corpsIds.Add(corpsId.Value);
        }

        scheduleEntries.Add(new ShowPrefillScheduleEntryResponse(entry.Time24h, entry.Label, corpsId));
    }

    return new ShowPrefillResponse(
        prefillData.Location,
        prefillData.Latitude,
        prefillData.Longitude,
        prefillData.StartTime,
        prefillData.ScoresAnnouncedTime,
        prefillData.Timezone,
        prefillData.IsExhibition,
        corpsIds,
        scheduleEntries);
}

private static string Slugify(string name)
{
    var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-");

    return slug.Trim('-');
}

private static string StripCity(string label)
{
    var dashIndex = label.IndexOf(" - ", StringComparison.Ordinal);

    return dashIndex >= 0 ? label[..dashIndex].Trim() : label.Trim();
}
```

Add `using System.Text.RegularExpressions;` to the using block in `AdminService.cs`.

- [ ] **Step 5: Update IAdminService.cs**

Replace entire file:

```csharp
using DCF.Api.Models;

namespace DCF.Api.Services;

public interface IAdminService
{
    Task<bool> IsAdminAsync(string sub);
    Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync();
    Task<SeasonSummary> CreateSeasonAsync(int year, DateOnly startDate, DateOnly endDate);
    Task<SeasonDetail?> GetSeasonDetailAsync(Guid id);
    Task<bool> PublishSeasonAsync(Guid id);
    Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync();
    Task<CorpsSummary> CreateCorpsAsync(string name);
    Task<bool> SetSeasonCorpsAsync(Guid seasonId, List<Guid> corpsIds);
    Task<(bool Found, bool CanEdit)> SetSeasonCorpsOrderAsync(Guid seasonId, List<(Guid CorpsId, int? SortOrder)> orders);
    Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId);
    Task<ShowBrief> CreateShowAsync(
        Guid seasonId, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule);
    Task<bool> UpdateShowAsync(
        Guid id, string name, string? url, DateOnly date,
        DateTimeOffset? startTime, DateTimeOffset? scoresAnnouncedTime, string? timezone,
        bool isExhibition, string? location, double? latitude, double? longitude,
        List<Guid> corpsIds, List<ShowScheduleEntryRequest> schedule);
    Task<bool> TriggerScrapeAsync(Guid showId);
    Task<CorpsSummary?> RenameCorpsAsync(Guid id, string name);
    Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath);
    Task<(bool Found, bool Deletable)> DeleteCorpsAsync(Guid id);
    Task<bool> UpdateSeasonDatesAsync(Guid id, DateOnly startDate, DateOnly endDate);
    Task<bool> DeleteShowAsync(Guid id);
    Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId);
}
```

- [ ] **Step 6: Update AdminController.cs**

Update `CreateShow` action:

```csharp
[HttpPost("seasons/{seasonId}/shows")]
public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    try
    {
        var result = await adminService.CreateShowAsync(
            seasonId, req.Name, req.Url, req.Date, req.StartTime, req.ScoresAnnouncedTime,
            req.Timezone, req.IsExhibition, req.Location, req.Latitude, req.Longitude,
            req.CorpsIds, req.Schedule);

        return Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}
```

Update `UpdateShow` action:

```csharp
[HttpPut("shows/{id}")]
public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    return await adminService.UpdateShowAsync(
        id, req.Name, req.Url, req.Date, req.StartTime, req.ScoresAnnouncedTime,
        req.Timezone, req.IsExhibition, req.Location, req.Latitude, req.Longitude,
        req.CorpsIds, req.Schedule) ? NoContent() : NotFound();
}
```

Add new prefill action in the Shows section:

```csharp
[HttpGet("seasons/{seasonId}/shows/prefill")]
public async Task<IActionResult> PrefillShow(Guid seasonId, [FromQuery] string name)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    var result = await adminService.PrefillShowAsync(name, seasonId);

    if (result is null)
    {
        return NotFound(new { error = "Could not fetch show info from DCI. Check the show name and try again." });
    }

    return Ok(result);
}
```

- [ ] **Step 7: Update ScrapeSchedulerService.cs**

Update `ExecuteAsync` startup query:

```csharp
var shows = await db.Shows
    .Include(s => s.ShowCorps)
    .Where(s => !s.IsExhibition
             && s.Url != null
             && s.ScoresAnnouncedTime.HasValue
             && s.ScoresAnnouncedTime.Value > DateTimeOffset.UtcNow)
    .ToListAsync(stoppingToken);
```

Update `ScheduleScrape` to add guard at the top:

```csharp
public void ScheduleScrape(ShowEntity show)
{
    if (show.IsExhibition || show.Url is null || show.ScoresAnnouncedTime is null)
    {
        return;
    }

    if (_scheduled.TryRemove(show.Id, out var existing))
    {
        existing.Cancel();
        existing.Dispose();
    }

    var cts = new CancellationTokenSource();
    _scheduled[show.Id] = cts;

    _ = Task.Run(async () =>
    {
        try
        {
            var delay = GetScrapeDelay(show.ScoresAnnouncedTime.Value, _delayMinutes, DateTimeOffset.UtcNow);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cts.Token);
            }

            if (cts.Token.IsCancellationRequested)
            {
                return;
            }

            await ExecuteScrapeAsync(show);

            await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
        }
        catch (OperationCanceledException)
        {
            // expected when rescheduled
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled scrape task failed for show {ShowId}", show.Id);
        }
    });
}
```

Update `GetScrapeDelay` to accept non-nullable (the guard above ensures null can't reach it):

```csharp
public static TimeSpan GetScrapeDelay(DateTimeOffset scoresAnnouncedTime, int delayMinutes, DateTimeOffset now)
    => scoresAnnouncedTime.AddMinutes(delayMinutes) - now;
```

Update `ExecuteScrapeAsync` to guard against null URL:

```csharp
if (freshShow is null || freshShow.IsExhibition || freshShow.Url is null)
{
    logger.LogWarning("Show {ShowId} cannot be scraped", show.Id);

    return;
}

var scraperShow = new Show(freshShow.Id, freshShow.Name, freshShow.Url, freshShow.Date);
```

- [ ] **Step 8: Run failing tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~CreateShowAsync_PersistsScheduleEntries|UpdateShowAsync_ReplacesScheduleEntries|DeleteShowAsync_AlsoDeletesScheduleEntries" -v n
```

Expected: PASS.

- [ ] **Step 9: Run full test suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Fix any failures from the `ScoresAnnouncedTime` nullability change (e.g. existing tests that call `CreateShowAsync` with the old signature need their argument lists updated to include the new parameters with default-safe values).

- [ ] **Step 10: Commit**

```
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: update admin API for show location, schedule, exhibition flag, and prefill endpoint"
```

---

## Task 4: Frontend — types, client, and SeasonDetail form

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Consumes: `GET /api/admin/seasons/{seasonId}/shows/prefill?name={name}` (Task 3)
- Produces: updated `Show` type; `ShowScheduleEntry`, `ShowPrefillResponse`, `ShowPrefillScheduleEntry` types; updated `adminCreateShow`, `adminUpdateShow`; new `adminPrefillShow`; "Fetch from DCI" button with inline error; `IsExhibition` toggle; `Location` field; schedule display

- [ ] **Step 1: Update api.ts**

Replace the `Show` interface and add new types:

```typescript
export interface ShowScheduleEntry {
  time: string;
  label: string;
  corpsId: string | null;
}

export interface ShowPrefillScheduleEntry {
  time: string;
  label: string;
  corpsId: string | null;
}

export interface ShowPrefillResponse {
  location?: string;
  latitude?: number;
  longitude?: number;
  startTime?: string;
  scoresAnnouncedTime?: string;
  timezone?: string;
  isExhibition: boolean;
  corpsIds: string[];
  schedule: ShowPrefillScheduleEntry[];
}

export interface Show {
  id: string;
  name: string;
  url?: string;
  date: string;
  startTime?: string;
  scoresAnnouncedTime?: string;
  timezone?: string;
  isExhibition: boolean;
  location?: string;
  latitude?: number;
  longitude?: number;
  corpsIds: string[];
  scrapeStatus: 'NotStarted' | 'Succeeded' | 'Failed';
  lastScrapeAttemptAt?: string;
  scrapeError?: string;
  schedule: ShowScheduleEntry[];
}
```

- [ ] **Step 2: Update client.ts**

Replace `adminCreateShow`, `adminUpdateShow`, add `adminPrefillShow`:

```typescript
adminCreateShow: (
  seasonId: string,
  body: {
    name: string;
    url?: string | null;
    date: string;
    startTime: string | null;
    scoresAnnouncedTime: string | null;
    timezone?: string;
    isExhibition: boolean;
    location?: string | null;
    latitude?: number | null;
    longitude?: number | null;
    corpsIds: string[];
    schedule: { time: string; label: string; corpsId: string | null }[];
  }
) =>
  request<{ id: string; name: string }>(`/api/admin/seasons/${seasonId}/shows`, {
    method: 'POST',
    body: JSON.stringify(body),
  }),
adminUpdateShow: (
  id: string,
  body: {
    name: string;
    url?: string | null;
    date: string;
    startTime: string | null;
    scoresAnnouncedTime: string | null;
    timezone?: string;
    isExhibition: boolean;
    location?: string | null;
    latitude?: number | null;
    longitude?: number | null;
    corpsIds: string[];
    schedule: { time: string; label: string; corpsId: string | null }[];
  }
) =>
  request<void>(`/api/admin/shows/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
adminDeleteShow: (id: string) =>
  request<void>(`/api/admin/shows/${id}`, { method: 'DELETE' }),
adminPrefillShow: (seasonId: string, name: string) =>
  request<ShowPrefillResponse>(
    `/api/admin/seasons/${seasonId}/shows/prefill?name=${encodeURIComponent(name)}`
  ),
```

Update the import at the top of `client.ts` to include `ShowPrefillResponse`:

```typescript
import type { ..., ShowPrefillResponse } from '../types/api';
```

- [ ] **Step 3: Update SeasonDetail.tsx — add new form state**

Add after existing `addingShow` state variable:

```typescript
const [isExhibition, setIsExhibition] = useState(false);
const [showLocation, setShowLocation] = useState('');
const [showLatitude, setShowLatitude] = useState<number | null>(null);
const [showLongitude, setShowLongitude] = useState<number | null>(null);
const [showSchedule, setShowSchedule] = useState<ShowPrefillScheduleEntry[]>([]);
const [prefetchError, setPrefetchError] = useState<string | null>(null);
const [prefetching, setPrefetching] = useState(false);
```

Add the `fetchFromDci` handler alongside `addShow`:

```typescript
const fetchFromDci = async () => {
  if (!id || !showName || prefetching) return;
  setPrefetching(true);
  setPrefetchError(null);

  try {
    const data = await api.adminPrefillShow(id, showName);
    setIsExhibition(data.isExhibition);
    setShowLocation(data.location ?? '');
    setShowLatitude(data.latitude ?? null);
    setShowLongitude(data.longitude ?? null);

    if (data.startTime) {
      setShowStartTime(data.startTime);
    }

    if (data.scoresAnnouncedTime) {
      setShowScoresTime(data.scoresAnnouncedTime);
    }

    if (data.timezone) {
      setShowTz(data.timezone);
    }

    if (data.corpsIds.length > 0) {
      setShowCorpsIds(new Set(data.corpsIds));
    }

    setShowSchedule(data.schedule);
  } catch {
    setPrefetchError('Could not fetch from DCI — fill in manually.');
  } finally {
    setPrefetching(false);
  }
};
```

- [ ] **Step 4: Update addShow to use new fields**

Replace the `addShow` handler:

```typescript
const addShow = async (e: FormEvent) => {
  e.preventDefault();
  if (!id || addingShow) return;
  if (!isExhibition && showCorpsIds.size === 0) { setError('Select at least one corps.'); return; }
  if (!isExhibition && !showScoresTime) { setError('Scores announced time is required for competitive shows.'); return; }
  setAddingShow(true);
  setError(null);

  try {
    const startTimeIso = showStartTime ? buildDateTime(showDate, showStartTime, showTz) : null;
    const scoresTimeIso = showScoresTime ? buildDateTime(showDate, showScoresTime, showTz) : null;
    const schedulePayload = showSchedule.map(entry => ({
      time: buildDateTime(showDate, entry.time, showTz),
      label: entry.label,
      corpsId: entry.corpsId,
    }));

    await api.adminCreateShow(id, {
      name: showName,
      url: isExhibition ? null : showUrl,
      date: showDate,
      startTime: startTimeIso,
      scoresAnnouncedTime: scoresTimeIso,
      timezone: showTz,
      isExhibition,
      location: showLocation || null,
      latitude: showLatitude,
      longitude: showLongitude,
      corpsIds: Array.from(showCorpsIds),
      schedule: schedulePayload,
    });

    const updated = await api.adminGetShows(id);
    setShows(updated);
    setShowName('');
    setShowUrl('');
    setUrlManuallyEdited(false);
    setShowDate('');
    setShowTz('ET');
    setShowStartTime('');
    setShowScoresTime('');
    setShowCorpsIds(new Set());
    setIsExhibition(false);
    setShowLocation('');
    setShowLatitude(null);
    setShowLongitude(null);
    setShowSchedule([]);
    setPrefetchError(null);
    setAddShowOpen(false);
  } catch {
    setError('Failed to add show.');
  } finally {
    setAddingShow(false);
  }
};
```

- [ ] **Step 5: Update the form JSX**

In the add-show form, add the "Fetch from DCI" button next to the Name field, and add the new fields. Find the name field row and update it:

```tsx
{/* Name + Fetch row */}
<div style={{ display: 'flex', gap: 8, alignItems: 'flex-end' }}>
  <div style={{ flex: 1 }}>
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
      <span style={labelStyle}>Name</span>
      <input
        style={inputStyle}
        value={showName}
        onChange={e => {
          setShowName(e.target.value);
          if (!urlManuallyEdited && season) {
            setShowUrl(generateRecapUrl(e.target.value, season.year));
          }
        }}
        required
      />
    </div>
  </div>
  <button
    type="button"
    onClick={fetchFromDci}
    disabled={!showName || prefetching}
    style={{
      padding: '7px 12px', borderRadius: 5, fontSize: 10, fontWeight: 600,
      background: 'var(--surface)', border: '1px solid var(--border)',
      color: 'var(--text-muted)', cursor: showName && !prefetching ? 'pointer' : 'not-allowed',
      opacity: !showName || prefetching ? 0.5 : 1, whiteSpace: 'nowrap', marginBottom: 6,
    }}
  >
    {prefetching ? 'Fetching…' : 'Fetch from DCI'}
  </button>
</div>
{prefetchError && (
  <p style={{ fontSize: 10, color: 'var(--red)', margin: '2px 0 6px' }}>{prefetchError}</p>
)}

{/* IsExhibition toggle */}
<div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
  <span style={labelStyle}>Exhibition</span>
  <input
    type="checkbox"
    checked={isExhibition}
    onChange={e => setIsExhibition(e.target.checked)}
  />
  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>Non-competitive show (no scores)</span>
</div>

{/* Scores URL (hide for exhibition) */}
{!isExhibition && (
  <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
    <span style={labelStyle}>Recap URL</span>
    <input
      style={inputStyle}
      value={showUrl}
      onChange={e => { setShowUrl(e.target.value); setUrlManuallyEdited(true); }}
    />
  </div>
)}

{/* Location */}
<div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6 }}>
  <span style={labelStyle}>Location</span>
  <input
    style={inputStyle}
    value={showLocation}
    onChange={e => setShowLocation(e.target.value)}
    placeholder="Venue Name, City, ST 00000"
  />
</div>
```

After the corps chip-select section, add the schedule display (read-only):

```tsx
{showSchedule.length > 0 && (
  <div style={{ marginTop: 8 }}>
    <p style={{ ...labelStyle, textAlign: 'left', marginBottom: 4 }}>Schedule</p>
    <div style={{
      background: 'var(--bg)', border: '1px solid var(--border)',
      borderRadius: 5, padding: '6px 10px', fontSize: 10, color: 'var(--text-muted)',
    }}>
      {showSchedule.map((entry, i) => (
        <div key={i} style={{ display: 'flex', gap: 12, padding: '2px 0' }}>
          <span style={{ minWidth: 40, fontVariantNumeric: 'tabular-nums' }}>{entry.time}</span>
          <span>{entry.label}</span>
        </div>
      ))}
    </div>
  </div>
)}
```

- [ ] **Step 6: Fix hasScoresAnnounced for nullable scoresAnnouncedTime**

Update the `hasScoresAnnounced` function:

```typescript
function hasScoresAnnounced(show: Show): boolean {
  return !!show.scoresAnnouncedTime && new Date(show.scoresAnnouncedTime) <= new Date();
}
```

Hide the scrape trigger button for exhibition shows — find the scrape trigger render and wrap with:

```tsx
{!show.isExhibition && (hasStarted(show) || hasScoresAnnounced(show)) && (
  // ... existing scrape trigger button JSX
)}
```

- [ ] **Step 7: Update editShow submit to include new fields**

Find the existing `editShow` save handler and update the `api.adminUpdateShow` call to include the new fields:

```typescript
await api.adminUpdateShow(show.id, {
  name: editShow.name,
  url: show.isExhibition ? null : editShow.url,
  date: editShow.date,
  startTime: editShow.startTime ? buildDateTime(editShow.date, editShow.startTime, editShow.tz) : null,
  scoresAnnouncedTime: editShow.scoresTime ? buildDateTime(editShow.date, editShow.scoresTime, editShow.tz) : null,
  timezone: editShow.tz,
  isExhibition: show.isExhibition,
  location: show.location ?? null,
  latitude: show.latitude ?? null,
  longitude: show.longitude ?? null,
  corpsIds: Array.from(editShow.corpsIds),
  schedule: show.schedule.map(e => ({
    time: new Date(e.time).toISOString(),
    label: e.label,
    corpsId: e.corpsId,
  })),
});
```

- [ ] **Step 8: Add ShowPrefillScheduleEntry to imports in SeasonDetail.tsx**

Ensure the type is imported:

```typescript
import type { Corps, SeasonDetail as SeasonDetailType, Show, ShowPrefillScheduleEntry } from '../types/api';
```

- [ ] **Step 9: Build and lint**

```
cd DCF.Web && npm run build
npm run lint
```

Fix any TypeScript errors.

- [ ] **Step 10: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests pass.

- [ ] **Step 11: Commit**

```
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: add DCI show auto-populate button, exhibition toggle, location, and schedule display"
```

---

## Self-Review Checklist

- [x] `ShowEntity.Url` made nullable — `ScrapeSchedulerService` guards updated
- [x] `ShowEntity.ScoresAnnouncedTime` made nullable — all call sites updated (`.Value`, `.HasValue`, null checks)
- [x] Exhibition shows skipped by `ScrapeSchedulerService.ScheduleScrape` and `ExecuteScrapeAsync`
- [x] Schedule entries deleted in `DeleteShowAsync`
- [x] `GetShowsAsync` includes `Schedule` via `Include(s => s.Schedule)`
- [x] `ShowPrefillData.ScheduleEntries` excludes "Gates Open" (filtered in scraper)
- [x] Lat/lng extracted from Google Maps link in the scraper — no external geocoding API
- [x] `hasScoresAnnounced` updated for nullable `scoresAnnouncedTime`
- [x] Scrape trigger button hidden for exhibition shows in frontend
- [x] `adminUpdateShow` in `editShow` handler sends new fields to avoid data loss
- [x] Schedule times in `addShow` converted to ISO via `buildDateTime` before submitting
