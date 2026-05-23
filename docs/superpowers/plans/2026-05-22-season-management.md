# Season Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full season management to the admin page — create seasons with start/end dates, automatic status transitions (Upcoming → Active → Completed), manual publishing, corps assignment, and show creation.

**Architecture:** Backend adds a `SeasonStatus` enum, updates `SeasonEntity`, adds a `SeasonStatusService` background service for automatic date-driven transitions, and new controller endpoints. Frontend adds a tabbed `/admin` page and a new `/admin/seasons/:id` detail page wired to the existing + new API endpoints.

**Tech Stack:** ASP.NET Core .NET 10, EF Core + Npgsql (PostgreSQL), xUnit + EF InMemory, React 19 + TypeScript + React Router v6.

---

## File Map

**Create:**
- `DCF.Data/Models/SeasonStatus.cs` — `SeasonStatus` enum
- `DCF.Api/Services/ISeasonStatusService.cs` — interface for scheduling season transitions
- `DCF.Api/Services/SeasonStatusService.cs` — `BackgroundService` implementing `ISeasonStatusService`
- `DCF.Tests/Services/AdminServiceTests.cs` — tests for new AdminService methods
- `DCF.Web/src/pages/SeasonDetail.tsx` — season detail page

**Modify:**
- `DCF.Data/Entities/SeasonEntity.cs` — remove `IsActive`, add `Status`, `StartDate`, `EndDate`, `IsPublished`
- `DCF.Api/Services/IAdminService.cs` — update `CreateSeasonAsync`, remove `ActivateSeasonAsync`, add `GetSeasonDetailAsync` + `PublishSeasonAsync`
- `DCF.Api/Services/AdminService.cs` — implement updated/new service methods
- `DCF.Api/Controllers/AdminController.cs` — remove activate endpoint, add get-detail + publish endpoints
- `DCF.Api/Models/AdminRequests.cs` — add `StartDate`/`EndDate` to `CreateSeasonRequest`
- `DCF.Api/Services/ScrapeSchedulerService.cs` — update `IsActive` bool query to `Status == SeasonStatus.Active`
- `DCF.Api/Program.cs` — register `SeasonStatusService`
- `DCF.Web/src/types/api.ts` — add `SeasonStatus`, `Season`, `SeasonDetail`, `Show`
- `DCF.Web/src/api/client.ts` — add 7 new admin API methods
- `DCF.Web/src/pages/Admin.tsx` — tabbed layout (Seasons + Corps tabs), remove manual scrape
- `DCF.Web/src/App.tsx` — add `/admin/seasons/:id` route

---

### Task 1: SeasonStatus enum + SeasonEntity changes

**Files:**
- Create: `DCF.Data/Models/SeasonStatus.cs`
- Modify: `DCF.Data/Entities/SeasonEntity.cs`

- [ ] **Step 1: Create the SeasonStatus enum**

Create `DCF.Data/Models/SeasonStatus.cs`:

```csharp
namespace DCF.Data.Models;

public enum SeasonStatus { Upcoming, Active, Completed }
```

- [ ] **Step 2: Update SeasonEntity**

Replace the contents of `DCF.Data/Entities/SeasonEntity.cs`:

```csharp
using DCF.Data.Models;

namespace DCF.Data.Entities;

public class SeasonEntity
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public SeasonStatus Status { get; set; } = SeasonStatus.Upcoming;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsPublished { get; set; }

    public List<ShowEntity> Shows { get; set; } = [];
    public List<LeagueEntity> Leagues { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
}
```

- [ ] **Step 3: Verify build**

Run:
```
dotnet build DCF.slnx
```

Expected: `Build succeeded` with 0 errors (the `ScrapeSchedulerService` will now have a compiler error referencing `IsActive` — that is expected and will be fixed in Task 5). If that error blocks compilation of the whole solution, temporarily comment out the `IsActive` reference in `ScrapeSchedulerService.cs` line 28 with `// TEMP` until Task 5.

- [ ] **Step 4: Commit**

```
git add DCF.Data/Models/SeasonStatus.cs DCF.Data/Entities/SeasonEntity.cs
git commit -m "feat: add SeasonStatus enum and update SeasonEntity schema"
```

---

### Task 2: EF Core migration

**Files:**
- Generated: `DCF.Data/Migrations/<timestamp>_AddSeasonStatusFields.cs`

- [ ] **Step 1: Generate the migration**

Run from the repo root (requires Docker PostgreSQL running — `docker compose up db -d`):

```
dotnet ef migrations add AddSeasonStatusFields --project DCF.Data --startup-project DCF.Api
```

Expected: A new file `DCF.Data/Migrations/<timestamp>_AddSeasonStatusFields.cs` is created.

- [ ] **Step 2: Verify the migration contents**

Open the generated migration file. The `Up` method should:
- Drop the `IsActive` column from `Seasons`
- Add `Status` column (integer, default 0)
- Add `StartDate` column (date)
- Add `EndDate` column (date)
- Add `IsPublished` column (boolean, default false)

The `Down` method should reverse these changes (re-add `IsActive`, remove the new columns).

If the migration looks wrong, delete it and check that `SeasonEntity.cs` matches Step 2 of Task 1 exactly before re-running.

- [ ] **Step 3: Apply the migration**

```
dotnet ef database update --project DCF.Data --startup-project DCF.Api
```

Expected: `Done.`

- [ ] **Step 4: Commit**

```
git add DCF.Data/Migrations/
git commit -m "feat: migration - replace IsActive with Status enum and add season dates"
```

---

### Task 3: ISeasonStatusService + IAdminService + AdminService + tests

**Files:**
- Create: `DCF.Api/Services/ISeasonStatusService.cs`
- Create: `DCF.Tests/Services/AdminServiceTests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`

- [ ] **Step 1: Create ISeasonStatusService**

Create `DCF.Api/Services/ISeasonStatusService.cs`:

```csharp
using DCF.Data.Entities;

namespace DCF.Api.Services;

public interface ISeasonStatusService
{
    void ScheduleSeason(SeasonEntity season);
}
```

- [ ] **Step 2: Write failing tests**

Create `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class AdminServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    private class NoOpSeasonStatus : ISeasonStatusService
    {
        public void ScheduleSeason(SeasonEntity season) { }
    }

    [Fact]
    public async Task CreateSeasonAsync_PersistsSeasonWithCorrectFields()
    {
        using var db = CreateDb("admin_create_season");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 8, 12);
        var result = await svc.CreateSeasonAsync(2026, start, end);

        Assert.Equal(2026, result.Year);
        Assert.Equal(start, result.StartDate);
        Assert.Equal(end, result.EndDate);
        Assert.Equal(SeasonStatus.Upcoming, result.Status);
        Assert.False(result.IsPublished);
    }

    [Fact]
    public async Task GetSeasonDetailAsync_MissingSeason_ReturnsNull()
    {
        using var db = CreateDb("admin_get_detail_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

        var result = await svc.GetSeasonDetailAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSeasonDetailAsync_ExistingSeason_ReturnsDetailWithCorpsIds()
    {
        using var db = CreateDb("admin_get_detail_existing");
        var seasonId = Guid.NewGuid();
        var corps1Id = Guid.NewGuid();
        var corps2Id = Guid.NewGuid();

        db.Seasons.Add(new SeasonEntity
        {
            Id = seasonId,
            Year = 2026,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 12)
        });
        db.SeasonCorps.AddRange(
        [
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = corps1Id },
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = corps2Id }
        ]);
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var result = await svc.GetSeasonDetailAsync(seasonId);

        Assert.NotNull(result);
        Assert.Equal(seasonId, result.Id);
        Assert.Equal(2026, result.Year);
        Assert.Contains(corps1Id, result.CorpsIds);
        Assert.Contains(corps2Id, result.CorpsIds);
    }

    [Fact]
    public async Task PublishSeasonAsync_MissingSeason_ReturnsFalse()
    {
        using var db = CreateDb("admin_publish_missing");
        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

        var result = await svc.PublishSeasonAsync(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public async Task PublishSeasonAsync_ExistingSeason_SetsIsPublished()
    {
        using var db = CreateDb("admin_publish_existing");
        var seasonId = Guid.NewGuid();
        db.Seasons.Add(new SeasonEntity
        {
            Id = seasonId,
            Year = 2026,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 12)
        });
        await db.SaveChangesAsync();

        var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
        var result = await svc.PublishSeasonAsync(seasonId);

        Assert.True(result);
        var season = await db.Seasons.FindAsync(seasonId);
        Assert.True(season!.IsPublished);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: 5 new tests fail with compile errors because `AdminService` doesn't yet have the new constructor signature or methods.

- [ ] **Step 4: Update IAdminService**

Replace the contents of `DCF.Api/Services/IAdminService.cs`:

```csharp
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
    Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId);
    Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url, DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
    Task<bool> UpdateShowAsync(Guid id, string name, string url, DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
    Task<bool> TriggerScrapeAsync(Guid showId);
}
```

- [ ] **Step 5: Update AdminService records, constructor, and methods**

Replace the contents of `DCF.Api/Services/AdminService.cs`:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record SeasonSummary(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished);
public record SeasonDetail(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished, IEnumerable<Guid> CorpsIds);
public record CorpsSummary(Guid Id, string Name);
public record ShowSummary(Guid Id, string Name, string Url, DateOnly Date, DateTimeOffset ScoresAnnouncedTime, IEnumerable<Guid> CorpsIds);
public record ShowBrief(Guid Id, string Name);

public class AdminService(
    DcfDbContext db,
    ScrapeSchedulerService scrapeScheduler,
    IMqttPublisherService mqtt,
    ISeasonStatusService seasonStatus) : IAdminService
{
    public async Task<bool> IsAdminAsync(string sub)
    {
        return await db.Users.AnyAsync(u => u.Auth0Sub == sub && u.IsAdmin);
    }

    public async Task<IReadOnlyList<SeasonSummary>> GetSeasonsAsync()
    {
        return await db.Seasons
            .OrderByDescending(s => s.Year)
            .Select(s => new SeasonSummary(s.Id, s.Year, s.StartDate, s.EndDate, s.Status, s.IsPublished))
            .ToListAsync();
    }

    public async Task<SeasonSummary> CreateSeasonAsync(int year, DateOnly startDate, DateOnly endDate)
    {
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(),
            Year = year,
            StartDate = startDate,
            EndDate = endDate
        };

        db.Seasons.Add(season);

        await db.SaveChangesAsync();

        seasonStatus.ScheduleSeason(season);

        return new SeasonSummary(season.Id, season.Year, season.StartDate, season.EndDate, season.Status, season.IsPublished);
    }

    public async Task<SeasonDetail?> GetSeasonDetailAsync(Guid id)
    {
        var season = await db.Seasons
            .Include(s => s.SeasonCorps)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (season is null)
        {
            return null;
        }

        return new SeasonDetail(
            season.Id, season.Year, season.StartDate, season.EndDate,
            season.Status, season.IsPublished,
            season.SeasonCorps.Select(sc => sc.CorpsId));
    }

    public async Task<bool> PublishSeasonAsync(Guid id)
    {
        var season = await db.Seasons.FindAsync(id);

        if (season is null)
        {
            return false;
        }

        season.IsPublished = true;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync()
    {
        return await db.Corps
            .OrderBy(c => c.Name)
            .Select(c => new CorpsSummary(c.Id, c.Name))
            .ToListAsync();
    }

    public async Task<CorpsSummary> CreateCorpsAsync(string name)
    {
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = name };
        db.Corps.Add(corps);

        await db.SaveChangesAsync();

        return new CorpsSummary(corps.Id, corps.Name);
    }

    public async Task<bool> SetSeasonCorpsAsync(Guid seasonId, List<Guid> corpsIds)
    {
        if (!await db.Seasons.AnyAsync(s => s.Id == seasonId))
        {
            return false;
        }

        var existing = await db.SeasonCorps.Where(sc => sc.SeasonId == seasonId).ToListAsync();
        db.SeasonCorps.RemoveRange(existing);
        db.SeasonCorps.AddRange(corpsIds.Select(cId =>
            new SeasonCorpsEntity { SeasonId = seasonId, CorpsId = cId }));

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<IReadOnlyList<ShowSummary>> GetShowsAsync(Guid seasonId)
    {
        return await db.Shows
            .Where(s => s.SeasonId == seasonId)
            .Include(s => s.ShowCorps)
            .OrderBy(s => s.Date)
            .Select(s => new ShowSummary(s.Id, s.Name, s.Url, s.Date, s.ScoresAnnouncedTime,
                s.ShowCorps.Select(sc => sc.CorpsId)))
            .ToListAsync();
    }

    public async Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url,
        DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
            Date = date,
            ScoresAnnouncedTime = scoresAnnouncedTime,
            SeasonId = seasonId
        };
        db.Shows.Add(show);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));

        await db.SaveChangesAsync();

        scrapeScheduler.ScheduleScrape(show);

        return new ShowBrief(show.Id, show.Name);
    }

    public async Task<bool> UpdateShowAsync(Guid id, string name, string url,
        DateOnly date, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
    {
        var show = await db.Shows.FindAsync(id);

        if (show is null)
        {
            return false;
        }

        show.Name = name;
        show.Url = url;
        show.Date = date;
        show.ScoresAnnouncedTime = scoresAnnouncedTime;

        var existing = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
        db.ShowCorps.RemoveRange(existing);
        db.ShowCorps.AddRange(corpsIds.Select(cId =>
            new ShowCorpsEntity { ShowId = id, CorpsId = cId }));

        await db.SaveChangesAsync();

        var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
        scrapeScheduler.ScheduleScrape(updatedShow);

        return true;
    }

    public async Task<bool> TriggerScrapeAsync(Guid showId)
    {
        var show = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == showId);

        if (show is null)
        {
            return false;
        }

        await scrapeScheduler.ExecuteScrapeAsync(show);

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = showId });

        return true;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: All tests pass (15 existing + 5 new = 20 total).

- [ ] **Step 7: Commit**

```
git add DCF.Api/Services/ISeasonStatusService.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: update AdminService with season status support and add ISeasonStatusService"
```

---

### Task 4: SeasonStatusService background service

**Files:**
- Create: `DCF.Api/Services/SeasonStatusService.cs`

- [ ] **Step 1: Create SeasonStatusService**

Create `DCF.Api/Services/SeasonStatusService.cs`:

```csharp
using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class SeasonStatusService(
    IServiceScopeFactory scopeFactory,
    ILogger<SeasonStatusService> logger) : BackgroundService, ISeasonStatusService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var seasons = await db.Seasons
            .Where(s => s.Status != SeasonStatus.Completed)
            .ToListAsync(stoppingToken);

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

        await db.SaveChangesAsync(stoppingToken);

        foreach (var season in seasons.Where(s => s.Status != SeasonStatus.Completed))
        {
            ScheduleSeason(season);
        }
    }

    public void ScheduleSeason(SeasonEntity season)
    {
        if (_scheduled.TryRemove(season.Id, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        if (season.Status == SeasonStatus.Completed)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _scheduled[season.Id] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                if (season.Status == SeasonStatus.Upcoming)
                {
                    var activateAt = season.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                    var activateDelay = activateAt - DateTime.UtcNow;

                    if (activateDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(activateDelay, cts.Token);
                    }

                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    using (var scope = scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

                        await db.Seasons
                            .Where(s => s.Id == season.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SeasonStatus.Active));

                        logger.LogInformation("Season {Year} status set to Active", season.Year);
                    }
                }

                var completeAt = season.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var completeDelay = completeAt - DateTime.UtcNow;

                if (completeDelay > TimeSpan.Zero)
                {
                    await Task.Delay(completeDelay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                using (var scope = scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

                    await db.Seasons
                        .Where(s => s.Id == season.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SeasonStatus.Completed));

                    logger.LogInformation("Season {Year} status set to Completed", season.Year);
                }
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SeasonStatusService task failed for season {SeasonId}", season.Id);
            }
        });
    }
}
```

- [ ] **Step 2: Verify build**

```
dotnet build DCF.slnx
```

Expected: `Build succeeded` (the `ScrapeSchedulerService` `IsActive` error still exists — fixed next task).

- [ ] **Step 3: Commit**

```
git add DCF.Api/Services/SeasonStatusService.cs
git commit -m "feat: add SeasonStatusService for automatic season status transitions"
```

---

### Task 5: ScrapeSchedulerService fix + Program.cs registration

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`
- Modify: `DCF.Api/Program.cs`

- [ ] **Step 1: Fix ScrapeSchedulerService**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, update line 28. Change:

```csharp
.Where(s => s.Season.IsActive && s.ScoresAnnouncedTime > DateTimeOffset.UtcNow)
```

to:

```csharp
.Where(s => s.Season.Status == SeasonStatus.Active && s.ScoresAnnouncedTime > DateTimeOffset.UtcNow)
```

Also add the using directive at the top of the file if not already present:

```csharp
using DCF.Data.Models;
```

- [ ] **Step 2: Register SeasonStatusService in Program.cs**

In `DCF.Api/Program.cs`, add after the `DraftSchedulerService` registration block:

```csharp
builder.Services.AddSingleton<SeasonStatusService>();
builder.Services.AddSingleton<ISeasonStatusService>(sp => sp.GetRequiredService<SeasonStatusService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SeasonStatusService>());
```

- [ ] **Step 3: Build and run all tests**

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: `Build succeeded`, all 20 tests pass.

- [ ] **Step 4: Commit**

```
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Api/Program.cs
git commit -m "feat: register SeasonStatusService and fix ScrapeSchedulerService season query"
```

---

### Task 6: AdminController + AdminRequests

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`

- [ ] **Step 1: Update CreateSeasonRequest**

In `DCF.Api/Models/AdminRequests.cs`, replace the `CreateSeasonRequest` record:

```csharp
public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
```

The file now reads:

```csharp
namespace DCF.Api.Models;

public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
public record CreateCorpsRequest(string Name);
public record CreateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);
public record UpdateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);
public record SetSeasonCorpsRequest(List<Guid> CorpsIds);
```

- [ ] **Step 2: Update AdminController**

Replace the contents of `DCF.Api/Controllers/AdminController.cs`:

```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IAdminService adminService) : ControllerBase
{
    private string GetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No sub claim");
    }

    // --- Seasons ---

    [HttpGet("seasons")]
    public async Task<IActionResult> GetSeasons()
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetSeasonsAsync());
    }

    [HttpGet("seasons/{id}")]
    public async Task<IActionResult> GetSeason(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var detail = await adminService.GetSeasonDetailAsync(id);

        if (detail is null)
        {
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpPost("seasons")]
    public async Task<IActionResult> CreateSeason(CreateSeasonRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.CreateSeasonAsync(req.Year, req.StartDate, req.EndDate));
    }

    [HttpPost("seasons/{id}/publish")]
    public async Task<IActionResult> PublishSeason(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.PublishSeasonAsync(id) ? NoContent() : NotFound();
    }

    // --- Corps ---

    [HttpGet("corps")]
    public async Task<IActionResult> GetCorps()
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetCorpsAsync());
    }

    [HttpPost("corps")]
    public async Task<IActionResult> CreateCorps(CreateCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.CreateCorpsAsync(req.Name));
    }

    [HttpPut("seasons/{seasonId}/corps")]
    public async Task<IActionResult> SetSeasonCorps(Guid seasonId, SetSeasonCorpsRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.SetSeasonCorpsAsync(seasonId, req.CorpsIds) ? NoContent() : NotFound();
    }

    // --- Shows ---

    [HttpGet("seasons/{seasonId}/shows")]
    public async Task<IActionResult> GetShows(Guid seasonId)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return Ok(await adminService.GetShowsAsync(seasonId));
    }

    [HttpPost("seasons/{seasonId}/shows")]
    public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var result = await adminService.CreateShowAsync(seasonId, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds);

        return Ok(result);
    }

    [HttpPut("shows/{id}")]
    public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.UpdateShowAsync(id, req.Name, req.Url,
            req.Date, req.ScoresAnnouncedTime, req.CorpsIds) ? NoContent() : NotFound();
    }

    // --- Manual scrape trigger ---

    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.TriggerScrapeAsync(id) ? Ok() : NotFound();
    }
}
```

- [ ] **Step 3: Build and run tests**

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: `Build succeeded`, all 20 tests pass.

- [ ] **Step 4: Commit**

```
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Controllers/AdminController.cs
git commit -m "feat: update admin controller - add season detail/publish endpoints, remove activate"
```

---

### Task 7: Frontend types + API client

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Add season and show types to api.ts**

Append to the end of `DCF.Web/src/types/api.ts`:

```ts
export type SeasonStatus = 'Upcoming' | 'Active' | 'Completed';

export interface Season {
  id: string;
  year: number;
  startDate: string;
  endDate: string;
  status: SeasonStatus;
  isPublished: boolean;
}

export interface SeasonDetail extends Season {
  corpsIds: string[];
}

export interface Show {
  id: string;
  name: string;
  url: string;
  date: string;
  scoresAnnouncedTime: string;
  corpsIds: string[];
}
```

- [ ] **Step 2: Add API methods to client.ts**

In `DCF.Web/src/api/client.ts`, add the following methods to the `api` object (after the existing `adminTriggerScrape` entry, before the closing `}`):

```ts
  adminGetSeasons: () =>
    request<Season[]>('/api/admin/seasons'),
  adminGetSeason: (id: string) =>
    request<SeasonDetail>(`/api/admin/seasons/${id}`),
  adminCreateSeason: (year: number, startDate: string, endDate: string) =>
    request<Season>('/api/admin/seasons', { method: 'POST', body: JSON.stringify({ year, startDate, endDate }) }),
  adminPublishSeason: (id: string) =>
    request<void>(`/api/admin/seasons/${id}/publish`, { method: 'POST' }),
  adminSetSeasonCorps: (id: string, corpsIds: string[]) =>
    request<void>(`/api/admin/seasons/${id}/corps`, { method: 'PUT', body: JSON.stringify({ corpsIds }) }),
  adminGetShows: (seasonId: string) =>
    request<Show[]>(`/api/admin/seasons/${seasonId}/shows`),
  adminCreateShow: (
    seasonId: string,
    name: string,
    url: string,
    date: string,
    scoresAnnouncedTime: string,
    corpsIds: string[]
  ) =>
    request<{ id: string; name: string }>(`/api/admin/seasons/${seasonId}/shows`, {
      method: 'POST',
      body: JSON.stringify({ name, url, date, scoresAnnouncedTime, corpsIds }),
    }),
```

Also add the imports for the new types at the top of `client.ts`. Change the existing import line:

```ts
import type { Corps, CreateLeagueRequest, League, Standing, UserProfile } from '../types/api';
```

to:

```ts
import type { Corps, CreateLeagueRequest, League, Season, SeasonDetail, Show, Standing, UserProfile } from '../types/api';
```

- [ ] **Step 3: TypeScript check**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: add season/show types and admin API methods to frontend client"
```

---

### Task 8: Admin.tsx tabbed layout

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`

- [ ] **Step 1: Rewrite Admin.tsx with tabbed layout**

Replace the entire contents of `DCF.Web/src/pages/Admin.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, Season } from '../types/api';

type Tab = 'seasons' | 'corps';

export function Admin() {
  const [tab, setTab] = useState<Tab>('seasons');

  const [seasons, setSeasons] = useState<Season[]>([]);
  const [newYear, setNewYear] = useState('');
  const [newStartDate, setNewStartDate] = useState('');
  const [newEndDate, setNewEndDate] = useState('');
  const [addingSeason, setAddingSeason] = useState(false);

  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [addingCorps, setAddingCorps] = useState(false);

  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
    if (tab === 'seasons') {
      api.adminGetSeasons().then(setSeasons).catch(() => setError('Failed to load seasons.'));
    } else {
      api.adminGetCorps().then(setCorps).catch(() => setError('Failed to load corps.'));
    }
  }, [tab]);

  const addSeason = async (e: React.FormEvent) => {
    e.preventDefault();
    if (addingSeason) return;
    setAddingSeason(true);
    setError(null);
    try {
      await api.adminCreateSeason(Number(newYear), newStartDate, newEndDate);
      const updated = await api.adminGetSeasons();
      setSeasons(updated);
      setNewYear('');
      setNewStartDate('');
      setNewEndDate('');
    } catch {
      setError('Failed to add season.');
    } finally {
      setAddingSeason(false);
    }
  };

  const addCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    if (addingCorps) return;
    setAddingCorps(true);
    setError(null);
    try {
      await api.adminCreateCorps(newCorpsName);
      const updated = await api.adminGetCorps();
      setCorps(updated);
      setNewCorpsName('');
    } catch {
      setError('Failed to add corps.');
    } finally {
      setAddingCorps(false);
    }
  };

  return (
    <div>
      <h2>Admin Panel</h2>
      {error && <p style={{ color: 'red' }}>{error}</p>}

      <div>
        <button onClick={() => setTab('seasons')} disabled={tab === 'seasons'}>Seasons</button>
        <button onClick={() => setTab('corps')} disabled={tab === 'corps'}>Corps</button>
      </div>

      {tab === 'seasons' && (
        <section>
          <h3>Seasons</h3>
          <table>
            <thead>
              <tr>
                <th>Year</th>
                <th>Start</th>
                <th>End</th>
                <th>Status</th>
                <th>Published</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {seasons.map(s => (
                <tr key={s.id}>
                  <td>{s.year}</td>
                  <td>{s.startDate}</td>
                  <td>{s.endDate}</td>
                  <td>{s.status}</td>
                  <td>{s.isPublished ? 'Published' : ''}</td>
                  <td><Link to={`/admin/seasons/${s.id}`}>Manage →</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
          <form onSubmit={addSeason}>
            <input
              type="number"
              value={newYear}
              onChange={e => setNewYear(e.target.value)}
              placeholder="Year"
              required
            />
            <input
              type="date"
              value={newStartDate}
              onChange={e => setNewStartDate(e.target.value)}
              required
            />
            <input
              type="date"
              value={newEndDate}
              onChange={e => setNewEndDate(e.target.value)}
              required
            />
            <button type="submit" disabled={addingSeason}>Add Season</button>
          </form>
        </section>
      )}

      {tab === 'corps' && (
        <section>
          <h3>Corps</h3>
          <ul>{corps.map(c => <li key={c.id}>{c.name}</li>)}</ul>
          <form onSubmit={addCorps}>
            <input
              value={newCorpsName}
              onChange={e => setNewCorpsName(e.target.value)}
              placeholder="Corps name"
              required
            />
            <button type="submit" disabled={addingCorps}>Add Corps</button>
          </form>
        </section>
      )}
    </div>
  );
}
```

- [ ] **Step 2: TypeScript check**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/pages/Admin.tsx
git commit -m "feat: refactor Admin page to tabbed layout with Seasons and Corps tabs"
```

---

### Task 9: SeasonDetail page + routing

**Files:**
- Create: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/App.tsx`

- [ ] **Step 1: Create SeasonDetail.tsx**

Create `DCF.Web/src/pages/SeasonDetail.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, SeasonDetail as SeasonDetailType, Show } from '../types/api';

export function SeasonDetail() {
  const { id } = useParams<{ id: string }>();
  const [season, setSeason] = useState<SeasonDetailType | null>(null);
  const [allCorps, setAllCorps] = useState<Corps[]>([]);
  const [shows, setShows] = useState<Show[]>([]);
  const [selectedCorpsIds, setSelectedCorpsIds] = useState<Set<string>>(new Set());
  const [savingCorps, setSavingCorps] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const [showName, setShowName] = useState('');
  const [showUrl, setShowUrl] = useState('');
  const [showDate, setShowDate] = useState('');
  const [showScoresTime, setShowScoresTime] = useState('');
  const [showCorpsIds, setShowCorpsIds] = useState<Set<string>>(new Set());
  const [addingShow, setAddingShow] = useState(false);

  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    Promise.all([
      api.adminGetSeason(id),
      api.adminGetCorps(),
      api.adminGetShows(id),
    ]).then(([s, corps, sh]) => {
      setSeason(s);
      setAllCorps(corps);
      setShows(sh);
      setSelectedCorpsIds(new Set(s.corpsIds));
    }).catch(() => setError('Failed to load season.'));
  }, [id]);

  const toggleCorps = (corpsId: string) => {
    setSelectedCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) {
        next.delete(corpsId);
      } else {
        next.add(corpsId);
      }
      return next;
    });
  };

  const saveCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!id || savingCorps) return;
    setSavingCorps(true);
    setError(null);
    try {
      await api.adminSetSeasonCorps(id, Array.from(selectedCorpsIds));
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
      setSelectedCorpsIds(new Set(updated.corpsIds));
    } catch {
      setError('Failed to save corps.');
    } finally {
      setSavingCorps(false);
    }
  };

  const publish = async () => {
    if (!id || publishing) return;
    setPublishing(true);
    setError(null);
    try {
      await api.adminPublishSeason(id);
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
    } catch {
      setError('Failed to publish season.');
    } finally {
      setPublishing(false);
    }
  };

  const toggleShowCorps = (corpsId: string) => {
    setShowCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) {
        next.delete(corpsId);
      } else {
        next.add(corpsId);
      }
      return next;
    });
  };

  const addShow = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!id || addingShow) return;
    setAddingShow(true);
    setError(null);
    try {
      await api.adminCreateShow(
        id, showName, showUrl, showDate,
        new Date(showScoresTime).toISOString(),
        Array.from(showCorpsIds)
      );
      const updated = await api.adminGetShows(id);
      setShows(updated);
      setShowName('');
      setShowUrl('');
      setShowDate('');
      setShowScoresTime('');
      setShowCorpsIds(new Set());
    } catch {
      setError('Failed to add show.');
    } finally {
      setAddingShow(false);
    }
  };

  if (!season) {
    return <div>{error ?? 'Loading...'}</div>;
  }

  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  return (
    <div>
      <h2>Season {season.year}</h2>
      <p>{season.startDate} — {season.endDate}</p>
      <p>Status: {season.status}</p>
      {season.isPublished && <p>Published</p>}
      {!season.isPublished && (
        <button
          onClick={publish}
          disabled={publishing || season.corpsIds.length === 0}
        >
          {publishing ? 'Publishing...' : 'Publish'}
        </button>
      )}
      {error && <p style={{ color: 'red' }}>{error}</p>}

      <section>
        <h3>Corps</h3>
        <form onSubmit={saveCorps}>
          {allCorps.map(c => (
            <label key={c.id} style={{ display: 'block' }}>
              <input
                type="checkbox"
                checked={selectedCorpsIds.has(c.id)}
                onChange={() => toggleCorps(c.id)}
                disabled={season.isPublished}
              />
              {c.name}
            </label>
          ))}
          <button type="submit" disabled={savingCorps || season.isPublished}>
            Save Corps
          </button>
        </form>
      </section>

      <section>
        <h3>Shows</h3>
        <table>
          <thead>
            <tr><th>Name</th><th>Date</th><th>URL</th></tr>
          </thead>
          <tbody>
            {shows.map(s => (
              <tr key={s.id}>
                <td>{s.name}</td>
                <td>{s.date}</td>
                <td>{s.url}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <form onSubmit={addShow}>
          <input
            value={showName}
            onChange={e => setShowName(e.target.value)}
            placeholder="Show name"
            required
          />
          <input
            value={showUrl}
            onChange={e => setShowUrl(e.target.value)}
            placeholder="URL"
            required
          />
          <input
            type="date"
            value={showDate}
            onChange={e => setShowDate(e.target.value)}
            required
          />
          <input
            type="datetime-local"
            value={showScoresTime}
            onChange={e => setShowScoresTime(e.target.value)}
            required
          />
          <fieldset>
            <legend>Participating corps</legend>
            {seasonCorps.map(c => (
              <label key={c.id} style={{ display: 'block' }}>
                <input
                  type="checkbox"
                  checked={showCorpsIds.has(c.id)}
                  onChange={() => toggleShowCorps(c.id)}
                />
                {c.name}
              </label>
            ))}
          </fieldset>
          <button type="submit" disabled={addingShow}>Add Show</button>
        </form>
      </section>
    </div>
  );
}
```

- [ ] **Step 2: Add route in App.tsx**

In `DCF.Web/src/App.tsx`, add the `SeasonDetail` import after the existing admin import:

```tsx
import { SeasonDetail } from './pages/SeasonDetail';
```

Then add the route after the `/admin` route:

```tsx
<Route path="/admin/seasons/:id" element={<AdminRoute><SeasonDetail /></AdminRoute>} />
```

The routes section in `App.tsx` should now look like:

```tsx
<Route path="/admin" element={<AdminRoute><Admin /></AdminRoute>} />
<Route path="/admin/seasons/:id" element={<AdminRoute><SeasonDetail /></AdminRoute>} />
```

- [ ] **Step 3: TypeScript check**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: No errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/App.tsx
git commit -m "feat: add SeasonDetail page and admin seasons route"
```

---

## Final verification

After all tasks:

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
cd DCF.Web && npx tsc --noEmit
```

Expected: build succeeds, 20 tests pass, 0 TypeScript errors.
