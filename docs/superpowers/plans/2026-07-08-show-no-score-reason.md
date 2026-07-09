# Show No-Score Reason + Exhibition Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let admins mark a competitive show as not receiving scores (with a free-text reason), which cancels/skips its automatic scrape scheduling and replaces its status badge — and give exhibition shows a computed "Completed" badge once their concludes time passes.

**Architecture:** A new nullable `ShowEntity.NoScoreReason` is set/cleared through a dedicated always-available endpoint (not the existing lockable show-edit form). `ScrapeSchedulerService` gains a public `CancelScheduledScrape` and a guard so a reason-marked show's automatic scrape is cancelled/skipped, while manual "Trigger Score Scrape" stays possible at the API layer. The frontend computes both the no-score badge and the exhibition "Completed" badge on read — no new stored/scheduled lifecycle status, mirroring the existing `hasStarted`/`hasScoresAnnounced` pattern rather than `SeasonStatus`'s background-scheduler pattern.

**Tech Stack:** ASP.NET Core / EF Core (Npgsql, InMemory for tests) on the backend; React + TypeScript + Vitest on the frontend.

## Global Constraints

- All work in this plan happens on branch `feat/show-no-score-reason` (already created off the updated `master`; the design spec commit is already on it). Commit after every task.
- EF Core migrations: `dotnet ef migrations add <Name> --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj`. Apply with `dotnet ef database update` using the same two flags.
- `IsExhibition` is not touched anywhere in this plan — it stays immutable, set only at show creation. This was an explicit decision in the approved spec (`docs/superpowers/specs/2026-07-08-show-no-score-reason-design.md`), not an oversight: a storm-cancelled competitive show stays a competitive show with a reason attached, not a reclassified exhibition.
- `ScrapeStatus` (`NotStarted`/`Succeeded`/`Failed`) gets no new value. It stays a strict record of "what happened on the last scrape attempt"; `NoScoreReason` is the orthogonal "why we're not expecting scores" signal. Do not conflate them.
- `CreateShowRequest`/`UpdateShowRequest`/`AdminService.CreateShowAsync`/`UpdateShowAsync` are not touched — `NoScoreReason` is reachable only through the new dedicated endpoint added in Task 3, which is deliberately never subject to the existing show-edit lock.
- `ScrapeSchedulerService.ExecuteScrapeAsync`/`ExecuteScrapeWithRetriesAsync` (the manual "Trigger Score Scrape" path, via `AdminService.TriggerScrapeAsync`) are **not** given a `NoScoreReason` guard in this plan — only `ScheduleScrape` (the automatic path) and the startup reconciliation query are. This is deliberate: an admin's explicit manual trigger stays possible as an override even on a reason-marked show. Do not add a guard to `ExecuteScrapeAsync` for "consistency" — the frontend hides the trigger button instead (Task 5).
- Backend tests use EF Core InMemory: `new DbContextOptionsBuilder<DcfDbContext>().UseInMemoryDatabase(name).Options`, one unique `name` string per test, matching the existing convention in `DCF.Tests/Services/AdminServiceTests.cs` and `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`.
- `ScrapeSchedulerService` tests that need to prove a scrape did or didn't fire use short real delays (tens to hundreds of milliseconds) plus a generous `Task.Delay` margin before asserting — this is the existing convention in this file (see `RetryIntervalMinutes = "0"` in the pre-existing retry tests), not something to "fix" by mocking time.
- Frontend tests use Vitest, colocated as `<name>.test.ts`/`.test.tsx` next to the source file, matching `SeasonDetail.helpers.test.ts`.

---

## File Structure

**Backend — modified:**
- `DCF.Data/Entities/ShowEntity.cs` — new `NoScoreReason` property.
- `DCF.Data/Migrations/` — new generated migration (`dotnet ef migrations add`), not hand-written.
- `DCF.Api/Services/ScrapeSchedulerService.cs` — extract `CancelScheduledScrape`; guard `ScheduleScrape` and the startup reconciliation query on `NoScoreReason`.
- `DCF.Api/Models/AdminRequests.cs` — new `SetNoScoreReasonRequest` record.
- `DCF.Api/Services/IAdminService.cs` — new `SetNoScoreReasonAsync` method signature.
- `DCF.Api/Services/AdminService.cs` — `ShowSummary` gains `NoScoreReason`; `GetShowsAsync` projects it; new `SetNoScoreReasonAsync` method.
- `DCF.Api/Controllers/AdminController.cs` — new `PATCH shows/{id}/no-score-reason` endpoint.

**Backend — tests modified:**
- `DCF.Tests/Services/AdminServiceTests.cs` — 1 entity test + 4 `SetNoScoreReasonAsync` tests.
- `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs` — 2 new tests.

**Frontend — modified:**
- `DCF.Web/src/types/api.ts` — `Show.noScoreReason: string | null`.
- `DCF.Web/src/api/client.ts` — new `adminSetNoScoreReason`.
- `DCF.Web/src/pages/SeasonDetail.helpers.ts` — `hasStarted`/`hasScoresAnnounced` moved in from `SeasonDetail.tsx`; new `getShowStatusBadge`.
- `DCF.Web/src/pages/SeasonDetail.helpers.test.ts` — tests for `getShowStatusBadge`.
- `DCF.Web/src/pages/SeasonDetail.tsx` — import the moved helpers instead of defining them locally; new always-available "Mark No Scores"/"Clear" control; badge rendering now uses `getShowStatusBadge`; "Trigger Score Scrape" hidden when a reason is set.

---

### Task 1: Add `ShowEntity.NoScoreReason` + migration

**Files:**
- Modify: `DCF.Data/Entities/ShowEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddShowNoScoreReason.cs` (+ `.Designer.cs`, snapshot update) — generated, not hand-written
- Test: `DCF.Tests/Services/AdminServiceTests.cs`

**Interfaces:**
- Produces: `ShowEntity.NoScoreReason : string?` — every later task reads or writes this property.

- [ ] **Step 1: Write the failing test**

Add to `DCF.Tests/Services/AdminServiceTests.cs`, right after `DeleteShowAsync_AlsoDeletesScheduleEntries` (ends at line 637):

```csharp
    [Fact]
    public async Task ShowEntity_NoScoreReason_PersistsAndDefaultsToNull()
    {
        using var db = CreateDb("show_entity_no_score_reason");

        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show",
            Date = new DateOnly(2030, 7, 4), SeasonId = Guid.NewGuid()
        };

        db.Shows.Add(show);
        await db.SaveChangesAsync();

        Assert.Null(db.Shows.Single(s => s.Id == show.Id).NoScoreReason);

        show.NoScoreReason = "Storm forced standstill exhibition";
        await db.SaveChangesAsync();

        Assert.Equal("Storm forced standstill exhibition", db.Shows.Single(s => s.Id == show.Id).NoScoreReason);
    }
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS1061: 'ShowEntity' does not contain a definition for 'NoScoreReason'`, pointing at the test just added.

- [ ] **Step 3: Add the property**

In `DCF.Data/Entities/ShowEntity.cs`, change:

```csharp
    public ScrapeStatus ScrapeStatus { get; set; } = ScrapeStatus.NotStarted;
    public DateTimeOffset? LastScrapeAttemptAt { get; set; }
    public string? ScrapeError { get; set; }
    public Guid SeasonId { get; set; }
```

to:

```csharp
    public ScrapeStatus ScrapeStatus { get; set; } = ScrapeStatus.NotStarted;
    public DateTimeOffset? LastScrapeAttemptAt { get; set; }
    public string? ScrapeError { get; set; }
    public string? NoScoreReason { get; set; }
    public Guid SeasonId { get; set; }
```

- [ ] **Step 4: Generate the migration**

Run:
```bash
dotnet ef migrations add AddShowNoScoreReason --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
```
Expected: creates `DCF.Data/Migrations/<timestamp>_AddShowNoScoreReason.cs` and `.Designer.cs`, and updates `DcfDbContextModelSnapshot.cs`. The generated `Up()` should call `migrationBuilder.AddColumn<string>(name: "NoScoreReason", table: "Shows", type: "text", nullable: true);` (or equivalent) — inspect the generated file to confirm it only adds this one column.

- [ ] **Step 5: Run build and test to verify it passes**

Run: `dotnet build DCF.slnx`
Expected: SUCCESS

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ShowEntity_NoScoreReason_PersistsAndDefaultsToNull"`
Expected: PASS (1 test)

- [ ] **Step 6: Commit**

```bash
git add DCF.Data/Entities/ShowEntity.cs DCF.Data/Migrations/ DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: add NoScoreReason to ShowEntity"
```

---

### Task 2: `ScrapeSchedulerService` respects `NoScoreReason`

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`
- Test: `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`

**Interfaces:**
- Consumes: `ShowEntity.NoScoreReason : string?` (Task 1)
- Produces: `ScrapeSchedulerService.CancelScheduledScrape(Guid showId) : void` (new public method) — Task 3 calls this directly. `ScheduleScrape(ShowEntity show)` now also returns early when `show.NoScoreReason != null`.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`, after the last test (`ExecuteScrapeWithRetriesAsync_EventuallySucceeds_DoesNotSendAlertEmail`, ends at line 341), before the closing class brace:

```csharp
    [Fact]
    public async Task ScheduleScrape_ShowHasNoScoreReason_DoesNotExecuteScrape()
    {
        using var db = CreateDb("schedule_skips_no_score_reason");
        var show = CreateShow();
        show.NoScoreReason = "Storm forced standstill exhibition";
        show.ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddMilliseconds(50);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?> { ["Scraper:DelayMinutes"] = "0" });

        svc.ScheduleScrape(show);

        await Task.Delay(300);

        Assert.Equal(0, scraperTask.CallCount);
    }

    [Fact]
    public async Task CancelScheduledScrape_PendingScrape_PreventsExecution()
    {
        using var db = CreateDb("cancel_scheduled_scrape");
        var show = CreateShow();
        show.ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddMilliseconds(100);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?> { ["Scraper:DelayMinutes"] = "0" });

        svc.ScheduleScrape(show);
        svc.CancelScheduledScrape(show.Id);

        await Task.Delay(400);

        Assert.Equal(0, scraperTask.CallCount);
    }
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS1061: 'ScrapeSchedulerService' does not contain a definition for 'CancelScheduledScrape'`, from the second test.

- [ ] **Step 3: Implement**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, change:

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
```

to:

```csharp
    public void ScheduleScrape(ShowEntity show)
    {
        if (show.IsExhibition || show.Url is null || show.ScoresAnnouncedTime is null || show.NoScoreReason != null)
        {
            return;
        }

        CancelScheduledScrape(show.Id);

        var cts = new CancellationTokenSource();
        _scheduled[show.Id] = cts;
```

Then add the new method right after `ScheduleScrape`'s closing brace (before `ExecuteScrapeWithRetriesAsync`):

```csharp
    public void CancelScheduledScrape(Guid showId)
    {
        if (_scheduled.TryRemove(showId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
    }
```

Finally, in `ExecuteAsync`, change:

```csharp
        var shows = await db.Shows
            .Include(s => s.ShowCorps)
            .Where(s => !s.IsExhibition
                     && s.Url != null
                     && s.ScoresAnnouncedTime.HasValue
                     && s.ScoresAnnouncedTime.Value > DateTimeOffset.UtcNow)
            .ToListAsync(stoppingToken);
```

to:

```csharp
        var shows = await db.Shows
            .Include(s => s.ShowCorps)
            .Where(s => !s.IsExhibition
                     && s.Url != null
                     && s.ScoresAnnouncedTime.HasValue
                     && s.ScoresAnnouncedTime.Value > DateTimeOffset.UtcNow
                     && s.NoScoreReason == null)
            .ToListAsync(stoppingToken);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"`
Expected: PASS (all tests in the file, including the 2 new ones)

Run full backend suite to confirm no regression: `dotnet test DCF.Tests/DCF.Tests.csproj`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeSchedulerServiceTests.cs
git commit -m "feat: cancel/skip scheduled scrapes for shows marked with a no-score reason"
```

---

### Task 3: `AdminService.SetNoScoreReasonAsync` + endpoint

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Test: `DCF.Tests/Services/AdminServiceTests.cs`

**Interfaces:**
- Consumes: `ShowEntity.NoScoreReason : string?` (Task 1); `ScrapeSchedulerService.CancelScheduledScrape(Guid showId) : void` (Task 2); `ScrapeSchedulerService.ScheduleScrape(ShowEntity show) : void` (existing, now guards on `NoScoreReason` per Task 2).
- Produces: `IAdminService.SetNoScoreReasonAsync(Guid id, string? reason) : Task<bool>`; `AdminRequests.SetNoScoreReasonRequest(string? Reason)`; `ShowSummary.NoScoreReason : string?`; endpoint `PATCH /api/admin/shows/{id}/no-score-reason` — Task 5's frontend control calls this.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Tests/Services/AdminServiceTests.cs`, after the last test in the file (`PrefillShowAsync_TbdScheduleEntry_CorpsIncludedAndTimeNull`, ends at line 739), before the closing class brace:

```csharp
    [Fact]
    public async Task SetNoScoreReasonAsync_SetsReason_CancelsScheduledScrapeAndAppearsInShowSummary()
    {
        using var db = CreateDb("set_no_score_reason_sets");
        var seasonId = Guid.NewGuid();
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddMilliseconds(100),
            SeasonId = seasonId
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var scrapeScheduler = CreateSvc(db, scraperTask, new Dictionary<string, string?> { ["Scraper:DelayMinutes"] = "0" });
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        scrapeScheduler.ScheduleScrape(show);

        var result = await svc.SetNoScoreReasonAsync(show.Id, "Storm forced standstill exhibition");

        await Task.Delay(400);

        Assert.True(result);
        Assert.Equal(0, scraperTask.CallCount);

        var summary = (await svc.GetShowsAsync(seasonId)).Single();
        Assert.Equal("Storm forced standstill exhibition", summary.NoScoreReason);
    }

    [Fact]
    public async Task SetNoScoreReasonAsync_ClearsReason_ReschedulesScrape()
    {
        using var db = CreateDb("set_no_score_reason_clears");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddMilliseconds(-1000),
            NoScoreReason = "Storm forced standstill exhibition",
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask(failuresBeforeSuccess: 0);
        var scrapeScheduler = CreateSvc(db, scraperTask, new Dictionary<string, string?> { ["Scraper:DelayMinutes"] = "0" });
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var result = await svc.SetNoScoreReasonAsync(show.Id, null);

        await Task.Delay(300);

        Assert.True(result);
        Assert.Null(db.Shows.Single(s => s.Id == show.Id).NoScoreReason);
        Assert.Equal(1, scraperTask.CallCount);
    }

    [Fact]
    public async Task SetNoScoreReasonAsync_WhitespaceReason_NormalizesToNull()
    {
        using var db = CreateDb("set_no_score_reason_whitespace");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show",
            Date = new DateOnly(2026, 7, 4), NoScoreReason = "Old reason",
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var result = await svc.SetNoScoreReasonAsync(show.Id, "   ");

        Assert.True(result);
        Assert.Null(db.Shows.Single(s => s.Id == show.Id).NoScoreReason);
    }

    [Fact]
    public async Task SetNoScoreReasonAsync_MissingShow_ReturnsFalse()
    {
        using var db = CreateDb("set_no_score_reason_missing");

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus(), null!);

        var result = await svc.SetNoScoreReasonAsync(Guid.NewGuid(), "Cancelled");

        Assert.False(result);
    }
```

- [ ] **Step 2: Run build to verify it fails**

Run: `dotnet build DCF.slnx`
Expected: FAIL — `CS1061: 'AdminService' does not contain a definition for 'SetNoScoreReasonAsync'`

- [ ] **Step 3: Implement**

In `DCF.Api/Models/AdminRequests.cs`, add after `UpdateShowRequest` (after its closing `);` on line 31):

```csharp
public record SetNoScoreReasonRequest(string? Reason);
```

In `DCF.Api/Services/IAdminService.cs`, change:

```csharp
    Task<bool> DeleteShowAsync(Guid id);
    Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId);
}
```

to:

```csharp
    Task<bool> DeleteShowAsync(Guid id);
    Task<ShowPrefillResponse?> PrefillShowAsync(string showName, Guid seasonId);
    Task<bool> SetNoScoreReasonAsync(Guid id, string? reason);
}
```

In `DCF.Api/Services/AdminService.cs`, change the `ShowSummary` record:

```csharp
public record ShowSummary(
    Guid Id, string Name, string? Url, DateOnly Date, DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime, string? Timezone, bool IsExhibition,
    string? Location, double? Latitude, double? Longitude,
    ScrapeStatus ScrapeStatus, DateTimeOffset? LastScrapeAttemptAt, string? ScrapeError,
    IEnumerable<Guid> CorpsIds, IEnumerable<ShowScheduleEntryResponse> Schedule);
```

to:

```csharp
public record ShowSummary(
    Guid Id, string Name, string? Url, DateOnly Date, DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime, string? Timezone, bool IsExhibition,
    string? Location, double? Latitude, double? Longitude,
    ScrapeStatus ScrapeStatus, DateTimeOffset? LastScrapeAttemptAt, string? ScrapeError,
    string? NoScoreReason,
    IEnumerable<Guid> CorpsIds, IEnumerable<ShowScheduleEntryResponse> Schedule);
```

In the same file, change `GetShowsAsync`'s projection:

```csharp
        return shows.Select(s => new ShowSummary(
            s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime, s.Timezone,
            s.IsExhibition, s.Location, s.Latitude, s.Longitude,
            s.ScrapeStatus, s.LastScrapeAttemptAt, s.ScrapeError,
            s.ShowCorps.Select(sc => sc.CorpsId),
            s.Schedule.OrderBy(e => e.SortOrder)
                .Select(e => new ShowScheduleEntryResponse(e.Time, e.Label, e.CorpsId))))
            .ToList();
```

to:

```csharp
        return shows.Select(s => new ShowSummary(
            s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime, s.Timezone,
            s.IsExhibition, s.Location, s.Latitude, s.Longitude,
            s.ScrapeStatus, s.LastScrapeAttemptAt, s.ScrapeError, s.NoScoreReason,
            s.ShowCorps.Select(sc => sc.CorpsId),
            s.Schedule.OrderBy(e => e.SortOrder)
                .Select(e => new ShowScheduleEntryResponse(e.Time, e.Label, e.CorpsId))))
            .ToList();
```

Then add the new method after `UpdateShowAsync` (after its closing brace, before `TriggerScrapeAsync`):

```csharp
    public async Task<bool> SetNoScoreReasonAsync(Guid id, string? reason)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        show.NoScoreReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await db.SaveChangesAsync();

        if (show.NoScoreReason != null)
        {
            scrapeScheduler?.CancelScheduledScrape(show.Id);
        }
        else
        {
            scrapeScheduler?.ScheduleScrape(show);
        }

        return true;
    }
```

In `DCF.Api/Controllers/AdminController.cs`, add after `UpdateShow` (after its closing brace, before `PrefillShow`):

```csharp
    [HttpPatch("shows/{id}/no-score-reason")]
    public async Task<IActionResult> SetNoScoreReason(Guid id, SetNoScoreReasonRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.SetNoScoreReasonAsync(id, req.Reason) ? NoContent() : NotFound();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SetNoScoreReasonAsync"`
Expected: PASS (4 tests)

Run the full backend suite to confirm no regression: `dotnet test DCF.Tests/DCF.Tests.csproj`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: add admin endpoint to set/clear a show's no-score reason"
```

---

### Task 4: Frontend helpers — `getShowStatusBadge` + API plumbing

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`

**Interfaces:**
- Produces: `Show.noScoreReason : string | null`; `api.adminSetNoScoreReason(id: string, reason: string | null) : Promise<void>`; `hasStarted(show: Show): boolean` and `hasScoresAnnounced(show: Show): boolean` (moved here, same behavior as before); `getShowStatusBadge(show: Show): { label: string; color: string } | null`. Task 5 imports all three functions from here.

Note: after this task, `hasStarted`/`hasScoresAnnounced` exist in **both** `SeasonDetail.helpers.ts` (new, exported) and `SeasonDetail.tsx` (existing, local, unexported) — harmless, since neither file imports the other's copy yet. Task 5 removes the local copies from `SeasonDetail.tsx` and switches it to import from here.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`, changing the import on line 2:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso } from './SeasonDetail.helpers';
```

to:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge } from './SeasonDetail.helpers';
import type { Show } from '../types/api';
```

Then add at the end of the file:

```typescript

function makeShow(overrides: Partial<Show> = {}): Show {
  return {
    id: 'show-1',
    name: 'Test Show',
    date: '2026-08-15',
    isExhibition: false,
    corpsIds: [],
    scrapeStatus: 'NotStarted',
    schedule: [],
    noScoreReason: null,
    ...overrides,
  };
}

describe('getShowStatusBadge', () => {
  const past = new Date(Date.now() - 60 * 60 * 1000).toISOString();
  const future = new Date(Date.now() + 60 * 60 * 1000).toISOString();

  it('returns a NO SCORES badge when noScoreReason is set, regardless of other state', () => {
    const show = makeShow({ noScoreReason: 'Storm forced standstill exhibition', startTime: past, scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'NO SCORES', color: 'var(--red)' });
  });

  it('returns a COMPLETED badge for an exhibition show whose concludes time has passed', () => {
    const show = makeShow({ isExhibition: true, scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'COMPLETED', color: 'var(--green)' });
  });

  it('does not return COMPLETED for an exhibition show whose concludes time has not passed', () => {
    const show = makeShow({ isExhibition: true, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toBeNull();
  });

  it('returns a SCORES ANNOUNCED badge for a competitive show once scores time has passed', () => {
    const show = makeShow({ scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'SCORES ANNOUNCED', color: 'var(--green)' });
  });

  it('returns a STARTED badge once start time has passed but scores have not been announced', () => {
    const show = makeShow({ startTime: past, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toEqual({ label: 'STARTED', color: 'var(--accent)' });
  });

  it('returns null for a show that has not started', () => {
    const show = makeShow({ startTime: future, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: FAIL — `does not provide an export named 'getShowStatusBadge'`, and a TypeScript error that `noScoreReason` doesn't exist on `Show` in the `makeShow` factory.

- [ ] **Step 3: Add the `noScoreReason` field, the client method, and the helper**

In `DCF.Web/src/types/api.ts`, change:

```typescript
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

to:

```typescript
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
  noScoreReason: string | null;
  schedule: ShowScheduleEntry[];
}
```

In `DCF.Web/src/api/client.ts`, change:

```typescript
  adminDeleteShow: (id: string) =>
    request<void>(`/api/admin/shows/${id}`, { method: 'DELETE' }),
  adminPrefillShow: (seasonId: string, name: string) =>
```

to:

```typescript
  adminDeleteShow: (id: string) =>
    request<void>(`/api/admin/shows/${id}`, { method: 'DELETE' }),
  adminSetNoScoreReason: (id: string, reason: string | null) =>
    request<void>(`/api/admin/shows/${id}/no-score-reason`, { method: 'PATCH', body: JSON.stringify({ reason }) }),
  adminPrefillShow: (seasonId: string, name: string) =>
```

In `DCF.Web/src/pages/SeasonDetail.helpers.ts`, change:

```typescript
export const TZ_HOURS: Record<string, number> = { PT: 7, MT: 6, CT: 5, ET: 4 };
```

to:

```typescript
import type { Show } from '../types/api';

export const TZ_HOURS: Record<string, number> = { PT: 7, MT: 6, CT: 5, ET: 4 };
```

Then add at the end of the file:

```typescript

export function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}

export function hasScoresAnnounced(show: Show): boolean {
  return !!show.scoresAnnouncedTime && new Date(show.scoresAnnouncedTime) <= new Date();
}

export interface ShowStatusBadge {
  label: string;
  color: string;
}

export function getShowStatusBadge(show: Show): ShowStatusBadge | null {
  if (show.noScoreReason) {
    return { label: 'NO SCORES', color: 'var(--red)' };
  }

  if (show.isExhibition && hasScoresAnnounced(show)) {
    return { label: 'COMPLETED', color: 'var(--green)' };
  }

  if (hasScoresAnnounced(show)) {
    return { label: 'SCORES ANNOUNCED', color: 'var(--green)' };
  }

  if (hasStarted(show)) {
    return { label: 'STARTED', color: 'var(--accent)' };
  }

  return null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: PASS (all tests in the file, including the 6 new ones)

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — confirms `Show.noScoreReason` doesn't break any other call site (it won't yet, since nothing else reads it until Task 5).

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts DCF.Web/src/pages/SeasonDetail.helpers.ts DCF.Web/src/pages/SeasonDetail.helpers.test.ts
git commit -m "feat: add getShowStatusBadge helper and no-score-reason API plumbing"
```

---

### Task 5: Wire the control and badges into `SeasonDetail.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Consumes: `hasStarted`, `hasScoresAnnounced`, `getShowStatusBadge` (Task 4, from `SeasonDetail.helpers.ts`); `Show.noScoreReason : string | null` (Task 4); `api.adminSetNoScoreReason` (Task 4).

This task has no dedicated automated test — it's UI wiring with no new pure-function seam (the logic worth unit-testing was already extracted and tested in Task 4), matching the "no full component-test effort for `SeasonDetail`" boundary set in the approved spec. It's verified manually in Task 6.

- [ ] **Step 1: Import the moved helpers instead of defining them locally**

In `DCF.Web/src/pages/SeasonDetail.tsx`, change:

```typescript
import { TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso } from './SeasonDetail.helpers';

function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}

function hasScoresAnnounced(show: Show): boolean {
  return !!show.scoresAnnouncedTime && new Date(show.scoresAnnouncedTime) <= new Date();
}
```

to:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge,
} from './SeasonDetail.helpers';
```

- [ ] **Step 2: Add state for the reason input**

Change:

```typescript
  const [savingShowEdit, setSavingShowEdit] = useState(false);
  const [deletingShowId, setDeletingShowId] = useState<string | null>(null);
```

to:

```typescript
  const [savingShowEdit, setSavingShowEdit] = useState(false);
  const [deletingShowId, setDeletingShowId] = useState<string | null>(null);
  const [noScoreReasonInput, setNoScoreReasonInput] = useState('');
  const [savingNoScoreReason, setSavingNoScoreReason] = useState(false);
```

- [ ] **Step 3: Seed/reset the input in `expandShow`**

Change:

```typescript
  function expandShow(show: Show) {
    if (expandedShowId === show.id) {
      setExpandedShowId(null);
      setEditShow(null);
      return;
    }
    const tz = show.timezone ?? 'ET';
    const toHHMM = (iso: string) => {
      const d = new Date(iso);
      d.setUTCHours(d.getUTCHours() - (TZ_HOURS[tz] ?? 4));
      return d.toISOString().slice(11, 16);
    };
    setExpandedShowId(show.id);
    setEditShow({
      name: show.name,
      url: show.url ?? '',
      date: show.date,
      startTime: show.startTime ? toHHMM(show.startTime) : '',
      scoresTime: show.scoresAnnouncedTime ? toHHMM(show.scoresAnnouncedTime) : '',
      tz,
      corpsIds: new Set(show.corpsIds),
    });
  }
```

to:

```typescript
  function expandShow(show: Show) {
    if (expandedShowId === show.id) {
      setExpandedShowId(null);
      setEditShow(null);
      setNoScoreReasonInput('');
      return;
    }
    const tz = show.timezone ?? 'ET';
    const toHHMM = (iso: string) => {
      const d = new Date(iso);
      d.setUTCHours(d.getUTCHours() - (TZ_HOURS[tz] ?? 4));
      return d.toISOString().slice(11, 16);
    };
    setExpandedShowId(show.id);
    setEditShow({
      name: show.name,
      url: show.url ?? '',
      date: show.date,
      startTime: show.startTime ? toHHMM(show.startTime) : '',
      scoresTime: show.scoresAnnouncedTime ? toHHMM(show.scoresAnnouncedTime) : '',
      tz,
      corpsIds: new Set(show.corpsIds),
    });
    setNoScoreReasonInput(show.noScoreReason ?? '');
  }
```

- [ ] **Step 4: Add the save/clear handler**

Add after `deleteShow` (after its closing `};`, before `if (!season) {`):

```typescript
  const saveNoScoreReason = async (showId: string, reason: string | null) => {
    if (savingNoScoreReason) return;
    setSavingNoScoreReason(true);
    setError(null);

    try {
      await api.adminSetNoScoreReason(showId, reason);

      const updated = await api.adminGetShows(id!);

      setShows(updated);
      setNoScoreReasonInput(reason ?? '');
    } catch {
      setError('Failed to update no-score reason.');
    } finally {
      setSavingNoScoreReason(false);
    }
  };
```

- [ ] **Step 5: Compute the badge alongside `started`**

Change:

```typescript
          {shows.map(s => {
            const expanded = expandedShowId === s.id;
            const started = hasStarted(s);

            return (
```

to:

```typescript
          {shows.map(s => {
            const expanded = expandedShowId === s.id;
            const started = hasStarted(s);
            const statusBadge = getShowStatusBadge(s);

            return (
```

- [ ] **Step 6: Replace the badge JSX**

Change:

```jsx
                      {hasScoresAnnounced(s)
                        ? <span style={{ color: 'var(--green)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCORES ANNOUNCED</span>
                        : started && <span style={{ color: 'var(--accent)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>STARTED</span>
                      }
                      {s.scrapeStatus === 'Succeeded'
                        ? <span style={{ color: 'var(--green)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCRAPE COMPLETED</span>
                        : s.scrapeStatus === 'Failed'
                        ? <span style={{ color: 'var(--red)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCRAPE FAILED</span>
                        : <span/>
                      }
```

to:

```jsx
                      {statusBadge && (
                        <span
                          style={{ color: statusBadge.color, marginLeft: 6, fontWeight: 700, fontSize: 8 }}
                          title={s.noScoreReason ?? undefined}
                        >
                          {statusBadge.label}
                        </span>
                      )}
                      {!s.noScoreReason && (
                        s.scrapeStatus === 'Succeeded'
                          ? <span style={{ color: 'var(--green)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCRAPE COMPLETED</span>
                          : s.scrapeStatus === 'Failed'
                          ? <span style={{ color: 'var(--red)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCRAPE FAILED</span>
                          : <span/>
                      )}
```

- [ ] **Step 7: Add the always-available control and gate the trigger-scrape button**

Change:

```jsx
                    {!s.isExhibition && (started || hasScoresAnnounced(s)) && (
                      <div style={{ marginTop: 10 }}>
                        <button
                          type="button"
                          onClick={() => {
                            api.adminTriggerScrape(s.id)
                              .then(() => {
                                setError(null);
                                setScrapeSuccessId(s.id);
                                setTimeout(() => setScrapeSuccessId(null), 3000);
                              })
                              .catch(() => setError('Scrape trigger failed.'));
                          }}
                          style={{
                            width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                            background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                          }}
                        >
                          Trigger Score Scrape
                        </button>

                        {scrapeSuccessId === s.id && (
                          <div style={{ marginTop: 6, fontSize: 11, fontWeight: 600, color: 'var(--green)', textAlign: 'center' }}>
                            ✓ Scrape triggered successfully
                          </div>
                        )}
                      </div>
                    )}
```

to:

```jsx
                    {!s.isExhibition && (
                      <div style={{ marginTop: 10 }}>
                        {s.noScoreReason ? (
                          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <div style={{ flex: 1, fontSize: 10, color: 'var(--text-muted)' }}>
                              <strong style={{ color: 'var(--red)' }}>No scores:</strong> {s.noScoreReason}
                            </div>
                            <button
                              type="button"
                              onClick={() => saveNoScoreReason(s.id, null)}
                              disabled={savingNoScoreReason}
                              style={{
                                padding: '6px 10px', borderRadius: 5, fontSize: 10, fontWeight: 700,
                                background: 'transparent', border: '1px solid var(--border)', color: 'var(--text-muted)',
                                cursor: savingNoScoreReason ? 'not-allowed' : 'pointer', whiteSpace: 'nowrap',
                              }}
                            >
                              Clear
                            </button>
                          </div>
                        ) : (
                          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <input
                              value={noScoreReasonInput}
                              onChange={e => setNoScoreReasonInput(e.target.value)}
                              placeholder="Reason, e.g. rained out"
                              style={{ ...inputStyle, flex: 1 }}
                            />
                            <button
                              type="button"
                              onClick={() => saveNoScoreReason(s.id, noScoreReasonInput)}
                              disabled={savingNoScoreReason || !noScoreReasonInput.trim()}
                              style={{
                                padding: '6px 10px', borderRadius: 5, fontSize: 10, fontWeight: 700,
                                background: 'var(--red)', color: 'var(--bg)', border: 'none',
                                cursor: savingNoScoreReason || !noScoreReasonInput.trim() ? 'not-allowed' : 'pointer',
                                whiteSpace: 'nowrap',
                              }}
                            >
                              Mark No Scores
                            </button>
                          </div>
                        )}
                      </div>
                    )}

                    {!s.isExhibition && !s.noScoreReason && (started || hasScoresAnnounced(s)) && (
                      <div style={{ marginTop: 10 }}>
                        <button
                          type="button"
                          onClick={() => {
                            api.adminTriggerScrape(s.id)
                              .then(() => {
                                setError(null);
                                setScrapeSuccessId(s.id);
                                setTimeout(() => setScrapeSuccessId(null), 3000);
                              })
                              .catch(() => setError('Scrape trigger failed.'));
                          }}
                          style={{
                            width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                            background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                          }}
                        >
                          Trigger Score Scrape
                        </button>

                        {scrapeSuccessId === s.id && (
                          <div style={{ marginTop: 6, fontSize: 11, fontWeight: 600, color: 'var(--green)', textAlign: 'center' }}>
                            ✓ Scrape triggered successfully
                          </div>
                        )}
                      </div>
                    )}
```

- [ ] **Step 8: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — no type errors

Run: `npm run lint` (from `DCF.Web/`)
Expected: SUCCESS, no new warnings/errors

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (regression check — confirms nothing else broke)

- [ ] **Step 9: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: let admins mark/clear a show's no-score reason and show a Completed badge for concluded exhibitions"
```

---

### Task 6: End-to-end manual verification

**Files:** none (verification only — no commit at the end of this task)

**Interfaces:**
- Consumes: everything produced by Tasks 1-5.

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
Expected: API listening, Vite dev server on `http://localhost:5173`, no startup errors in either terminal.

- [ ] **Step 2: Mark a competitive show with a no-score reason**

In the browser, sign in (dev auth bypass applies automatically), go to an admin season's detail page. Add or expand an existing competitive (non-exhibition) show. Confirm the "Mark No Scores" input + button appear regardless of whether the show has started. Type a reason (e.g. "Storm forced standstill exhibition") and click "Mark No Scores". Confirm:
- The collapsed card header now shows a red "NO SCORES" badge, and hovering it shows the reason as a tooltip.
- The "Trigger Score Scrape" button (if it was visible before) is now hidden.
- Reloading the page preserves the reason and badge.

- [ ] **Step 3: Clear the reason**

Re-expand the same show. Confirm the reason text and a "Clear" button are shown instead of the input. Click "Clear". Confirm the "NO SCORES" badge disappears and (if the show has started or announced scores) the normal STARTED/SCORES ANNOUNCED/SCRAPE badges resume.

- [ ] **Step 4: Verify the exhibition "Completed" badge**

Find or create an exhibition show whose "Concludes" time is in the past. Confirm its collapsed card header shows a green "COMPLETED" badge instead of "SCORES ANNOUNCED". Confirm the "Mark No Scores" control does **not** appear for exhibition shows.

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
