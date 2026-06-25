# Testing Gaps + CI Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill automated testing gaps across the backend and frontend, then wire the new tests into CI so they run before the smoke test on every push and PR.

**Architecture:** Extract pure static helper methods from the scheduler services (making them unit-testable without mocking DI), add xUnit tests using EF Core InMemory (matching the existing pattern), set up Vitest for the frontend, and update the GitHub Actions workflow file (renamed from `smoke.yml` to `ci.yml`) with a `tests` job that gates the existing `smoke` job.

**Tech Stack:** xUnit, EF Core InMemory, Vitest, @testing-library/react, @testing-library/user-event, @testing-library/jest-dom, jsdom, GitHub Actions

## Global Constraints

- Follow existing xUnit test pattern: `CreateDb(string name)` helper, `DcfDbContext` constructed with `UseInMemoryDatabase`, unique name per test
- No new production dependencies — only devDependencies for frontend
- All new C# test classes in `DCF.Tests/Services/`
- Vitest config in a separate `vitest.config.ts` (not merged into `vite.config.ts`)
- No coverage threshold — report-only
- Mobile breakpoint and CI triggers: exact strings must match exactly (`"CI"` in deploy.yml must match workflow `name:` in ci.yml)
- Branch: create `feat/testing-and-ci` from `master` before starting

---

### Task 1: ScrapeSchedulerService — GetScrapeDelay extraction + ComputeAndUpsert edge cases

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`
- Modify: `DCF.Tests/Services/ScrapeComputedScoreTests.cs` (add 3 edge case tests)
- Create: `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks
- Produces: `ScrapeSchedulerService.GetScrapeDelay(DateTimeOffset, int, DateTimeOffset)` static method

- [ ] **Step 1: Create branch**

```bash
git checkout master
git pull
git checkout -b feat/testing-and-ci
```

- [ ] **Step 2: Write failing tests for GetScrapeDelay**

Create `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`:

```csharp
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
```

- [ ] **Step 3: Run to verify tests fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests" -v n
```

Expected: FAILED — `GetScrapeDelay` does not exist yet.

- [ ] **Step 4: Add GetScrapeDelay to ScrapeSchedulerService.cs**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, add this static method between the `ScheduleScrape` method and `ExecuteScrapeAsync`. The new method belongs right after line 79 (end of the `ScheduleScrape` body):

```csharp
public static TimeSpan GetScrapeDelay(DateTimeOffset scoresAnnouncedTime, int delayMinutes, DateTimeOffset now)
    => scoresAnnouncedTime.AddMinutes(delayMinutes) - now;
```

Then update the two lines inside `ScheduleScrape` that compute `fireAt` and `delay` (currently lines 54–55):

```csharp
// BEFORE:
var fireAt = show.ScoresAnnouncedTime.AddMinutes(_delayMinutes);
var delay = fireAt - DateTimeOffset.UtcNow;

// AFTER:
var delay = GetScrapeDelay(show.ScoresAnnouncedTime, _delayMinutes, DateTimeOffset.UtcNow);
```

- [ ] **Step 5: Run to verify GetScrapeDelay tests pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests" -v n
```

Expected: 4 tests PASSED.

- [ ] **Step 6: Add edge case tests to ScrapeComputedScoreTests.cs**

Append the three new test methods inside the `ScrapeComputedScoreTests` class in `DCF.Tests/Services/ScrapeComputedScoreTests.cs`, before the closing `}`:

```csharp
[Fact]
public async Task ComputeAndUpsert_NoScores_WritesNoComputedRows()
{
    using var db = CreateDb("scrape_no_scores");
    var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Show", Url = "https://dci.org/scores/test",
        Date = new DateOnly(2025, 7, 1), SeasonId = season.Id, Season = season
    };
    db.Seasons.Add(season);
    db.Shows.Add(show);

    await db.SaveChangesAsync();

    await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

    Assert.Empty(await db.ComputedScores.ToListAsync());
}

[Fact]
public async Task ComputeAndUpsert_PartialCaptions_MissingCaptionsAreZero()
{
    using var db = CreateDb("scrape_partial");
    var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Show", Url = "https://dci.org/scores/test",
        Date = new DateOnly(2025, 7, 1), SeasonId = season.Id, Season = season
    };
    db.Seasons.Add(season);
    db.Corps.Add(corps);
    db.Shows.Add(show);
    db.Scores.Add(new ScoreEntity
    {
        Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
        Caption = Caption.Brass, TotalScore = 20.0, Corps = corps, Show = show
    });

    await db.SaveChangesAsync();

    await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

    var computed = await db.ComputedScores.FirstAsync();

    Assert.Equal(20.0, computed.Brass, precision: 5);
    Assert.Equal(0.0, computed.Percussion, precision: 5);
    Assert.Equal(0.0, computed.GeneralEffect1, precision: 5);
    Assert.Equal(0.0, computed.GeneralEffect2, precision: 5);
}

[Fact]
public async Task ComputeAndUpsert_MultipleCorps_EachCorpsGetsOwnRow()
{
    using var db = CreateDb("scrape_multi_corps");
    var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
    var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
    var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Show", Url = "https://dci.org/scores/test",
        Date = new DateOnly(2025, 7, 1), SeasonId = season.Id, Season = season
    };
    db.Seasons.Add(season);
    db.Corps.AddRange(corps1, corps2);
    db.Shows.Add(show);
    db.Scores.AddRange(
        new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps1.Id, ShowId = show.Id,
            Caption = Caption.Brass, TotalScore = 20.0, Corps = corps1, Show = show
        },
        new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps2.Id, ShowId = show.Id,
            Caption = Caption.Brass, TotalScore = 18.5, Corps = corps2, Show = show
        }
    );

    await db.SaveChangesAsync();

    await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

    var rows = await db.ComputedScores.Where(cs => cs.ShowId == show.Id).ToListAsync();

    Assert.Equal(2, rows.Count);
    Assert.Contains(rows, r => r.CorpsId == corps1.Id && r.Brass == 20.0);
    Assert.Contains(rows, r => r.CorpsId == corps2.Id && r.Brass == 18.5);
}
```

- [ ] **Step 7: Run all ScrapeSchedulerService-related tests**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeComputedScore|FullyQualifiedName~ScrapeSchedulerService" -v n
```

Expected: all 7 tests PASSED (3 existing + 3 new edge cases + 4 GetScrapeDelay).

- [ ] **Step 8: Run full test suite to verify nothing broken**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests PASSED.

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeComputedScoreTests.cs DCF.Tests/Services/ScrapeSchedulerServiceTests.cs
git commit -m "test: add GetScrapeDelay extraction and ComputeAndUpsert edge case tests"
```

---

### Task 2: SeasonStatusService — ApplyStatusTransitions extraction + tests

**Files:**
- Modify: `DCF.Api/Services/SeasonStatusService.cs`
- Create: `DCF.Tests/Services/SeasonStatusServiceTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks
- Produces: `SeasonStatusService.ApplyStatusTransitions(IList<SeasonEntity>, DateOnly)` static method

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/SeasonStatusServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data.Entities;
using DCF.Data.Models;
using Xunit;

namespace DCF.Tests.Services;

public class SeasonStatusServiceTests
{
    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateToday_SetsToActive()
    {
        var today = new DateOnly(2025, 7, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 15),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateInPast_SetsToActive()
    {
        var today = new DateOnly(2025, 7, 20);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 10),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateInFuture_NoChange()
    {
        var today = new DateOnly(2025, 7, 1);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 10),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Upcoming, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateBeforeToday_SetsToCompleted()
    {
        var today = new DateOnly(2025, 8, 20);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Completed, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateEqualToday_NoChange()
    {
        var today = new DateOnly(2025, 8, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateInFuture_NoChange()
    {
        var today = new DateOnly(2025, 7, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_Completed_NeverChanges()
    {
        var today = new DateOnly(2025, 6, 1);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2024, Status = SeasonStatus.Completed,
            StartDate = new DateOnly(2024, 7, 1),
            EndDate = new DateOnly(2024, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Completed, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_MultipleSeasons_TransitionsEachIndependently()
    {
        var today = new DateOnly(2025, 8, 20);
        var upcoming = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 8, 18), EndDate = new DateOnly(2026, 8, 15)
        };
        var active = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1), EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([upcoming, active], today);

        Assert.Equal(SeasonStatus.Active, upcoming.Status);
        Assert.Equal(SeasonStatus.Completed, active.Status);
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SeasonStatusServiceTests" -v n
```

Expected: FAILED — `ApplyStatusTransitions` does not exist yet.

- [ ] **Step 3: Extract ApplyStatusTransitions from SeasonStatusService.cs**

In `DCF.Api/Services/SeasonStatusService.cs`, add this static method after the `ScheduleSeason` method (before `DelayUntilAsync`):

```csharp
public static void ApplyStatusTransitions(IList<SeasonEntity> seasons, DateOnly today)
{
    foreach (var season in seasons)
    {
        if (season.Status == SeasonStatus.Active && season.EndDate < today)
        {
            season.Status = SeasonStatus.Completed;
        }
        else if (season.Status == SeasonStatus.Upcoming && season.StartDate <= today)
        {
            season.Status = SeasonStatus.Active;
        }
    }
}
```

Then replace the inline `foreach` in `ExecuteAsync` (the block that reads and mutates seasons, currently lines 28–39) with a call to the new method followed by separate logging:

```csharp
// BEFORE:
foreach (var season in seasons)
{
    if (season.Status == SeasonStatus.Active && season.EndDate < today)
    {
        season.Status = SeasonStatus.Completed;
        logger.LogInformation("Season {Year} completed on startup (end date passed)", season.Year);
    }
    else if (season.Status == SeasonStatus.Upcoming && season.StartDate <= today)
    {
        season.Status = SeasonStatus.Active;
        logger.LogInformation("Season {Year} activated on startup (start date passed)", season.Year);
    }
}

// AFTER:
var statusesBefore = seasons.ToDictionary(s => s.Id, s => s.Status);

ApplyStatusTransitions(seasons, today);

foreach (var season in seasons.Where(s => s.Status != statusesBefore[s.Id]))
{
    logger.LogInformation("Season {Year} set to {Status} on startup", season.Year, season.Status);
}
```

- [ ] **Step 4: Run to verify tests pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SeasonStatusServiceTests" -v n
```

Expected: 8 tests PASSED.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests PASSED.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/SeasonStatusService.cs DCF.Tests/Services/SeasonStatusServiceTests.cs
git commit -m "test: add ApplyStatusTransitions extraction and SeasonStatusService tests"
```

---

### Task 3: DraftSchedulerService + CorpsService tests + coverlet.collector

**Files:**
- Modify: `DCF.Api/Services/DraftSchedulerService.cs`
- Modify: `DCF.Tests/DCF.Tests.csproj`
- Create: `DCF.Tests/Services/DraftSchedulerServiceTests.cs`
- Create: `DCF.Tests/Services/CorpsServiceTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks
- Produces: `DraftSchedulerService.GetDraftDelay(DateTimeOffset, TimeSpan, DateTimeOffset)` static method

- [ ] **Step 1: Write failing tests for GetDraftDelay**

Create `DCF.Tests/Services/DraftSchedulerServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Write CorpsService tests**

Create `DCF.Tests/Services/CorpsServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class CorpsServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task GetCorpsAsync_ReturnsAllCorpsKeyedByName()
    {
        using var db = CreateDb("corps_all");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        db.Corps.AddRange(
            new CorpsEntity { Id = id1, Name = "Blue Devils" },
            new CorpsEntity { Id = id2, Name = "Cavaliers" }
        );

        await db.SaveChangesAsync();

        var service = new CorpsService(db);
        var result = await service.GetCorpsAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("Blue Devils"));
        Assert.True(result.ContainsKey("Cavaliers"));
        Assert.Equal(id1, result["Blue Devils"].Id);
        Assert.Equal(id2, result["Cavaliers"].Id);
    }

    [Fact]
    public async Task GetCorpsAsync_EmptyDatabase_ReturnsEmptyDictionary()
    {
        using var db = CreateDb("corps_empty");
        var service = new CorpsService(db);

        var result = await service.GetCorpsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCorpsAsync_CorpsNameMatchesKey()
    {
        using var db = CreateDb("corps_name");
        var id = Guid.NewGuid();
        db.Corps.Add(new CorpsEntity { Id = id, Name = "Phantom Regiment" });

        await db.SaveChangesAsync();

        var service = new CorpsService(db);
        var result = await service.GetCorpsAsync();

        Assert.Equal("Phantom Regiment", result["Phantom Regiment"].Name);
        Assert.Equal(id, result["Phantom Regiment"].Id);
    }
}
```

- [ ] **Step 3: Run to verify both new test files fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftSchedulerServiceTests|FullyQualifiedName~CorpsServiceTests" -v n
```

Expected: DraftSchedulerServiceTests FAILED (`GetDraftDelay` missing). CorpsServiceTests should PASS already — CorpsService exists and takes DcfDbContext directly.

- [ ] **Step 4: Add GetDraftDelay to DraftSchedulerService.cs**

In `DCF.Api/Services/DraftSchedulerService.cs`, add this static method between `CancelScheduled` and `NotifyLeagueMembersAsync`:

```csharp
public static TimeSpan GetDraftDelay(DateTimeOffset startTime, TimeSpan leadTime, DateTimeOffset now)
    => startTime - leadTime - now;
```

Then replace the four inline delay calculations in `ScheduleNext` with calls to this method:

```csharp
// Replace:
var oneDayDelay = startTime - TimeSpan.FromHours(24) - DateTimeOffset.UtcNow;
// With:
var oneDayDelay = GetDraftDelay(startTime, TimeSpan.FromHours(24), DateTimeOffset.UtcNow);

// Replace:
var oneHourDelay = startTime - TimeSpan.FromHours(1) - DateTimeOffset.UtcNow;
// With:
var oneHourDelay = GetDraftDelay(startTime, TimeSpan.FromHours(1), DateTimeOffset.UtcNow);

// Replace:
var openDelay = startTime - OpenLeadTime - DateTimeOffset.UtcNow;
// With:
var openDelay = GetDraftDelay(startTime, OpenLeadTime, DateTimeOffset.UtcNow);

// Replace:
var startDelay = startTime - DateTimeOffset.UtcNow;
// With:
var startDelay = GetDraftDelay(startTime, TimeSpan.Zero, DateTimeOffset.UtcNow);
```

- [ ] **Step 5: Add coverlet.collector to DCF.Tests.csproj**

In `DCF.Tests/DCF.Tests.csproj`, inside the `<ItemGroup>` that has the other `PackageReference` entries, add:

```xml
<PackageReference Include="coverlet.collector" Version="6.0.2">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

- [ ] **Step 6: Run all new tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftSchedulerServiceTests|FullyQualifiedName~CorpsServiceTests" -v n
```

Expected: all 7 tests PASSED.

- [ ] **Step 7: Verify coverage collection works**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./coverage -v n
```

Expected: all tests PASSED, a `coverage.cobertura.xml` file written under `./coverage/`.

- [ ] **Step 8: Run full test suite**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests PASSED.

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Services/DraftSchedulerService.cs DCF.Tests/Services/DraftSchedulerServiceTests.cs DCF.Tests/Services/CorpsServiceTests.cs DCF.Tests/DCF.Tests.csproj
git commit -m "test: add DraftSchedulerService and CorpsService tests, add coverlet.collector"
```

---

### Task 4: Vitest setup + TimePicker test suite

**Files:**
- Modify: `DCF.Web/package.json`
- Create: `DCF.Web/vitest.config.ts`
- Create: `DCF.Web/src/test/setup.ts`
- Create: `DCF.Web/src/components/TimePicker.test.tsx`

**Interfaces:**
- Consumes: `DCF.Web/src/components/TimePicker.tsx` (existing component)
- Produces: `npm test` script that runs Vitest in CI mode

**TimePicker internals to know before writing tests:**
- `value` prop: 24-hour format string `"HH:mm"` or `""`
- `onChange(value: string)`: called with 24-hour format or `""` if `required=false` and field cleared
- `required` prop defaults to `true`; when `false`, clearing the hour field and blurring emits `""`
- Internal display uses 12-hour with AM/PM selector
- `to24(hour, 'AM')`: 12→0, 1-11→same; `to24(hour, 'PM')`: 12→12, 1-11→+12
- Arrow buttons render as `▲` and `▼`; DOM order: hourUp, hourDown, minuteUp, minuteDown
- Minute step size: 5; minute wraps with carry (55+5→next hour, 00-5→prev hour)
- Empty state displays `"--"` in both inputs and `""` in select
- First arrow click on empty state calls `initDefault()`: emits `"12:00"` (12 PM = 12 in 24h)

- [ ] **Step 1: Install Vitest and Testing Library packages**

```bash
cd DCF.Web
npm install --save-dev vitest @vitest/coverage-v8 @testing-library/react @testing-library/user-event @testing-library/jest-dom jsdom
```

- [ ] **Step 2: Create vitest.config.ts**

Create `DCF.Web/vitest.config.ts`:

```ts
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
});
```

- [ ] **Step 3: Create src/test/setup.ts**

Create `DCF.Web/src/test/setup.ts`:

```ts
import '@testing-library/jest-dom';
```

- [ ] **Step 4: Add test scripts to package.json**

In `DCF.Web/package.json`, add to the `"scripts"` object:

```json
"test": "vitest run",
"test:watch": "vitest"
```

- [ ] **Step 5: Write the TimePicker test suite**

Create `DCF.Web/src/components/TimePicker.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import { TimePicker } from './TimePicker';

// Helpers — DOM order of buttons: [hourUp, hourDown, minuteUp, minuteDown]
function getButtons() {
  const [hourUp, hourDown, minuteUp, minuteDown] = screen.getAllByRole('button');

  return { hourUp, hourDown, minuteUp, minuteDown };
}

function getInputs() {
  const [hour, minute] = screen.getAllByRole('textbox');

  return { hour, minute };
}

describe('TimePicker', () => {
  describe('hour arrows', () => {
    it('increments hour on up click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('10:30');
    });

    it('decrements hour on down click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().hourDown);

      expect(onChange).toHaveBeenCalledWith('08:30');
    });

    it('wraps 12 up to 1 (same AM/PM), so 12:00 PM -> 13:00', async () => {
      const onChange = vi.fn();
      // 12:00 24h = 12:00 PM; click up → 1 PM = 13:00
      render(<TimePicker value="12:00" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('13:00');
    });

    it('wraps 1 AM down to 12 AM (midnight), so 01:00 -> 00:00', async () => {
      const onChange = vi.fn();
      // 01:00 24h = 1:00 AM; click down → 12 AM = 00:00
      render(<TimePicker value="01:00" onChange={onChange} />);

      await userEvent.click(getButtons().hourDown);

      expect(onChange).toHaveBeenCalledWith('00:00');
    });
  });

  describe('minute arrows', () => {
    it('increments minute by 5', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().minuteUp);

      expect(onChange).toHaveBeenCalledWith('09:35');
    });

    it('decrements minute by 5', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />);

      await userEvent.click(getButtons().minuteDown);

      expect(onChange).toHaveBeenCalledWith('09:25');
    });

    it('carries into next hour when minute wraps 55 -> 00', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:55" onChange={onChange} />);

      await userEvent.click(getButtons().minuteUp);

      expect(onChange).toHaveBeenCalledWith('10:00');
    });

    it('borrows from hour when minute wraps 00 -> 55', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:00" onChange={onChange} />);

      await userEvent.click(getButtons().minuteDown);

      expect(onChange).toHaveBeenCalledWith('08:55');
    });
  });

  describe('AM/PM select', () => {
    it('switching AM to PM adds 12 hours', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} />); // 9:30 AM

      await userEvent.selectOptions(screen.getByRole('combobox'), 'PM');

      expect(onChange).toHaveBeenCalledWith('21:30');
    });

    it('switching PM to AM subtracts 12 hours', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="21:30" onChange={onChange} />); // 9:30 PM

      await userEvent.selectOptions(screen.getByRole('combobox'), 'AM');

      expect(onChange).toHaveBeenCalledWith('09:30');
    });

    it('12 PM stays 12 when switching to AM (becomes midnight 00:00)', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="12:00" onChange={onChange} />); // 12:00 PM = noon

      await userEvent.selectOptions(screen.getByRole('combobox'), 'AM');

      expect(onChange).toHaveBeenCalledWith('00:00');
    });
  });

  describe('empty state', () => {
    it('displays -- in both inputs when no value', () => {
      render(<TimePicker value="" onChange={vi.fn()} />);
      const { hour, minute } = getInputs();

      expect(hour).toHaveValue('--');
      expect(minute).toHaveValue('--');
    });

    it('initialises to 12:00 PM and emits "12:00" on first arrow click', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="" onChange={onChange} />);

      await userEvent.click(getButtons().hourUp);

      expect(onChange).toHaveBeenCalledWith('12:00');
    });
  });

  describe('value prop sync', () => {
    it('updates displayed hour and minute when value prop changes', () => {
      const { rerender } = render(<TimePicker value="09:30" onChange={vi.fn()} />);

      rerender(<TimePicker value="14:00" onChange={vi.fn()} />); // 2:00 PM

      const { hour, minute } = getInputs();

      expect(hour).toHaveValue('02');
      expect(minute).toHaveValue('00');
    });
  });

  describe('required=false', () => {
    it('emits "" when hour field is cleared and blurred', async () => {
      const onChange = vi.fn();
      render(<TimePicker value="09:30" onChange={onChange} required={false} />);

      await userEvent.click(getInputs().hour);
      await userEvent.clear(getInputs().hour);
      await userEvent.tab();

      expect(onChange).toHaveBeenCalledWith('');
    });
  });
});
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd DCF.Web
npm test
```

Expected: all tests PASSED. If any test fails, the failure message will indicate which TimePicker behavior doesn't match — adjust the expected value in the test (not the component) based on the actual behavior observed.

- [ ] **Step 7: Commit**

```bash
cd ..
git add DCF.Web/vitest.config.ts DCF.Web/src/test/setup.ts DCF.Web/src/components/TimePicker.test.tsx DCF.Web/package.json DCF.Web/package-lock.json
git commit -m "test: add Vitest setup and TimePicker test suite"
```

---

### Task 5: CI pipeline — rename smoke.yml → ci.yml, add tests job, update deploy.yml

**Files:**
- Delete: `.github/workflows/smoke.yml`
- Create: `.github/workflows/ci.yml`
- Modify: `.github/workflows/deploy.yml`

**Interfaces:**
- Consumes: `npm test` script from Task 4, `coverlet.collector` from Task 3
- Produces: unified `ci.yml` where `tests` gates `smoke`; `deploy.yml` triggers on workflow name `"CI"`

- [ ] **Step 1: Write the new ci.yml**

Delete `.github/workflows/smoke.yml` and create `.github/workflows/ci.yml` with the following content (the `smoke` job is unchanged from the original except for `needs: [tests]`; the `tests` job is new):

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]
  workflow_dispatch:

jobs:
  tests:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Restore .NET dependencies
        run: dotnet restore DCF.slnx

      - name: Run .NET tests with coverage
        run: dotnet test DCF.Tests/DCF.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./coverage

      - name: Upload .NET coverage report
        uses: actions/upload-artifact@v4
        with:
          name: dotnet-coverage
          path: coverage/**/coverage.cobertura.xml

      - uses: actions/setup-node@v4
        with:
          node-version: '20'

      - name: Install frontend dependencies
        run: npm ci
        working-directory: DCF.Web

      - name: Run frontend tests
        run: npm test
        working-directory: DCF.Web

  smoke:
    needs: [tests]
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_DB: dcf
          POSTGRES_USER: dcf
          POSTGRES_HOST_AUTH_METHOD: trust
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

    steps:
      - uses: actions/checkout@v4

      - name: Start Mosquitto
        run: |
          docker run -d \
            --name mosquitto \
            -p 1883:1883 \
            -p 9001:9001 \
            -v ${{ github.workspace }}/scripts/smoke/mosquitto.conf:/mosquitto/config/mosquitto.conf \
            eclipse-mosquitto:2

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - uses: actions/setup-python@v5
        with:
          python-version: '3.12'

      - name: Install Python dependencies
        run: pip install -r scripts/smoke/requirements.txt

      - name: Restore .NET dependencies
        run: dotnet restore DCF.slnx

      - name: Install dotnet-ef tool
        run: dotnet tool install --global dotnet-ef

      - name: Apply DB migrations
        run: dotnet ef database update --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
        env:
          ConnectionStrings__Default: Host=localhost;Database=dcf;Username=dcf

      - name: Start API
        run: dotnet run --project DCF.Api/DCF.Api.csproj &
        env:
          ConnectionStrings__Default: Host=localhost;Database=dcf;Username=dcf
          Mqtt__Host: localhost
          Mqtt__Port: 1883
          ASPNETCORE_URLS: http://localhost:5000
          ASPNETCORE_ENVIRONMENT: Development

      - name: Wait for API to be ready
        run: |
          for i in $(seq 1 30); do
            code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/seasons/active 2>/dev/null || true)
            [ "$code" != "000" ] && break || sleep 2
          done
          [ "$code" != "000" ] || { echo "API did not become ready after 60s"; exit 1; }

      - name: Run smoke test
        run: python scripts/smoke/smoke_test.py
        env:
          SMOKE_API_URL: http://localhost:5000
          SMOKE_DB_URL: postgresql://dcf@localhost:5432/dcf
          SMOKE_MQTT_HOST: localhost
          SMOKE_MQTT_PORT: "1883"
```

- [ ] **Step 2: Update deploy.yml to reference the new workflow name**

In `.github/workflows/deploy.yml`, change line 5:

```yaml
# BEFORE:
    workflows: ["Smoke Test"]

# AFTER:
    workflows: ["CI"]
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml .github/workflows/deploy.yml
git rm .github/workflows/smoke.yml
git commit -m "ci: rename smoke.yml to ci.yml, add tests job gating smoke"
```

- [ ] **Step 4: Push and verify**

```bash
git push -u origin feat/testing-and-ci
```

Open the GitHub Actions tab. Confirm:
- The `CI` workflow appears and runs both `tests` and `smoke` jobs
- `smoke` shows a dependency on `tests` in the workflow graph
- The `Deploy` workflow still triggers on CI completion
