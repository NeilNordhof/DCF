# Computed Scores Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace on-the-fly standings computation from raw DCI score rows with a pre-computed `ComputedScoreEntity` table, and rewrite `StandingsService` to read from it using DCI-aligned weighting (40% GE, 30% Visual, 30% Music).

**Architecture:** A new `ComputedCaption` enum captures 12 fantasy-specific caption types distinct from the raw `Caption` enum. `ComputedScoreEntity` stores one row per corps per show — every scrape upserts a row for that show, preserving full season history for future graphing. `StandingsService` reads the most recent row per corps (by show date) and applies split-aware weighting. `LeagueEntity.DraftableCaptions` and `DraftPickEntity.Caption` both switch from `Caption` to `ComputedCaption`.

**Tech Stack:** C# / .NET 10, EF Core + Npgsql, xUnit InMemory, TypeScript (Vite + React)

---

## File Map

### New files
- `DCF.Data/Models/ComputedCaption.cs` — enum with 12 values
- `DCF.Data/Entities/ComputedScoreEntity.cs` — one row per corps per show; `SeasonId` denormalized for efficient filtering
- `DCF.Data/Migrations/XXXXX_AddComputedScores.cs` (generated)
- `DCF.Data/Migrations/XXXXX_MigrateToComputedCaption.cs` (generated)
- `DCF.Tests/Services/ComputedScoreEntityTests.cs` — entity persistence test
- `DCF.Tests/Services/ScrapeComputedScoreTests.cs` — computation tests

### Modified files
- `DCF.Data/DcfDbContext.cs` — add `ComputedScores` DbSet, configure unique index on `(ShowId, CorpsId)`, update JSONB conversion
- `DCF.Data/Entities/LeagueEntity.cs` — `DraftableCaptions: Caption[]` → `ComputedCaption[]`
- `DCF.Data/Entities/DraftPickEntity.cs` — `Caption: Caption` → `ComputedCaption`
- `DCF.Api/Models/LeagueRequests.cs` — update `CreateLeagueRequest`, `SubmitPickRequest`
- `DCF.Api/Services/ILeagueService.cs` — update `CreateAsync` signature
- `DCF.Api/Services/LeagueService.cs` — update `CreateAsync` implementation
- `DCF.Api/Services/IDraftService.cs` — update `SubmitPickAsync` signature
- `DCF.Api/Services/DraftService.cs` — update `SubmitPickAsync` implementation
- `DCF.Api/Services/ScrapeSchedulerService.cs` — add `ComputeAndUpsertComputedScoresAsync`
- `DCF.Api/Services/StandingsService.cs` — full rewrite
- `DCF.Tests/Services/StandingsServiceTests.cs` — full rewrite
- `DCF.Tests/Services/DraftServiceTests.cs` — update `Caption` refs to `ComputedCaption`
- `DCF.Web/src/types/api.ts` — add `ComputedCaption` type, update `Standing` and related

---

## Task 1: Add ComputedCaption enum and ComputedScoreEntity

**Files:**
- Create: `DCF.Data/Models/ComputedCaption.cs`
- Create: `DCF.Data/Entities/ComputedScoreEntity.cs`
- Create: `DCF.Tests/Services/ComputedScoreEntityTests.cs`
- Modify: `DCF.Data/DcfDbContext.cs`
- Generate: EF migration

- [ ] **Step 1: Write failing test**

Create `DCF.Tests/Services/ComputedScoreEntityTests.cs`:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class ComputedScoreEntityTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task ComputedScoreEntity_CanBeAddedAndRetrieved()
    {
        using var db = CreateDb("computed_score_basic");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025 };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/show1",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = season.Id,
            CorpsId = corps.Id,
            GeneralEffectCombined = 38.5,
            GeneralEffect1 = 19.25,
            GeneralEffect2 = 19.25,
            VisualCombined = 28.5,
            Visual = 19.0,
            Colorguard = 18.0,
            VisualProficiency = 18.5,
            VisualAnalysis = 19.5,
            MusicCombined = 29.0,
            Brass = 19.0,
            Percussion = 18.5,
            MusicAnalysis = 19.0
        });
        await db.SaveChangesAsync();

        var loaded = await db.ComputedScores
            .FirstAsync(cs => cs.ShowId == show.Id && cs.CorpsId == corps.Id);

        Assert.Equal(38.5, loaded.GeneralEffectCombined, precision: 5);
        Assert.Equal(29.0, loaded.MusicCombined, precision: 5);
        Assert.Equal(season.Id, loaded.SeasonId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ComputedScoreEntityTests" -v n
```

Expected: compilation error — `ComputedScoreEntity` and `db.ComputedScores` do not yet exist.

- [ ] **Step 3: Create ComputedCaption enum**

Create `DCF.Data/Models/ComputedCaption.cs`:

```csharp
namespace DCF.Data.Models;

public enum ComputedCaption
{
    GeneralEffectCombined,
    GeneralEffect1,
    GeneralEffect2,
    VisualCombined,
    Visual,
    Colorguard,
    VisualProficiency,
    VisualAnalysis,
    MusicCombined,
    Brass,
    Percussion,
    MusicAnalysis,
}
```

- [ ] **Step 4: Create ComputedScoreEntity**

Create `DCF.Data/Entities/ComputedScoreEntity.cs`:

```csharp
namespace DCF.Data.Entities;

public class ComputedScoreEntity
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public ShowEntity Show { get; set; } = null!;
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public double GeneralEffectCombined { get; set; }
    public double GeneralEffect1 { get; set; }
    public double GeneralEffect2 { get; set; }
    public double VisualCombined { get; set; }
    public double Visual { get; set; }
    public double Colorguard { get; set; }
    public double VisualProficiency { get; set; }
    public double VisualAnalysis { get; set; }
    public double MusicCombined { get; set; }
    public double Brass { get; set; }
    public double Percussion { get; set; }
    public double MusicAnalysis { get; set; }
}
```

`SeasonId` is denormalized from `Show.SeasonId` so that standings queries can filter by season without a join. `ShowId` is the authoritative identity for each scraped result.

- [ ] **Step 5: Add DbSet and indexes to DcfDbContext**

In `DCF.Data/DcfDbContext.cs`, add after the `DraftPicks` DbSet:

```csharp
public DbSet<ComputedScoreEntity> ComputedScores => Set<ComputedScoreEntity>();
```

In `OnModelCreating`, add after the existing index configurations:

```csharp
mb.Entity<ComputedScoreEntity>()
    .HasIndex(e => new { e.ShowId, e.CorpsId })
    .IsUnique();

mb.Entity<ComputedScoreEntity>()
    .HasIndex(e => e.SeasonId);
```

- [ ] **Step 6: Run test to verify it passes**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ComputedScoreEntityTests" -v n
```

Expected: PASS.

- [ ] **Step 7: Generate migration**

```
dotnet ef migrations add AddComputedScores --project DCF.Data --startup-project DCF.Api
```

Open the generated file. Verify the `Up` method creates a `ComputedScores` table with columns: `Id` (uuid PK), `ShowId` (uuid FK → Shows), `SeasonId` (uuid FK → Seasons), `CorpsId` (uuid FK → Corps), plus 12 `double` score columns. Check for the unique index on `(ShowId, CorpsId)` and the non-unique index on `SeasonId`.

- [ ] **Step 8: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: All existing tests pass plus the new ComputedScoreEntityTests pass.

- [ ] **Step 9: Commit**

```
git add DCF.Data/Models/ComputedCaption.cs DCF.Data/Entities/ComputedScoreEntity.cs DCF.Data/DcfDbContext.cs DCF.Data/Migrations/ DCF.Tests/Services/ComputedScoreEntityTests.cs
git commit -m "feat: add ComputedCaption enum and ComputedScoreEntity (per-show history)"
```

---

## Task 2: Migrate Caption → ComputedCaption across league and draft infrastructure

All files that reference `Caption` in a league or draft context switch to `ComputedCaption`. The `StandingsService` is stubbed to compile (returns 0 for all scores); it is fully rewritten in Task 4.

**Files:**
- Modify: `DCF.Data/Entities/DraftPickEntity.cs`
- Modify: `DCF.Data/Entities/LeagueEntity.cs`
- Modify: `DCF.Data/DcfDbContext.cs`
- Modify: `DCF.Api/Models/LeagueRequests.cs`
- Modify: `DCF.Api/Services/ILeagueService.cs`
- Modify: `DCF.Api/Services/LeagueService.cs`
- Modify: `DCF.Api/Services/IDraftService.cs`
- Modify: `DCF.Api/Services/DraftService.cs`
- Modify: `DCF.Api/Services/StandingsService.cs` (stub)
- Modify: `DCF.Tests/Services/StandingsServiceTests.cs` (empty placeholder)
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`
- Generate: EF migration

- [ ] **Step 1: Update DraftPickEntity**

Full replacement of `DCF.Data/Entities/DraftPickEntity.cs`:

```csharp
using DCF.Data.Models;

namespace DCF.Data.Entities;

public class DraftPickEntity
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public LeagueEntity League { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public ComputedCaption Caption { get; set; }
    public int PickNumber { get; set; }
    public int RoundNumber { get; set; }
}
```

- [ ] **Step 2: Update LeagueEntity**

In `DCF.Data/Entities/LeagueEntity.cs`, change the DraftableCaptions property type:

```csharp
public ComputedCaption[] DraftableCaptions { get; set; } = [];
```

- [ ] **Step 3: Update DcfDbContext JSONB conversion**

In `DCF.Data/DcfDbContext.cs`, update the `DraftableCaptions` property conversion to use `ComputedCaption[]`:

```csharp
mb.Entity<LeagueEntity>()
    .Property(e => e.DraftableCaptions)
    .HasColumnType("jsonb")
    .HasConversion(
        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
        v => JsonSerializer.Deserialize<ComputedCaption[]>(v, JsonSerializerOptions.Default) ?? Array.Empty<ComputedCaption>());
```

- [ ] **Step 4: Update LeagueRequests.cs**

Full replacement of `DCF.Api/Models/LeagueRequests.cs`:

```csharp
using DCF.Data.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime);

public record JoinLeagueRequest(string? InviteCode);

public record SubmitPickRequest(Guid CorpsId, ComputedCaption Caption);
```

- [ ] **Step 5: Update ILeagueService.cs**

In `DCF.Api/Services/ILeagueService.cs`, change the `CreateAsync` signature:

```csharp
using DCF.Data.Models;

namespace DCF.Api.Services;

public interface ILeagueService
{
    Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub);
    Task<LeagueBrief?> CreateAsync(string userSub, string name, bool isPublic, int corpsPerCaption, ComputedCaption[] draftableCaptions, DateTimeOffset? draftStartTime);
    Task<JoinResult> JoinAsync(Guid leagueId, string userSub, string? inviteCode);
    Task<LeagueDetail?> GetAsync(Guid leagueId);
}
```

- [ ] **Step 6: Update LeagueService.cs**

In `DCF.Api/Services/LeagueService.cs`, change the `CreateAsync` method signature (body is unchanged):

```csharp
public async Task<LeagueBrief?> CreateAsync(string userSub, string name, bool isPublic,
    int corpsPerCaption, ComputedCaption[] draftableCaptions, DateTimeOffset? draftStartTime)
```

- [ ] **Step 7: Update IDraftService.cs**

Full replacement of `DCF.Api/Services/IDraftService.cs`:

```csharp
using DCF.Data.Models;

namespace DCF.Api.Services;

public interface IDraftService
{
    Task PublishStateAsync(Guid leagueId);
    Task OpenDraftAsync(Guid leagueId);
    Task OpenDraftAsync(Guid leagueId, string userSub);
    Task StartDraftAsync(Guid leagueId);
    Task StartDraftAsync(Guid leagueId, string userSub);
    Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption);
    Task SkipCurrentPickAsync(Guid leagueId, string userSub);
}
```

- [ ] **Step 8: Update DraftService.cs**

In `DCF.Api/Services/DraftService.cs`, change the `SubmitPickAsync` signature (body is unchanged):

```csharp
public async Task<(Guid Id, int PickNumber)> SubmitPickAsync(
    Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption)
```

Also ensure `using DCF.Data.Models;` is present at the top of the file.

- [ ] **Step 9: Stub StandingsService to compile**

Replace `DCF.Api/Services/StandingsService.cs` with this stub:

```csharp
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public record PickScore(string CorpsName, double? Score);

public record CaptionBreakdown(double Avg, List<PickScore> Picks);

public record MemberScoreBreakdown(Guid UserId, string DisplayName, double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public class StandingsService(DcfDbContext db) : IStandingsService
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        return members
            .Select(m => new MemberStanding(
                m.UserId, m.User.DisplayName, 0,
                new Dictionary<ComputedCaption, CaptionBreakdown>()))
            .ToList();
    }

    public async Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        return members
            .Select(m => new MemberScoreBreakdown(
                m.UserId, m.User.DisplayName, 0,
                new Dictionary<ComputedCaption, CaptionBreakdown>()))
            .ToList();
    }
}
```

- [ ] **Step 10: Replace StandingsServiceTests with placeholder**

Replace `DCF.Tests/Services/StandingsServiceTests.cs` with:

```csharp
namespace DCF.Tests.Services;

// Full test suite written in Task 4 after StandingsService is implemented.
public class StandingsServiceTests { }
```

- [ ] **Step 11: Update DraftServiceTests to use ComputedCaption**

In `DCF.Tests/Services/DraftServiceTests.cs`, find every occurrence of `Caption.` used as a `DraftableCaptions` value or as a caption argument, and change the enum to `ComputedCaption`. For example:

- `DraftableCaptions = [Caption.Brass]` → `DraftableCaptions = [ComputedCaption.Brass]`
- `DraftableCaptions = [Caption.GeneralEffect]` → `DraftableCaptions = [ComputedCaption.GeneralEffectCombined]`
- In calls to `SubmitPickAsync(…, Caption.Brass)` → `SubmitPickAsync(…, ComputedCaption.Brass)`
- In `new SubmitPickRequest(corpsId, Caption.Brass)` → `new SubmitPickRequest(corpsId, ComputedCaption.Brass)`

Add `using DCF.Data.Models;` if not present.

- [ ] **Step 12: Generate migration**

```
dotnet ef migrations add MigrateToComputedCaption --project DCF.Data --startup-project DCF.Api
```

Open the generated migration file. In the `Up` method, add these SQL statements **before** any column operations to clear stale league and pick data whose integer enum values no longer map to the new `ComputedCaption` enum:

```csharp
migrationBuilder.Sql("DELETE FROM \"DraftPicks\";");
migrationBuilder.Sql("DELETE FROM \"LeagueMembers\";");
migrationBuilder.Sql("DELETE FROM \"Leagues\";");
```

- [ ] **Step 13: Build to verify zero errors**

```
dotnet build DCF.slnx
```

Expected: 0 errors. Fix any remaining `Caption` / `ComputedCaption` type mismatches before proceeding.

- [ ] **Step 14: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: All tests pass.

- [ ] **Step 15: Commit**

```
git add DCF.Data/Entities/DraftPickEntity.cs DCF.Data/Entities/LeagueEntity.cs DCF.Data/DcfDbContext.cs DCF.Api/Models/LeagueRequests.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Services/LeagueService.cs DCF.Api/Services/IDraftService.cs DCF.Api/Services/DraftService.cs DCF.Api/Services/StandingsService.cs DCF.Data/Migrations/ DCF.Tests/Services/StandingsServiceTests.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: migrate Caption to ComputedCaption in league and draft infrastructure"
```

---

## Task 3: Compute and upsert ComputedScoreEntity per show in ScrapeSchedulerService

After each scrape, groups the saved raw `ScoreEntity` rows by corps, averages multi-judge captions (GE Music, GE Visual, Percussion, Music Analysis), and upserts one `ComputedScoreEntity` row per corps **for that show**. Re-scraping a show updates the existing row for that show.

**Files:**
- Create: `DCF.Tests/Services/ScrapeComputedScoreTests.cs`
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/ScrapeComputedScoreTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class ScrapeComputedScoreTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task ComputeAndUpsert_CreatesRowWithCorrectValues()
    {
        using var db = CreateDb("scrape_compute_create");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/test",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);

        // Two GE Music judges → averaged; one GE Visual judge
        // VP, VA, CG single; Brass single
        // Two Percussion judges → averaged; one Music Analysis judge
        db.Scores.AddRange(
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectMusic, Judge = "A", TotalScore = 19.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectMusic, Judge = "B", TotalScore = 18.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.GeneralEffectVisual, Judge = "C", TotalScore = 17.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.VisualProficiency, TotalScore = 18.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.VisualAnalysis, TotalScore = 19.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.ColorGuard, TotalScore = 17.0, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Brass, TotalScore = 19.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Percussion, Judge = "D", TotalScore = 18.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.Percussion, Judge = "E", TotalScore = 17.5, Corps = corps, Show = show },
            new ScoreEntity { Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
                Caption = Caption.MusicAnalysis, TotalScore = 18.0, Corps = corps, Show = show }
        );
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

        var computed = await db.ComputedScores
            .FirstAsync(cs => cs.ShowId == show.Id && cs.CorpsId == corps.Id);

        double ge1 = (19.0 + 18.0) / 2;    // 18.5
        double ge2 = 17.5;
        double vp = 18.0;
        double va = 19.0;
        double cg = 17.0;
        double brass = 19.5;
        double perc = (18.5 + 17.5) / 2;   // 18.0
        double ma = 18.0;

        Assert.Equal(ge1, computed.GeneralEffect1, precision: 5);
        Assert.Equal(ge2, computed.GeneralEffect2, precision: 5);
        Assert.Equal(ge1 + ge2, computed.GeneralEffectCombined, precision: 5);
        Assert.Equal((vp + va) / 2, computed.Visual, precision: 5);
        Assert.Equal(cg, computed.Colorguard, precision: 5);
        Assert.Equal((vp + va + cg) / 2, computed.VisualCombined, precision: 5);
        Assert.Equal(vp, computed.VisualProficiency, precision: 5);
        Assert.Equal(va, computed.VisualAnalysis, precision: 5);
        Assert.Equal(brass, computed.Brass, precision: 5);
        Assert.Equal(perc, computed.Percussion, precision: 5);
        Assert.Equal(ma, computed.MusicAnalysis, precision: 5);
        Assert.Equal((brass + ma + perc) / 2, computed.MusicCombined, precision: 5);
        Assert.Equal(season.Id, computed.SeasonId);
    }

    [Fact]
    public async Task ComputeAndUpsert_UpdatesExistingRowForSameShow()
    {
        using var db = CreateDb("scrape_compute_update");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show2", Url = "https://dci.org/scores/test2",
            Date = new DateOnly(2025, 8, 1), SeasonId = season.Id, Season = season
        };
        var existingComputed = new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, Brass = 10.0
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.ComputedScores.Add(existingComputed);
        db.Scores.Add(new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
            Caption = Caption.Brass, TotalScore = 19.0, Corps = corps, Show = show
        });
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show.Id, season.Id);

        var allRows = await db.ComputedScores
            .Where(cs => cs.SeasonId == season.Id && cs.CorpsId == corps.Id)
            .ToListAsync();

        Assert.Single(allRows);
        Assert.Equal(19.0, allRows[0].Brass, precision: 5);
    }

    [Fact]
    public async Task ComputeAndUpsert_CreatesNewRowForDifferentShow_PreservingHistory()
    {
        using var db = CreateDb("scrape_compute_history");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show1", Url = "https://dci.org/scores/s1",
            Date = new DateOnly(2025, 7, 10), SeasonId = season.Id, Season = season
        };
        var show2 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Show2", Url = "https://dci.org/scores/s2",
            Date = new DateOnly(2025, 8, 1), SeasonId = season.Id, Season = season
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show1.Id, SeasonId = season.Id,
            CorpsId = corps.Id, Brass = 17.0
        });
        db.Scores.Add(new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show2.Id,
            Caption = Caption.Brass, TotalScore = 19.5, Corps = corps, Show = show2
        });
        await db.SaveChangesAsync();

        await ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync(db, show2.Id, season.Id);

        var rows = await db.ComputedScores
            .Where(cs => cs.SeasonId == season.Id && cs.CorpsId == corps.Id)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ShowId == show1.Id && r.Brass == 17.0);
        Assert.Contains(rows, r => r.ShowId == show2.Id && r.Brass == 19.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeComputedScoreTests" -v n
```

Expected: FAIL — `ScrapeSchedulerService.ComputeAndUpsertComputedScoresAsync` does not exist.

- [ ] **Step 3: Add ComputeAndUpsertComputedScoresAsync to ScrapeSchedulerService**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, add the static method after `EnumerateScores`:

```csharp
public static async Task ComputeAndUpsertComputedScoresAsync(DcfDbContext db, Guid showId, Guid seasonId)
{
    var showScores = await db.Scores
        .Where(s => s.ShowId == showId)
        .ToListAsync();

    var byCorps = showScores.GroupBy(s => s.CorpsId);

    foreach (var group in byCorps)
    {
        var corpsId = group.Key;
        var scores = group.ToList();

        double Avg(Caption caption)
        {
            var vals = scores.Where(s => s.Caption == caption).Select(s => s.TotalScore).ToList();
            return vals.Count > 0 ? vals.Average() : 0;
        }

        double Single(Caption caption)
        {
            return scores.FirstOrDefault(s => s.Caption == caption)?.TotalScore ?? 0;
        }

        var ge1 = Avg(Caption.GeneralEffectMusic);
        var ge2 = Avg(Caption.GeneralEffectVisual);
        var vp = Single(Caption.VisualProficiency);
        var va = Single(Caption.VisualAnalysis);
        var cg = Single(Caption.ColorGuard);
        var brass = Single(Caption.Brass);
        var perc = Avg(Caption.Percussion);
        var ma = Avg(Caption.MusicAnalysis);

        var existing = await db.ComputedScores
            .FirstOrDefaultAsync(cs => cs.ShowId == showId && cs.CorpsId == corpsId);

        if (existing is null)
        {
            db.ComputedScores.Add(new ComputedScoreEntity
            {
                Id = Guid.NewGuid(),
                ShowId = showId,
                SeasonId = seasonId,
                CorpsId = corpsId,
                GeneralEffect1 = ge1,
                GeneralEffect2 = ge2,
                GeneralEffectCombined = ge1 + ge2,
                Visual = (vp + va) / 2,
                VisualCombined = (vp + va + cg) / 2,
                Colorguard = cg,
                VisualProficiency = vp,
                VisualAnalysis = va,
                Brass = brass,
                Percussion = perc,
                MusicAnalysis = ma,
                MusicCombined = (brass + ma + perc) / 2
            });
        }
        else
        {
            existing.GeneralEffect1 = ge1;
            existing.GeneralEffect2 = ge2;
            existing.GeneralEffectCombined = ge1 + ge2;
            existing.Visual = (vp + va) / 2;
            existing.VisualCombined = (vp + va + cg) / 2;
            existing.Colorguard = cg;
            existing.VisualProficiency = vp;
            existing.VisualAnalysis = va;
            existing.Brass = brass;
            existing.Percussion = perc;
            existing.MusicAnalysis = ma;
            existing.MusicCombined = (brass + ma + perc) / 2;
        }
    }
}
```

In `ExecuteScrapeAsync`, after `await db.SaveChangesAsync()` (the call that saves raw score rows), add:

```csharp
await ComputeAndUpsertComputedScoresAsync(db, freshShow.Id, freshShow.SeasonId);
await db.SaveChangesAsync();
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeComputedScoreTests" -v n
```

Expected: All 3 tests PASS.

- [ ] **Step 5: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeComputedScoreTests.cs
git commit -m "feat: compute and upsert ComputedScoreEntity per show after each scrape"
```

---

## Task 4: Fully rewrite StandingsService

Replaces the stub from Task 2. Reads `ComputedScoreEntity` rows for the season, picks the most recent show's row per corps (by `Show.Date`), applies split-aware weighting, and populates `MemberStanding.Captions`.

**Weighting rules (applied to per-member caption average before summing):**
- GE captions (`GeneralEffectCombined`, `GeneralEffect1`, `GeneralEffect2`): weight `1.0`
- `VisualCombined`: weight `1.0`
- `Visual` or `Colorguard` **when `Visual` is in `DraftableCaptions`** (2-split): weight `0.75`
- `VisualProficiency`, `VisualAnalysis`, `Colorguard` in all other cases (3-split): weight `0.5`
- `MusicCombined`: weight `1.0`
- `Brass` or `Percussion` **when `MusicAnalysis` is NOT in `DraftableCaptions`** (2-split): weight `0.75`
- `Brass`, `Percussion`, `MusicAnalysis` in all other cases (3-split): weight `0.5`

`CaptionBreakdown.Avg` stores the **weighted** caption contribution (ready to sum to total).

**Files:**
- Modify: `DCF.Api/Services/StandingsService.cs`
- Modify: `DCF.Tests/Services/StandingsServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Replace `DCF.Tests/Services/StandingsServiceTests.cs` with:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class StandingsServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    private static async Task<(SeasonEntity Season, CorpsEntity Corps, ShowEntity Show, UserEntity User, LeagueEntity League)>
        SeedLeagueAsync(DcfDbContext db, ComputedCaption[] captions, string inviteCode, int corpsPerCaption = 1)
    {
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|s1", Email = "a@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = inviteCode,
            CorpsPerCaption = corpsPerCaption, DraftableCaptions = captions,
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        await db.SaveChangesAsync();
        return (season, corps, show, user, league);
    }

    private static DraftPickEntity Pick(LeagueEntity league, UserEntity user, CorpsEntity corps,
        ComputedCaption caption, int pickNum)
    {
        return new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
            CorpsId = corps.Id, Caption = caption, PickNumber = pickNum, RoundNumber = 0,
            League = league, User = user, Corps = corps
        };
    }

    private static ComputedScoreEntity ComputedScore(ShowEntity show, CorpsEntity corps,
        double ge1 = 0, double ge2 = 0, double vp = 0, double va = 0, double cg = 0,
        double brass = 0, double perc = 0, double ma = 0)
    {
        return new ComputedScoreEntity
        {
            Id = Guid.NewGuid(),
            ShowId = show.Id,
            SeasonId = show.SeasonId,
            CorpsId = corps.Id,
            GeneralEffect1 = ge1,
            GeneralEffect2 = ge2,
            GeneralEffectCombined = ge1 + ge2,
            Visual = (vp + va) / 2,
            VisualCombined = (vp + va + cg) / 2,
            Colorguard = cg,
            VisualProficiency = vp,
            VisualAnalysis = va,
            Brass = brass,
            Percussion = perc,
            MusicAnalysis = ma,
            MusicCombined = (brass + ma + perc) / 2
        };
    }

    [Fact]
    public async Task GetStandings_GECombined_FullWeight()
    {
        using var db = CreateDb("standings_ge_combined");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.GeneralEffectCombined], "GEC12345");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.GeneralEffectCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, GeneralEffectCombined = 38.5
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal(38.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_VisualCombined_FullWeight()
    {
        using var db = CreateDb("standings_visual_combined");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.VisualCombined], "VIS12345");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.VisualCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, VisualCombined = 28.5
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Equal(28.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Visual2Split_75PercentWeight()
    {
        using var db = CreateDb("standings_vis2");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Visual, ComputedCaption.Colorguard], "V2S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Visual, 0),
            Pick(league, user, corps, ComputedCaption.Colorguard, 1)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, va: 19.0, vp: 19.0, cg: 17.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // Visual = avg(19.0, 19.0) = 19.0 → 19.0 * 0.75 = 14.25
        // Colorguard = 17.0 → 17.0 * 0.75 = 12.75
        Assert.Equal(27.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Visual3Split_50PercentWeight()
    {
        using var db = CreateDb("standings_vis3");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.VisualProficiency, ComputedCaption.VisualAnalysis, ComputedCaption.Colorguard],
            "V3S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.VisualProficiency, 0),
            Pick(league, user, corps, ComputedCaption.VisualAnalysis, 1),
            Pick(league, user, corps, ComputedCaption.Colorguard, 2)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, vp: 18.0, va: 19.0, cg: 17.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 18.0 * 0.5 + 19.0 * 0.5 + 17.0 * 0.5 = 9.0 + 9.5 + 8.5 = 27.0
        Assert.Equal(27.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Music2Split_75PercentWeight()
    {
        using var db = CreateDb("standings_mus2");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass, ComputedCaption.Percussion], "M2S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Brass, 0),
            Pick(league, user, corps, ComputedCaption.Percussion, 1)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, brass: 19.5, perc: 18.5));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 19.5 * 0.75 + 18.5 * 0.75 = 14.625 + 13.875 = 28.5
        Assert.Equal(28.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_Music3Split_50PercentWeight()
    {
        using var db = CreateDb("standings_mus3");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass, ComputedCaption.Percussion, ComputedCaption.MusicAnalysis],
            "M3S12345");
        db.DraftPicks.AddRange(
            Pick(league, user, corps, ComputedCaption.Brass, 0),
            Pick(league, user, corps, ComputedCaption.Percussion, 1),
            Pick(league, user, corps, ComputedCaption.MusicAnalysis, 2)
        );
        db.ComputedScores.Add(ComputedScore(show, corps, brass: 19.5, perc: 18.5, ma: 18.0));
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // 19.5 * 0.5 + 18.5 * 0.5 + 18.0 * 0.5 = 9.75 + 9.25 + 9.0 = 28.0
        Assert.Equal(28.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_MultipleCorps_AveragesBeforeWeighting()
    {
        using var db = CreateDb("standings_multi_corps");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|mc", Email = "mc@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = "MULTI123",
            CorpsPerCaption = 2, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.AddRange(corps1, corps2);
        db.Shows.Add(show);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.AddRange(
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps1.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
                League = league, User = user, Corps = corps1
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
                CorpsId = corps2.Id, Caption = ComputedCaption.Brass, PickNumber = 1, RoundNumber = 0,
                League = league, User = user, Corps = corps2
            }
        );
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corps1.Id, Brass = 20.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corps2.Id, Brass = 16.0 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // avg(20.0, 16.0) = 18.0 → 2-split weight 0.75 → 13.5
        Assert.Equal(13.5, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_UsesLatestShowScorePerCorps()
    {
        using var db = CreateDb("standings_latest_show");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var show1 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Prelims", Url = "https://dci.org/scores/prelims",
            Date = new DateOnly(2025, 8, 9), SeasonId = season.Id, Season = season
        };
        var show2 = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|ls", Email = "ls@b.com", DisplayName = "Alice" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = user.Id, Commissioner = user, InviteCode = "LATEST12",
            CorpsPerCaption = 1, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{user.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.Add(corps);
        db.Shows.AddRange(show1, show2);
        db.Users.Add(user);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id, League = league, User = user });
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
            CorpsId = corps.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
            League = league, User = user, Corps = corps
        });
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show1.Id, SeasonId = season.Id, CorpsId = corps.Id, Brass = 17.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show2.Id, SeasonId = season.Id, CorpsId = corps.Id, Brass = 19.5 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        // Should use show2 (later date): 19.5 * 0.75 = 14.625
        Assert.Equal(14.625, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_ZeroScore_WhenNoComputedScoreRow()
    {
        using var db = CreateDb("standings_no_computed");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.Brass], "NOCOMP12");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.Brass, 0));
        // No ComputedScoreEntity added
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.Equal(0.0, standings[0].Score, precision: 5);
    }

    [Fact]
    public async Task GetStandings_PopulatesCaptionsDictionary()
    {
        using var db = CreateDb("standings_captions_dict");
        var (season, corps, show, user, league) = await SeedLeagueAsync(db,
            [ComputedCaption.GeneralEffectCombined], "CAPTDICT1");
        db.DraftPicks.Add(Pick(league, user, corps, ComputedCaption.GeneralEffectCombined, 0));
        db.ComputedScores.Add(new ComputedScoreEntity
        {
            Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id,
            CorpsId = corps.Id, GeneralEffectCombined = 38.0
        });
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Single(standings);
        Assert.True(standings[0].Captions.ContainsKey(ComputedCaption.GeneralEffectCombined));
        Assert.Equal(38.0, standings[0].Captions[ComputedCaption.GeneralEffectCombined].Avg, precision: 5);
        Assert.Single(standings[0].Captions[ComputedCaption.GeneralEffectCombined].Picks);
        Assert.Equal("Blue Devils",
            standings[0].Captions[ComputedCaption.GeneralEffectCombined].Picks[0].CorpsName);
    }

    [Fact]
    public async Task GetStandings_OrderedByScoreDescending()
    {
        using var db = CreateDb("standings_ordering");
        var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active };
        var corpsA = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corpsB = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals",
            Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
        };
        var userA = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|a", Email = "a@b.com", DisplayName = "Alice" };
        var userB = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "sub|b", Email = "b@b.com", DisplayName = "Bob" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "L", SeasonId = season.Id, Season = season,
            CommissionerUserId = userA.Id, Commissioner = userA, InviteCode = "ORDR1234",
            CorpsPerCaption = 1, DraftableCaptions = [ComputedCaption.Brass],
            DraftStatus = DraftStatus.Completed, DraftOrderJson = $"[\"{userA.Id}\",\"{userB.Id}\"]"
        };
        db.Seasons.Add(season);
        db.Corps.AddRange(corpsA, corpsB);
        db.Shows.Add(show);
        db.Users.AddRange(userA, userB);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = userA.Id, League = league, User = userA },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = userB.Id, League = league, User = userB }
        );
        db.DraftPicks.AddRange(
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = userA.Id,
                CorpsId = corpsA.Id, Caption = ComputedCaption.Brass, PickNumber = 0, RoundNumber = 0,
                League = league, User = userA, Corps = corpsA
            },
            new DraftPickEntity
            {
                Id = Guid.NewGuid(), LeagueId = league.Id, UserId = userB.Id,
                CorpsId = corpsB.Id, Caption = ComputedCaption.Brass, PickNumber = 1, RoundNumber = 0,
                League = league, User = userB, Corps = corpsB
            }
        );
        db.ComputedScores.AddRange(
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corpsA.Id, Brass = 15.0 },
            new ComputedScoreEntity { Id = Guid.NewGuid(), ShowId = show.Id, SeasonId = season.Id, CorpsId = corpsB.Id, Brass = 20.0 }
        );
        await db.SaveChangesAsync();

        var standings = await new StandingsService(db).GetStandingsAsync(league.Id);

        Assert.Equal("Bob", standings[0].DisplayName);   // 20.0 * 0.75 = 15.0
        Assert.Equal("Alice", standings[1].DisplayName); // 15.0 * 0.75 = 11.25
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~StandingsServiceTests" -v n
```

Expected: Multiple failures — stub returns 0 and empty captions.

- [ ] **Step 3: Rewrite StandingsService.cs**

Full replacement of `DCF.Api/Services/StandingsService.cs`:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public record PickScore(string CorpsName, double? Score);

public record CaptionBreakdown(double Avg, List<PickScore> Picks);

public record MemberScoreBreakdown(Guid UserId, string DisplayName, double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions);

public class StandingsService(DcfDbContext db) : IStandingsService
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var corpsNames = await db.Corps.ToDictionaryAsync(c => c.Id, c => c.Name);
        var latestByCorps = await LoadLatestComputedScoresAsync(league.SeasonId);

        var standings = new List<MemberStanding>();

        foreach (var member in members)
        {
            var (totalScore, captions) = await ComputeMemberScoreAsync(
                leagueId, member.UserId, league, latestByCorps, corpsNames);

            standings.Add(new MemberStanding(member.UserId, member.User.DisplayName, totalScore, captions));
        }

        return standings.OrderByDescending(s => s.Score).ToList();
    }

    public async Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var corpsNames = await db.Corps.ToDictionaryAsync(c => c.Id, c => c.Name);
        var latestByCorps = await LoadLatestComputedScoresAsync(league.SeasonId);

        var result = new List<MemberScoreBreakdown>();

        foreach (var member in members)
        {
            var (totalScore, captions) = await ComputeMemberScoreAsync(
                leagueId, member.UserId, league, latestByCorps, corpsNames);

            result.Add(new MemberScoreBreakdown(
                member.UserId, member.User.DisplayName, totalScore, captions));
        }

        return result.OrderByDescending(r => r.TotalScore).ToList();
    }

    private async Task<Dictionary<Guid, ComputedScoreEntity>> LoadLatestComputedScoresAsync(Guid seasonId)
    {
        var allSeasonScores = await db.ComputedScores
            .Include(cs => cs.Show)
            .Where(cs => cs.SeasonId == seasonId)
            .ToListAsync();

        return allSeasonScores
            .GroupBy(cs => cs.CorpsId)
            .ToDictionary(
                g => g.Key,
                g => g.MaxBy(cs => cs.Show.Date)!);
    }

    private async Task<(double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions)>
        ComputeMemberScoreAsync(
            Guid leagueId,
            Guid userId,
            LeagueEntity league,
            Dictionary<Guid, ComputedScoreEntity> latestByCorps,
            Dictionary<Guid, string> corpsNames)
    {
        double totalScore = 0;
        var captions = new Dictionary<ComputedCaption, CaptionBreakdown>();

        foreach (var caption in league.DraftableCaptions)
        {
            var picks = await db.DraftPicks
                .Where(p => p.LeagueId == leagueId &&
                            p.UserId == userId &&
                            p.Caption == caption)
                .ToListAsync();

            var pickScores = new List<PickScore>();
            var captionScores = new List<double>();

            foreach (var pick in picks)
            {
                var corpsName = corpsNames.GetValueOrDefault(pick.CorpsId, "Unknown");

                if (latestByCorps.TryGetValue(pick.CorpsId, out var cs))
                {
                    var score = GetCaptionValue(cs, caption);
                    pickScores.Add(new PickScore(corpsName, score));
                    captionScores.Add(score);
                }
                else
                {
                    pickScores.Add(new PickScore(corpsName, null));
                }
            }

            var avg = captionScores.Count > 0 ? captionScores.Average() : 0;
            var weight = GetWeight(caption, league.DraftableCaptions);
            var weighted = avg * weight;
            totalScore += weighted;

            captions[caption] = new CaptionBreakdown(weighted, pickScores);
        }

        return (totalScore, captions);
    }

    private static double GetCaptionValue(ComputedScoreEntity cs, ComputedCaption caption)
    {
        return caption switch
        {
            ComputedCaption.GeneralEffectCombined => cs.GeneralEffectCombined,
            ComputedCaption.GeneralEffect1 => cs.GeneralEffect1,
            ComputedCaption.GeneralEffect2 => cs.GeneralEffect2,
            ComputedCaption.VisualCombined => cs.VisualCombined,
            ComputedCaption.Visual => cs.Visual,
            ComputedCaption.Colorguard => cs.Colorguard,
            ComputedCaption.VisualProficiency => cs.VisualProficiency,
            ComputedCaption.VisualAnalysis => cs.VisualAnalysis,
            ComputedCaption.MusicCombined => cs.MusicCombined,
            ComputedCaption.Brass => cs.Brass,
            ComputedCaption.Percussion => cs.Percussion,
            ComputedCaption.MusicAnalysis => cs.MusicAnalysis,
            _ => 0
        };
    }

    private static double GetWeight(ComputedCaption caption, ComputedCaption[] draftableCaptions)
    {
        if (caption is ComputedCaption.GeneralEffectCombined or
            ComputedCaption.GeneralEffect1 or ComputedCaption.GeneralEffect2)
        {
            return 1.0;
        }

        if (caption == ComputedCaption.VisualCombined)
        {
            return 1.0;
        }

        if (caption is ComputedCaption.Visual or ComputedCaption.VisualProficiency or
            ComputedCaption.VisualAnalysis or ComputedCaption.Colorguard)
        {
            return draftableCaptions.Contains(ComputedCaption.Visual) ? 0.75 : 0.5;
        }

        if (caption == ComputedCaption.MusicCombined)
        {
            return 1.0;
        }

        if (caption is ComputedCaption.Brass or ComputedCaption.Percussion or ComputedCaption.MusicAnalysis)
        {
            return draftableCaptions.Contains(ComputedCaption.MusicAnalysis) ? 0.5 : 0.75;
        }

        return 1.0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~StandingsServiceTests" -v n
```

Expected: All 9 tests PASS.

- [ ] **Step 5: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/StandingsService.cs DCF.Tests/Services/StandingsServiceTests.cs
git commit -m "feat: rewrite StandingsService with per-show ComputedScoreEntity and DCI-aligned weighting"
```

---

## Task 5: Update frontend types

The API now returns `ComputedCaption` enum values serialized as strings (e.g. `"Brass"`, `"GeneralEffectCombined"`). `Standing` gains a `captions` breakdown field. `League.draftableCaptions` and `DraftPick.caption` become `ComputedCaption`.

**Files:**
- Modify: `DCF.Web/src/types/api.ts`

- [ ] **Step 1: Update api.ts**

In `DCF.Web/src/types/api.ts`, add a `ComputedCaption` type alias after `DraftStatus`:

```typescript
export type ComputedCaption =
  | 'GeneralEffectCombined'
  | 'GeneralEffect1'
  | 'GeneralEffect2'
  | 'VisualCombined'
  | 'Visual'
  | 'Colorguard'
  | 'VisualProficiency'
  | 'VisualAnalysis'
  | 'MusicCombined'
  | 'Brass'
  | 'Percussion'
  | 'MusicAnalysis';
```

Update `League`:
```typescript
draftableCaptions: ComputedCaption[];
```

Update `DraftPick`:
```typescript
caption: ComputedCaption;
```

Update `Standing` to add the captions breakdown:
```typescript
export interface Standing {
  userId: string;
  displayName: string;
  score: number;
  captions: Partial<Record<ComputedCaption, CaptionBreakdown>>;
}
```

Update `MemberScoreBreakdown`:
```typescript
export interface MemberScoreBreakdown {
  userId: string;
  displayName: string;
  totalScore: number;
  captions: Partial<Record<ComputedCaption, CaptionBreakdown>>;
}
```

- [ ] **Step 2: Build frontend to verify no TypeScript errors**

```
cd DCF.Web && npm run build
```

Expected: No TypeScript errors. Fix any type errors in components that reference `caption` or `draftableCaptions`.

- [ ] **Step 3: Run lint**

```
npm run lint
```

Expected: No errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/types/api.ts
git commit -m "feat: update frontend types for ComputedCaption and Standing captions"
```

---

## Self-review against spec

| Spec requirement | Covered by |
|---|---|
| Average multi-judge GE Music/Visual scores to get GE1/GE2 | Task 3 `Avg(Caption.GeneralEffectMusic/Visual)` |
| Combined GE = GE1 + GE2 (40 pts) | Task 3 `GeneralEffectCombined = ge1 + ge2` |
| Split GE uses GE1 and GE2 as separate 20-pt captions | Task 4 `GetWeight` returns 1.0 for each |
| Visual Combined = (VP + VA + CG) / 2 (30 pts) | Task 3 `VisualCombined = (vp + va + cg) / 2` |
| Visual 2-Split: Visual = avg(VP, VA), CG separate, each × 0.75 | Task 3 `Visual = (vp+va)/2`; Task 4 weight 0.75 |
| Visual 3-Split: VP, VA, CG each × 0.5 | Task 3 stores each raw; Task 4 weight 0.5 |
| Average double Music Analysis judges | Task 3 `Avg(Caption.MusicAnalysis)` |
| Music Combined = (Brass + MA + Perc) / 2 (30 pts) | Task 3 `MusicCombined = (brass + ma + perc) / 2` |
| Music 2-Split: Brass + Percussion × 0.75, MA ignored | Task 4 weight 0.75 when `MusicAnalysis` not in DraftableCaptions |
| Music 3-Split: Brass + MA + Percussion each × 0.5 | Task 4 weight 0.5 when `MusicAnalysis` in DraftableCaptions |
| One row per corps **per show** (full season history) | Task 3 upsert keyed on `(ShowId, CorpsId)` |
| Standings uses most recent show score per corps | Task 4 `LoadLatestComputedScoresAsync` via `MaxBy(Show.Date)` |
| `ComputedCaption` enum replaces `Caption` for fantasy | Tasks 1 and 2 |
| Standings reads from `ComputedScoreEntity` | Task 4 |
| Caption score = avg of drafted corps scores | Task 4 `captionScores.Average()` |
| 40/30/30 weighting via split-aware factors | Task 4 `GetWeight` |
| Total = sum of weighted caption scores | Task 4 `totalScore += weighted` |
| `MemberStanding.Score` = 100-pt total | Task 4 |
| `MemberStanding.Captions` = per-caption breakdown dict | Task 4 |
| Raw `ScoreEntity` data preserved | Not touched anywhere |
| Historical scores preserved for future graphing | One row per show, never overwritten across shows |
| Frontend types updated | Task 5 |
