# Show Schedule Data Completeness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the DCI show-info scraper from silently dropping corps with unscheduled ("TBD") performance times, and make exhibition shows carry a "show is over" timestamp the same way competitive shows already do via `ScoresAnnouncedTime`.

**Architecture:** `ShowScheduleEntryEntity.Time` becomes nullable end-to-end (entity → request/response DTOs → frontend types) so an "unscheduled" corps is represented as a schedule row with no time, not a dropped row. Separately, `ShowInfoScraperTask`'s existing `ScoresAnnouncedTime` extraction is widened to also match DCI's "Event Concludes" label, so exhibition shows populate the same field competitive shows do — no new column.

**Tech Stack:** ASP.NET Core / EF Core (Npgsql, InMemory for tests) on the backend; React + TypeScript + Vitest/React Testing Library on the frontend.

## Global Constraints

- All work in this plan happens on branch `feat/show-schedule-completeness` (already created off `master`; the design spec commit is already on it). Commit after every task.
- EF Core migrations: `dotnet ef migrations add <Name> --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj`. Apply with `dotnet ef database update` using the same two flags.
- No new `EventConcludesTime` field/column. `ShowEntity.ScoresAnnouncedTime` is reused for both competitive and exhibition shows — this was an explicit decision in the approved spec (`docs/superpowers/specs/2026-07-07-show-schedule-completeness-design.md`), not an oversight.
- Backend tests use EF Core InMemory: `new DbContextOptionsBuilder<DcfDbContext>().UseInMemoryDatabase(name).Options`, one unique `name` string per test, matching the existing convention in `DCF.Tests/Services/AdminServiceTests.cs`.
- Do not modify the existing `ExhibitionHtml`/`CompetitiveHtml` fixture constants in `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs` — existing tests assert exact entry counts against them. Add new fixtures instead.
- Frontend tests use Vitest + React Testing Library (already configured), colocated as `<name>.test.tsx` next to the source file, matching `DCF.Web/src/components/TimePicker.test.tsx`.

---

## File Structure

**Backend — modified:**
- `DCF.Data/Entities/ShowScheduleEntryEntity.cs` — `Time` becomes `DateTimeOffset?`.
- `DCF.Data/Migrations/` — new generated migration (`dotnet ef migrations add`), not hand-written.
- `DCF.Api/Scraping/IShowInfoScraperTask.cs` — `ShowPrefillScheduleEntry.Time24h` becomes `string?`.
- `DCF.Api/Scraping/ShowInfoScraperTask.cs` — stop dropping rows with an unparseable time; widen `ScoresAnnouncedTime` label matching to include "conclude".
- `DCF.Api/Models/AdminRequests.cs` — `ShowScheduleEntryRequest.Time`, `ShowScheduleEntryResponse.Time`, `ShowPrefillScheduleEntryResponse.Time` all become nullable.

**Backend — tests modified:**
- `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs` — new fixture + 3 tests.
- `DCF.Tests/Services/ScrapeTestHelpers.cs` — new `FakeShowInfoScraperTask`.
- `DCF.Tests/Services/AdminServiceTests.cs` — 3 new tests.

**Frontend — modified:**
- `DCF.Web/src/types/api.ts` — `ShowScheduleEntry.time` / `ShowPrefillScheduleEntry.time` become `string | null`.
- `DCF.Web/src/api/client.ts` — `adminCreateShow`/`adminUpdateShow` stop duplicating an inline schedule-entry type and reuse `ShowScheduleEntry[]`.
- `DCF.Web/src/pages/SeasonDetail.tsx` — export `buildDateTime`; add exported `buildScheduleEntryTime`/`toNullableIso` helpers; fix two null-handling bugs; render "TBD"; dynamic "Scores"/"Concludes" label; widen required-time validation.

**Frontend — new:**
- `DCF.Web/src/pages/SeasonDetail.test.tsx` — unit tests for the three helpers above.

---

### Task 1: Make `ShowScheduleEntryEntity.Time` nullable

**Files:**
- Modify: `DCF.Data/Entities/ShowScheduleEntryEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_MakeShowScheduleTimeNullable.cs` (+ `.Designer.cs`, snapshot update) — generated, not hand-written
- Test: `DCF.Tests/Services/AdminServiceTests.cs`

**Interfaces:**
- Produces: `ShowScheduleEntryEntity.Time` as `DateTimeOffset?` (was `DateTimeOffset`) — every later task that reads/writes this property relies on it being nullable.

- [ ] **Step 1: Write the failing test**

Add to `DCF.Tests/Services/AdminServiceTests.cs`, right after the existing `ShowScheduleEntryEntity_CanPersistAndRetrieve` test (around line 438):

```csharp
[Fact]
public async Task ShowScheduleEntryEntity_NullTime_PersistsAsUnscheduled()
{
    using var db = CreateDb("schedule_entity_null_time");

    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(),
        Year = 2030,
        StartDate = new DateOnly(2030, 6, 1),
        EndDate = new DateOnly(2030, 8, 31)
    };
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Unscheduled Corps" };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(),
        Name = "Test Show",
        Date = new DateOnly(2030, 7, 4),
        SeasonId = season.Id
    };

    db.Seasons.Add(season);
    db.Corps.Add(corps);
    db.Shows.Add(show);
    db.ShowScheduleEntries.Add(new ShowScheduleEntryEntity
    {
        Id = Guid.NewGuid(),
        ShowId = show.Id,
        SortOrder = 0,
        Time = null,
        Label = "Unscheduled Corps",
        CorpsId = corps.Id
    });

    await db.SaveChangesAsync();

    var entry = db.ShowScheduleEntries.Single(e => e.ShowId == show.Id);

    Assert.Null(entry.Time);
    Assert.Equal("Unscheduled Corps", entry.Label);
}
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS0037: Cannot convert null to 'DateTimeOffset' because it is a non-nullable value type`, pointing at `Time = null` in the test just added.

- [ ] **Step 3: Make the property nullable**

In `DCF.Data/Entities/ShowScheduleEntryEntity.cs`, change:

```csharp
    public DateTimeOffset Time { get; set; }
```

to:

```csharp
    public DateTimeOffset? Time { get; set; }
```

- [ ] **Step 4: Generate the migration**

Run:
```bash
dotnet ef migrations add MakeShowScheduleTimeNullable --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
```
Expected: creates `DCF.Data/Migrations/<timestamp>_MakeShowScheduleTimeNullable.cs` and `.Designer.cs`, and updates `DcfDbContextModelSnapshot.cs`. The generated `Up()` should call `migrationBuilder.AlterColumn<DateTimeOffset>(name: "Time", table: "ShowScheduleEntries", nullable: true, oldClrType: typeof(DateTimeOffset))` (or equivalent) — inspect the generated file to confirm it only touches the `Time` column's nullability, nothing else.

- [ ] **Step 5: Run build and test to verify it passes**

Run: `dotnet build DCF.slnx`
Expected: SUCCESS

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowScheduleEntryEntity_NullTime_PersistsAsUnscheduled"`
Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add DCF.Data/Entities/ShowScheduleEntryEntity.cs DCF.Data/Migrations/ DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: make ShowScheduleEntryEntity.Time nullable to represent unscheduled corps"
```

---

### Task 2: Thread nullable `Time` through the Create/Update Show DTOs

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Test: `DCF.Tests/Services/AdminServiceTests.cs`

**Interfaces:**
- Consumes: `ShowScheduleEntryEntity.Time : DateTimeOffset?` (Task 1)
- Produces: `ShowScheduleEntryRequest.Time`, `ShowScheduleEntryResponse.Time`, `ShowPrefillScheduleEntryResponse.Time` all as nullable — Task 4 relies on `ShowPrefillScheduleEntryResponse.Time` being nullable.

`AdminService.CreateShowAsync`/`UpdateShowAsync`/`GetShowsAsync`/`PrefillShowAsync` all assign directly between these types (e.g. `Time = entry.Time`) with no branching logic — once both sides of each assignment are nullable, they compile unchanged. No changes to `AdminService.cs` are needed in this task.

- [ ] **Step 1: Write the failing test**

Add to `DCF.Tests/Services/AdminServiceTests.cs`, after `CreateShowAsync_PersistsScheduleEntries` (around line 479):

```csharp
[Fact]
public async Task CreateShowAsync_NullScheduleTime_PersistsAsUnscheduled()
{
    using var db = CreateDb("admin_create_show_null_time");

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
        new(null, "Blue Devils - Concord, CA", corps.Id)
    };

    await svc.CreateShowAsync(
        season.Id, "Test Show", null, new DateOnly(2030, 7, 4),
        null, null, "PT", true, "Test Venue", null, null,
        [corps.Id], schedule);

    var entry = db.ShowScheduleEntries.Single(e => e.CorpsId == corps.Id);

    Assert.Null(entry.Time);
    Assert.Equal("Blue Devils - Concord, CA", entry.Label);
}
```

Note: no equivalent test is added for `UpdateShowAsync` — it builds `ShowScheduleEntryEntity` from `ShowScheduleEntryRequest` the same way `CreateShowAsync` does (same assignment, same types), so it would be a redundant test of the same code shape.

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS1503` (or similar): cannot convert `null` to `DateTimeOffset` in `new(null, "Blue Devils - Concord, CA", corps.Id)`, since `ShowScheduleEntryRequest`'s first parameter is still non-nullable.

- [ ] **Step 3: Make the three Time fields nullable**

In `DCF.Api/Models/AdminRequests.cs`, change:

```csharp
public record ShowScheduleEntryRequest(DateTimeOffset Time, string Label, Guid? CorpsId);
```
to:
```csharp
public record ShowScheduleEntryRequest(DateTimeOffset? Time, string Label, Guid? CorpsId);
```

and:
```csharp
public record ShowScheduleEntryResponse(DateTimeOffset Time, string Label, Guid? CorpsId);
public record ShowPrefillScheduleEntryResponse(string Time, string Label, Guid? CorpsId);
```
to:
```csharp
public record ShowScheduleEntryResponse(DateTimeOffset? Time, string Label, Guid? CorpsId);
public record ShowPrefillScheduleEntryResponse(string? Time, string Label, Guid? CorpsId);
```

- [ ] **Step 4: Run build and test to verify it passes**

Run: `dotnet build DCF.slnx`
Expected: SUCCESS

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~CreateShowAsync_NullScheduleTime_PersistsAsUnscheduled"`
Expected: PASS (1 test)

Run full backend suite to confirm no regression: `dotnet test DCF.Tests/DCF.Tests.csproj`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Models/AdminRequests.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: thread nullable schedule time through Create/Update/Prefill show DTOs"
```

---

### Task 3: Scraper keeps unscheduled rows and captures the exhibition "conclude" time

**Files:**
- Modify: `DCF.Api/Scraping/IShowInfoScraperTask.cs`
- Modify: `DCF.Api/Scraping/ShowInfoScraperTask.cs`
- Test: `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs`

**Interfaces:**
- Produces: `ShowPrefillScheduleEntry.Time24h : string?` (was `string`) — Task 4 constructs these directly. `ShowInfoScraperTask.ScrapeAsync` no longer drops rows whose time cell doesn't parse (e.g. "TBD"); it keeps them with `Time24h = null`. `ScoresAnnouncedTime` extraction now also matches a label containing "conclude".

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs`, after the `CompetitiveHtml` constant (around line 70):

```csharp
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
```

Add these three tests after `ScrapeAsync_ConvertsPmTimeTo24h` (the last test in the file, around line 271):

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowInfoScraperTaskTests"`
Expected: FAIL —
- `ScrapeAsync_TbdRows_AreKeptInScheduleNotDropped`: `Assert.Equal(4, ...)` fails (actual is 2 — the TBD rows are dropped today).
- `ScrapeAsync_TbdRows_HaveNullTime24h`: throws `InvalidOperationException: Sequence contains no matching element` on `.Single(e => e.Label == "Blue Devils")`, since that row doesn't exist yet.
- `ScrapeAsync_ExhibitionShow_ScoresAnnouncedTimeParsesFromEventConcludesLabel`: `Assert.Equal("22:00", ...)` fails (actual is `null`).

- [ ] **Step 3: Implement**

In `DCF.Api/Scraping/IShowInfoScraperTask.cs`, change:
```csharp
public record ShowPrefillScheduleEntry(string Time24h, string Label);
```
to:
```csharp
public record ShowPrefillScheduleEntry(string? Time24h, string Label);
```

In `DCF.Api/Scraping/ShowInfoScraperTask.cs`, in `ParseScheduleEntries`, change:
```csharp
                var rawTime = cells[0].InnerText.Trim();
                var rawLabel = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
                var label = StripCity(rawLabel);
                var time24h = ConvertTo24h(rawTime);

                if (time24h is null)
                {
                    continue;
                }

                entries.Add(new ShowPrefillScheduleEntry(time24h, label));
```
to:
```csharp
                var rawTime = cells[0].InnerText.Trim();
                var rawLabel = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
                var label = StripCity(rawLabel);
                var time24h = ConvertTo24h(rawTime);

                entries.Add(new ShowPrefillScheduleEntry(time24h, label));
```

In the same file, in `ScrapeAsync`, change:
```csharp
        var scoresAnnouncedTime = filteredEntries
            .FirstOrDefault(e =>
                e.Label.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("recap", StringComparison.OrdinalIgnoreCase))
            ?.Time24h;
```
to:
```csharp
        var scoresAnnouncedTime = filteredEntries
            .FirstOrDefault(e =>
                e.Label.Contains("score", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("recap", StringComparison.OrdinalIgnoreCase) ||
                e.Label.Contains("conclude", StringComparison.OrdinalIgnoreCase))
            ?.Time24h;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowInfoScraperTaskTests"`
Expected: PASS (all tests in the file, including the 3 new ones and all pre-existing ones — `ScrapeAsync_ScheduleRetainsAllNonGateEntries`'s count of 4 against `ExhibitionHtml` is unaffected since that fixture has no TBD rows)

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Scraping/IShowInfoScraperTask.cs DCF.Api/Scraping/ShowInfoScraperTask.cs DCF.Tests/Scraping/ShowInfoScraperTaskTests.cs
git commit -m "feat: keep unscheduled (TBD) rows in scraped schedule instead of dropping them"
```

---

### Task 4: `PrefillShowAsync` includes TBD corps end-to-end

**Files:**
- Modify: `DCF.Tests/Services/ScrapeTestHelpers.cs`
- Test: `DCF.Tests/Services/AdminServiceTests.cs`

**Interfaces:**
- Consumes: `ShowPrefillScheduleEntry(string? Time24h, string Label)` (Task 3), `ShowPrefillScheduleEntryResponse.Time : string?` (Task 2), `IShowInfoScraperTask.ScrapeAsync(string url) : Task<ShowPrefillData?>`
- Produces: `FakeShowInfoScraperTask(ShowPrefillData? result) : IShowInfoScraperTask` in `DCF.Tests.Services`, usable via the existing `using static DCF.Tests.Services.ScrapeTestHelpers;` import.

This task adds no `AdminService.cs` changes — `PrefillShowAsync` already loops over every schedule entry to build `CorpsIds`, regardless of whether `Time24h` is null. This test proves that integration now holds true given Tasks 2 and 3.

- [ ] **Step 1: Write the failing test**

Add to `DCF.Tests/Services/AdminServiceTests.cs`, after `TriggerScrapeAsync_FailedScrape_ReturnsFailedOutcomeWithError` (the last test in the file):

```csharp
    [Fact]
    public async Task PrefillShowAsync_TbdScheduleEntry_CorpsIncludedAndTimeNull()
    {
        using var db = CreateDb("prefill_tbd_corps");
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026,
            StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
        };
        var timedCorps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Guardians" };
        var tbdCorps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        db.Seasons.Add(season);
        db.Corps.AddRange(timedCorps, tbdCorps);
        db.SeasonCorps.AddRange(
            new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = timedCorps.Id },
            new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = tbdCorps.Id }
        );
        await db.SaveChangesAsync();

        var prefillData = new ShowPrefillData(
            false, "San Antonio, TX", null, null,
            "13:30", "22:11", "CT",
            [
                new ShowPrefillScheduleEntry("13:40", "Guardians - McKinney, TX"),
                new ShowPrefillScheduleEntry(null, "Blue Devils - Concord, CA")
            ],
            "2026-08-15");

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), new FakeShowInfoScraperTask(prefillData));

        var result = await svc.PrefillShowAsync("Test Championship", season.Id);

        Assert.NotNull(result);
        Assert.Contains(timedCorps.Id, result!.CorpsIds);
        Assert.Contains(tbdCorps.Id, result.CorpsIds);

        var tbdEntry = result.Schedule.Single(e => e.Label.StartsWith("Blue Devils"));
        Assert.Null(tbdEntry.Time);
    }
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS0246: The type or namespace name 'FakeShowInfoScraperTask' could not be found`

- [ ] **Step 3: Add the fake**

In `DCF.Tests/Services/ScrapeTestHelpers.cs`, add after `FakeRecapScraperTask` (around line 35):

```csharp
internal sealed class FakeShowInfoScraperTask(ShowPrefillData? result) : IShowInfoScraperTask
{
    public Task<ShowPrefillData?> ScrapeAsync(string url)
    {
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build DCF.slnx`
Expected: SUCCESS

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PrefillShowAsync_TbdScheduleEntry_CorpsIncludedAndTimeNull"`
Expected: PASS (1 test)

Run the full backend suite one more time to confirm nothing else broke: `dotnet test DCF.Tests/DCF.Tests.csproj`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add DCF.Tests/Services/ScrapeTestHelpers.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "test: prove PrefillShowAsync includes TBD corps once scraper stops dropping them"
```

---

### Task 5: Frontend types + null-safety bug fixes

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Create: `DCF.Web/src/pages/SeasonDetail.test.tsx`

**Interfaces:**
- Produces: `export function buildDateTime(date: string, time: string, tz: string): string` (already existed, now exported); `export function buildScheduleEntryTime(date: string, time: string | null, tz: string): string | null`; `export function toNullableIso(time: string | null): string | null`. Task 6 uses the updated `ShowScheduleEntry`/`ShowPrefillScheduleEntry` types from this task.

This task fixes two real bugs the nullable backend contract (Tasks 1-4) would otherwise expose:
1. `addShow`'s schedule-payload mapping builds an invalid `Date` (`"...Tnull:00Z"`) and throws when a schedule entry has no time.
2. `saveShowEdit`'s schedule round-trip does `new Date(e.time).toISOString()`; `new Date(null)` silently evaluates to the Unix epoch instead of throwing, corrupting an unscheduled entry's time on any unrelated edit.

- [ ] **Step 1: Write the failing tests**

Create `DCF.Web/src/pages/SeasonDetail.test.tsx`:

```typescript
import { describe, it, expect } from 'vitest';
import { buildDateTime, buildScheduleEntryTime, toNullableIso } from './SeasonDetail';

describe('buildDateTime', () => {
  it('composes a date, HH:MM time, and timezone into a UTC ISO string', () => {
    expect(buildDateTime('2026-08-15', '19:00', 'ET')).toBe('2026-08-15T23:00:00.000Z');
  });
});

describe('buildScheduleEntryTime', () => {
  it('builds an ISO datetime when a time is present', () => {
    expect(buildScheduleEntryTime('2026-08-15', '19:00', 'ET')).toBe('2026-08-15T23:00:00.000Z');
  });

  it('returns null for an unscheduled (TBD) entry instead of throwing', () => {
    expect(buildScheduleEntryTime('2026-08-15', null, 'ET')).toBeNull();
  });
});

describe('toNullableIso', () => {
  it('converts an existing ISO time string to ISO', () => {
    expect(toNullableIso('2026-08-15T23:00:00.000Z')).toBe('2026-08-15T23:00:00.000Z');
  });

  it('returns null for an unscheduled (TBD) entry instead of the Unix epoch', () => {
    expect(toNullableIso(null)).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- SeasonDetail.test.tsx` (from `DCF.Web/`)
Expected: FAIL — `SyntaxError` / `does not provide an export named 'buildDateTime'` (it exists but isn't exported yet; `buildScheduleEntryTime` and `toNullableIso` don't exist at all).

- [ ] **Step 3: Update the shared types**

In `DCF.Web/src/types/api.ts`, change:
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
```
to:
```typescript
export interface ShowScheduleEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}

export interface ShowPrefillScheduleEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}
```

- [ ] **Step 4: Reuse the shared type in the API client instead of duplicating it**

In `DCF.Web/src/api/client.ts`, the `adminCreateShow` and `adminUpdateShow` body types each duplicate an inline `schedule` element type. Change both occurrences of:
```typescript
      schedule: { time: string; label: string; corpsId: string | null }[];
```
to:
```typescript
      schedule: ShowScheduleEntry[];
```

`ShowScheduleEntry` isn't imported yet. On line 1, change:
```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, Standing, UpdateLeagueRequest, UserProfile } from '../types/api';
```
to:
```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, ShowScheduleEntry, Standing, UpdateLeagueRequest, UserProfile } from '../types/api';
```

- [ ] **Step 5: Export `buildDateTime` and add the two null-safe helpers**

In `DCF.Web/src/pages/SeasonDetail.tsx`, change:
```typescript
function buildDateTime(date: string, time: string, tz: string): string {
  const d = new Date(`${date}T${time}:00Z`);
  d.setUTCHours(d.getUTCHours() + (TZ_HOURS[tz] ?? 4));
  return d.toISOString();
}
```
to:
```typescript
export function buildDateTime(date: string, time: string, tz: string): string {
  const d = new Date(`${date}T${time}:00Z`);
  d.setUTCHours(d.getUTCHours() + (TZ_HOURS[tz] ?? 4));
  return d.toISOString();
}

export function buildScheduleEntryTime(date: string, time: string | null, tz: string): string | null {
  return time ? buildDateTime(date, time, tz) : null;
}

export function toNullableIso(time: string | null): string | null {
  return time ? new Date(time).toISOString() : null;
}
```

- [ ] **Step 6: Fix `addShow`'s schedule-payload mapping**

In the same file, in `addShow`, change:
```typescript
      let rolloverDate = showDate;
      let prevTime = '';

      const schedulePayload = showSchedule.map(entry => {
        if (prevTime && entry.time < prevTime && prevTime >= '12:00') {
          const d = new Date(`${rolloverDate}T00:00:00`);
          d.setDate(d.getDate() + 1);
          rolloverDate = d.toISOString().slice(0, 10);
        }

        prevTime = entry.time;

        return {
          time: buildDateTime(rolloverDate, entry.time, showTz),
          label: entry.label,
          corpsId: entry.corpsId,
        };
      });
```
to:
```typescript
      let rolloverDate = showDate;
      let prevTime = '';

      const schedulePayload = showSchedule.map(entry => {
        if (entry.time && prevTime && entry.time < prevTime && prevTime >= '12:00') {
          const d = new Date(`${rolloverDate}T00:00:00`);
          d.setDate(d.getDate() + 1);
          rolloverDate = d.toISOString().slice(0, 10);
        }

        if (entry.time) {
          prevTime = entry.time;
        }

        return {
          time: buildScheduleEntryTime(rolloverDate, entry.time, showTz),
          label: entry.label,
          corpsId: entry.corpsId,
        };
      });
```

- [ ] **Step 7: Fix `saveShowEdit`'s schedule round-trip**

In the same file, in `saveShowEdit`, change:
```typescript
        schedule: show.schedule.map(e => ({
          time: new Date(e.time).toISOString(),
          label: e.label,
          corpsId: e.corpsId,
        })),
```
to:
```typescript
        schedule: show.schedule.map(e => ({
          time: toNullableIso(e.time),
          label: e.label,
          corpsId: e.corpsId,
        })),
```

- [ ] **Step 8: Run tests and type-check to verify everything passes**

Run: `npm test` (from `DCF.Web/`)
Expected: PASS — all new tests plus all pre-existing ones (e.g. `TimePicker.test.tsx`)

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — `tsc -b && vite build` completes with no type errors (this exercises every call site touched by the `ShowScheduleEntry`/`ShowPrefillScheduleEntry` type change)

- [ ] **Step 9: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/pages/SeasonDetail.test.tsx
git commit -m "fix: handle unscheduled (null) schedule entry times without crashing or corrupting data"
```

---

### Task 6: Display "TBD" and unify the Scores/Concludes field

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Consumes: `ShowScheduleEntry.time : string | null`, `ShowPrefillScheduleEntry.time : string | null` (Task 5)

This task has no dedicated automated test — it's display copy and a validation-message change with no pure-function seam to unit test in isolation, matching the "no full component-test effort for `SeasonDetail`" boundary set in the approved spec. It's verified manually in Task 7.

- [ ] **Step 1: Render "TBD" in the Add Show schedule preview**

In `DCF.Web/src/pages/SeasonDetail.tsx`, in the Add Show form's schedule preview, change:
```jsx
                        {showSchedule.map((entry, i) => (
                          <div key={i} style={{ display: 'flex', gap: 8, padding: '2px 0' }}>
                            <span style={{ minWidth: 36, fontVariantNumeric: 'tabular-nums' }}>{entry.time}</span>
                            <span>{entry.label}</span>
                          </div>
                        ))}
```
to:
```jsx
                        {showSchedule.map((entry, i) => (
                          <div key={i} style={{ display: 'flex', gap: 8, padding: '2px 0' }}>
                            <span style={{
                              minWidth: 36, fontVariantNumeric: 'tabular-nums',
                              color: entry.time ? undefined : 'var(--text-faint)',
                            }}>
                              {entry.time ?? 'TBD'}
                            </span>
                            <span>{entry.label}</span>
                          </div>
                        ))}
```

- [ ] **Step 2: Dynamic label in the Add Show form**

In the same file, in the Add Show form's Start/Scores row, change:
```jsx
                  <div className="admin-show-form-pair">
                    <label style={labelStyle}>Scores</label>
                    <TimePicker value={showScoresTime} onChange={setShowScoresTime} required style={{ flex: 1 }} />
                  </div>
```
to:
```jsx
                  <div className="admin-show-form-pair">
                    <label style={labelStyle}>{isExhibition ? 'Concludes' : 'Scores'}</label>
                    <TimePicker value={showScoresTime} onChange={setShowScoresTime} required style={{ flex: 1 }} />
                  </div>
```

- [ ] **Step 3: Dynamic label in the Edit Show form**

In the same file, in the expanded show card's edit form Start/Scores row, change:
```jsx
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>Scores</label>
                            <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                          </div>
```
to:
```jsx
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>{s.isExhibition ? 'Concludes' : 'Scores'}</label>
                            <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                          </div>
```
(`s` is the current show from the enclosing `shows.map(s => { ... })` — the same reference already used a few lines above for `{!s.isExhibition && (...)}`.)

- [ ] **Step 4: Require the Scores/Concludes time for both show types**

In the same file, in `addShow`, change:
```typescript
    if (!isExhibition && showCorpsIds.size === 0) { setError('Select at least one corps.'); return; }
    if (!isExhibition && !showScoresTime) { setError('Scores announced time is required for competitive shows.'); return; }
```
to:
```typescript
    if (!isExhibition && showCorpsIds.size === 0) { setError('Select at least one corps.'); return; }
    if (!showScoresTime) { setError('Scores/concludes time is required.'); return; }
```

- [ ] **Step 5: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS

Run: `npm run lint` (from `DCF.Web/`)
Expected: SUCCESS, no new warnings/errors

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (regression check — no test targets this task's JSX directly, but confirms nothing else broke)

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: show TBD for unscheduled corps and unify Scores/Concludes time field"
```

---

### Task 7: End-to-end manual verification

**Files:** none (verification only — no commit at the end of this task)

**Interfaces:**
- Consumes: everything produced by Tasks 1-6.

- [ ] **Step 1: Start the local stack**

```bash
docker compose up -d postgres mosquitto mailpit
dotnet ef database update --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
dotnet run --project DCF.Api/DCF.Api.csproj
```
In a second terminal, from `DCF.Web/`:
```bash
npm run dev
```
Expected: API listening (default `http://localhost:5000`-ish per launch settings), Vite dev server on `http://localhost:5173`, no startup errors in either terminal.

- [ ] **Step 2: Verify TBD corps are captured on show creation**

In the browser, sign in (dev auth bypass applies automatically), go to an admin season's detail page, open "Add Show". Enter a show name that resolves to a real DCI event page with unscheduled corps — as of 2026-07-07, `DCI Southwestern Championship` (year matching the season) resolves to `https://www.dci.org/events/2026-dci-southwestern-championship/`, which has 6 timed corps and 16 TBD corps. If that page no longer has TBD entries by the time this is run (DCI may have since published full times), find another current-season show that still shows "TBD" times on dci.org and use that instead — the specific show doesn't matter, only that it has at least one TBD row.

Click "Fetch from DCI". Confirm:
- The "Participating Corps" chip list includes corps that only appear in the TBD section (not just the timed ones).
- The "Schedule" preview shows "TBD" (in muted/faint styling, distinct from timed entries) for those corps instead of a blank or broken time.

- [ ] **Step 3: Verify the show saves and reloads without crashing or losing TBD status**

Submit the Add Show form. Confirm no crash and the show appears in the list. Reload the season page. Expand the newly created show, confirm it opens cleanly. Make an unrelated edit (e.g. change the name slightly) and Save. Reload again and re-expand the show — confirm the save succeeded and nothing errored (this is the regression check for the epoch-corruption bug: before Task 5's fix, this save would have silently overwritten every TBD entry's time with the Unix epoch).

- [ ] **Step 4: Verify the Concludes label for exhibition shows**

In "Add Show", check the "Exhibition" checkbox. Confirm the time-field label next to the second `TimePicker` changes from "Scores" to "Concludes". Uncheck it and confirm the label reverts to "Scores".

- [ ] **Step 5: Run the full automated suites one more time**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj
```
Expected: all PASS

```bash
npm test
```
(from `DCF.Web/`)
Expected: all PASS

- [ ] **Step 6: Stop local services**

```bash
docker compose down
```
