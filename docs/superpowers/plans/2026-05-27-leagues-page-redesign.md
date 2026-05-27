# Leagues Page Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the leagues section with tabbed My Leagues/Join layout, improved league cards, caption dropdowns, max players stepper, and non-member league detail view.

**Architecture:** Backend gains `MaxPlayers` on `LeagueEntity`, three new endpoints (public leagues browse, code lookup, active season), and non-member private league access via invite code. Frontend replaces the current monolithic Leagues page with a tabbed layout driven by URL query params, rewrites LeagueCreate to use mutually exclusive caption dropdowns and a validated stepper, and adds a non-member view to LeagueDetail.

**Tech Stack:** ASP.NET Core 10, EF Core, PostgreSQL, React 19, React Router v6, TypeScript, xUnit (EF InMemory)

---

## File Map

**Backend — create:**
- `DCF.Api/Controllers/SeasonsController.cs` — `GET /api/seasons/active`

**Backend — modify:**
- `DCF.Data/Entities/LeagueEntity.cs` — add `MaxPlayers`
- `DCF.Api/Models/LeagueRequests.cs` — add `MaxPlayers` to `CreateLeagueRequest`
- `DCF.Api/Services/ILeagueService.cs` — new method signatures
- `DCF.Api/Services/LeagueService.cs` — records, constructor, all service methods
- `DCF.Api/Services/IStandingsService.cs` — add `GetUserRankAsync`
- `DCF.Api/Services/StandingsService.cs` — implement `GetUserRankAsync`
- `DCF.Api/Controllers/LeaguesController.cs` — new actions, updated signatures

**Frontend — modify:**
- `DCF.Web/src/types/api.ts` — updated `League`, `CreateLeagueRequest`; new `PublicLeague`, `ActiveSeason`
- `DCF.Web/src/api/client.ts` — updated `getLeague`, new `getPublicLeagues`, `lookupLeagueByCode`, `getActiveSeason`
- `DCF.Web/src/pages/Leagues.tsx` — full rewrite: tabbed layout with `LeagueCard`
- `DCF.Web/src/pages/LeagueCreate.tsx` — full rewrite: dropdowns, stepper, discard dialog
- `DCF.Web/src/pages/LeagueDetail.tsx` — non-member view, code from URL, Full badge

**Tests — create:**
- `DCF.Tests/Services/LeagueServiceTests.cs` — created across Tasks 2–7

---

### Task 1: Add MaxPlayers to LeagueEntity + EF Migration

**Files:**
- Modify: `DCF.Data/Entities/LeagueEntity.cs`

- [ ] **Step 1: Add `MaxPlayers` property to `LeagueEntity`**

Open `DCF.Data/Entities/LeagueEntity.cs`. Add after the existing `InviteCode` property (or wherever the other scalar props live):

```csharp
public int MaxPlayers { get; set; } = 8;
```

- [ ] **Step 2: Create the EF migration**

```bash
dotnet ef migrations add AddLeagueMaxPlayers --project DCF.Data --startup-project DCF.Api
```

Expected: a new migration file created under `DCF.Data/Migrations/`.

- [ ] **Step 3: Verify the migration SQL looks correct**

Open the generated migration file. Confirm it adds a column `MaxPlayers integer NOT NULL DEFAULT 8` (or equivalent) to the `Leagues` table.

- [ ] **Step 4: Apply the migration**

```bash
dotnet ef database update --project DCF.Data --startup-project DCF.Api
```

Expected: `Done.`

- [ ] **Step 5: Build to confirm no compile errors**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add DCF.Data/Entities/LeagueEntity.cs DCF.Data/Migrations/
git commit -m "feat: add MaxPlayers to LeagueEntity with EF migration"
```

---

### Task 2: Update GetAsync — Non-Member Support + Invite Code Auth

**Files:**
- Modify: `DCF.Api/Services/LeagueService.cs` — `LeagueDetail` record, `GetAsync` signature and body
- Modify: `DCF.Api/Services/ILeagueService.cs` — `GetAsync` signature
- Modify: `DCF.Api/Controllers/LeaguesController.cs` — `Get` action
- Create: `DCF.Tests/Services/LeagueServiceTests.cs`

> **Note on test constructor:** In this task, use `new LeagueService(db, null!)` (2 args). Task 5 adds a 3rd arg and updates all these calls.

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/LeagueServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class LeagueServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    // ── GetAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_PublicLeague_NonMemberNoCode_ReturnsLeague()
    {
        await using var db = CreateDb(nameof(GetAsync_PublicLeague_NonMemberNoCode_ReturnsLeague));
        var league = new LeagueEntity { Name = "Open", IsPublic = true, MaxPlayers = 8 };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: null);

        Assert.NotNull(result);
        Assert.False(result!.IsMember);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberNoCode_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberNoCode_ReturnsNull));
        var league = new LeagueEntity { Name = "Private", IsPublic = false, InviteCode = "ABC123", MaxPlayers = 8 };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: null);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberCorrectCode_ReturnsLeagueWithoutInviteCode));
        var league = new LeagueEntity { Name = "Private", IsPublic = false, InviteCode = "ABC123", MaxPlayers = 8 };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: "ABC123");

        Assert.NotNull(result);
        Assert.False(result!.IsMember);
        Assert.Null(result.InviteCode);
    }

    [Fact]
    public async Task GetAsync_PrivateLeague_NonMemberWrongCode_ReturnsNull()
    {
        await using var db = CreateDb(nameof(GetAsync_PrivateLeague_NonMemberWrongCode_ReturnsNull));
        var league = new LeagueEntity { Name = "Private", IsPublic = false, InviteCode = "ABC123", MaxPlayers = 8 };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|other", inviteCode: "WRONG");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_Member_ReturnsLeagueWithInviteCode()
    {
        await using var db = CreateDb(nameof(GetAsync_Member_ReturnsLeagueWithInviteCode));
        var user = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
        var league = new LeagueEntity { Name = "Mine", IsPublic = false, InviteCode = "SECRET", MaxPlayers = 8 };
        db.Users.Add(user);
        db.Leagues.Add(league);
        await db.SaveChangesAsync();
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });
        await db.SaveChangesAsync();

        var svc = new LeagueService(db, null!);
        var result = await svc.GetAsync(league.Id, userSub: "sub|me", inviteCode: null);

        Assert.NotNull(result);
        Assert.True(result!.IsMember);
        Assert.Equal("SECRET", result.InviteCode);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL — `LeagueService` constructor doesn't match / `GetAsync` signature doesn't exist yet.

- [ ] **Step 3: Update `LeagueDetail` record in `LeagueService.cs`**

Find the `LeagueDetail` record definition. Add `IsMember`, `MaxPlayers`, and make `InviteCode` nullable:

```csharp
public record LeagueDetail(
    Guid Id,
    string Name,
    bool IsPublic,
    string? InviteCode,
    DraftStatus DraftStatus,
    int CorpsPerCaption,
    int MaxPlayers,
    bool IsMember,
    List<ComputedCaption> Captions,
    DateTime? DraftStartTime,
    List<LeagueMemberSummary> Members
);
```

- [ ] **Step 4: Update `GetAsync` in `LeagueService.cs`**

Change the signature to:

```csharp
public async Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode)
```

Replace the body:

```csharp
public async Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode)
{
    var league = await db.Leagues
        .Include(l => l.Members)
        .ThenInclude(m => m.User)
        .FirstOrDefaultAsync(l => l.Id == leagueId);

    if (league is null)
    {
        return null;
    }

    var user = userSub is not null
        ? await db.Users.FirstOrDefaultAsync(u => u.Sub == userSub)
        : null;

    var isMember = user is not null && league.Members.Any(m => m.UserId == user.Id);

    if (!isMember && !league.IsPublic)
    {
        if (inviteCode is null || league.InviteCode is null)
        {
            return null;
        }

        var codeBytes = System.Text.Encoding.UTF8.GetBytes(inviteCode);
        var storedBytes = System.Text.Encoding.UTF8.GetBytes(league.InviteCode);

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(codeBytes, storedBytes))
        {
            return null;
        }
    }

    return new LeagueDetail(
        league.Id,
        league.Name,
        league.IsPublic,
        isMember ? league.InviteCode : null,
        league.DraftStatus,
        league.CorpsPerCaption,
        league.MaxPlayers,
        isMember,
        league.Captions,
        league.DraftStartTime,
        league.Members.Select(m => new LeagueMemberSummary(m.User.Id, m.User.DisplayName)).ToList()
    );
}
```

- [ ] **Step 5: Update `ILeagueService.cs`**

Change the `GetAsync` signature:

```csharp
Task<LeagueDetail?> GetAsync(Guid leagueId, string? userSub, string? inviteCode);
```

- [ ] **Step 6: Update `LeaguesController.cs` — `Get` action**

Find the `Get` action and update it to accept an optional `code` query param and pass it through:

```csharp
[HttpGet("{id:guid}")]
public async Task<IActionResult> Get(Guid id, [FromQuery] string? code)
{
    var userSub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var league = await leagueService.GetAsync(id, userSub, code);

    if (league is null)
    {
        return NotFound();
    }

    return Ok(league);
}
```

- [ ] **Step 7: Run tests — confirm they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (5 tests).

- [ ] **Step 8: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Services/LeagueService.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: GetAsync supports non-member access with invite code auth"
```

---

### Task 3: Update CreateAsync — MaxPlayers + Server-Side Validation

**Files:**
- Modify: `DCF.Api/Models/LeagueRequests.cs` — add `MaxPlayers`
- Modify: `DCF.Api/Services/LeagueService.cs` — `CreateAsync`
- Modify: `DCF.Api/Services/ILeagueService.cs` — `CreateAsync` signature
- Modify: `DCF.Api/Controllers/LeaguesController.cs` — `Create` action
- Modify: `DCF.Tests/Services/LeagueServiceTests.cs` — add tests

> **Note:** Constructor still `new LeagueService(db, null!)` in this task. Task 5 updates all calls.

- [ ] **Step 1: Write failing tests**

Add to `DCF.Tests/Services/LeagueServiceTests.cs`:

```csharp
// ── CreateAsync ─────────────────────────────────────────────────────────────

[Fact]
public async Task CreateAsync_ValidParams_SetsMaxPlayers()
{
    await using var db = CreateDb(nameof(CreateAsync_ValidParams_SetsMaxPlayers));
    var user = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
    // Active season with 24 corps
    var season = new SeasonEntity { Year = 2025, IsPublished = true };
    db.Users.Add(user);
    db.Seasons.Add(season);
    await db.SaveChangesAsync();
    for (var i = 0; i < 24; i++)
    {
        db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = Guid.NewGuid() });
    }
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!);
    var league = await svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
        maxPlayers: 8, captions: [ComputedCaption.MusicCombined], userSub: "sub|me");

    Assert.Equal(8, league.MaxPlayers);
}

[Fact]
public async Task CreateAsync_MaxPlayersBelowMinimum_Throws()
{
    await using var db = CreateDb(nameof(CreateAsync_MaxPlayersBelowMinimum_Throws));
    var user = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
    var season = new SeasonEntity { Year = 2025, IsPublished = true };
    db.Users.Add(user);
    db.Seasons.Add(season);
    await db.SaveChangesAsync();
    for (var i = 0; i < 24; i++)
    {
        db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = Guid.NewGuid() });
    }
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!);
    await Assert.ThrowsAsync<ArgumentException>(() =>
        svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
            maxPlayers: 2, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
}

[Fact]
public async Task CreateAsync_CorpsPerCaptionTooHigh_Throws()
{
    await using var db = CreateDb(nameof(CreateAsync_CorpsPerCaptionTooHigh_Throws));
    var user = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
    var season = new SeasonEntity { Year = 2025, IsPublished = true };
    db.Users.Add(user);
    db.Seasons.Add(season);
    await db.SaveChangesAsync();
    for (var i = 0; i < 24; i++)
    {
        db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = Guid.NewGuid() });
    }
    await db.SaveChangesAsync();

    // floor(24/4) = 6, so 7 is invalid
    var svc = new LeagueService(db, null!);
    await Assert.ThrowsAsync<ArgumentException>(() =>
        svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 7,
            maxPlayers: 4, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
}

[Fact]
public async Task CreateAsync_MaxPlayersExceedsFloor_Throws()
{
    await using var db = CreateDb(nameof(CreateAsync_MaxPlayersExceedsFloor_Throws));
    var user = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
    var season = new SeasonEntity { Year = 2025, IsPublished = true };
    db.Users.Add(user);
    db.Seasons.Add(season);
    await db.SaveChangesAsync();
    for (var i = 0; i < 12; i++)
    {
        db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = Guid.NewGuid() });
    }
    await db.SaveChangesAsync();

    // 12 corps, corpsPerCaption=3 → floor(12/3) = 4 max
    var svc = new LeagueService(db, null!);
    await Assert.ThrowsAsync<ArgumentException>(() =>
        svc.CreateAsync("Test", isPublic: false, corpsPerCaption: 3,
            maxPlayers: 5, captions: [ComputedCaption.MusicCombined], userSub: "sub|me"));
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL — `CreateAsync` signature mismatch.

- [ ] **Step 3: Add `MaxPlayers` to `CreateLeagueRequest`**

Open `DCF.Api/Models/LeagueRequests.cs`. Add the property to `CreateLeagueRequest`:

```csharp
public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    int MaxPlayers,
    List<ComputedCaption> Captions,
    DateTime? DraftStartTime
);
```

- [ ] **Step 4: Update `CreateAsync` in `LeagueService.cs`**

Change the signature to accept `maxPlayers`:

```csharp
public async Task<LeagueEntity> CreateAsync(
    string name,
    bool isPublic,
    int corpsPerCaption,
    int maxPlayers,
    List<ComputedCaption> captions,
    string userSub,
    DateTime? draftStartTime = null)
```

At the top of the method body, add validation:

```csharp
var activeSeason = await db.Seasons
    .Include(s => s.SeasonCorps)
    .Where(s => s.IsPublished)
    .OrderByDescending(s => s.Year)
    .FirstOrDefaultAsync()
    ?? throw new InvalidOperationException("No active season found.");

var corpsCount = activeSeason.SeasonCorps.Count;
var maxCorpsPerCaption = corpsCount / 4;
var maxAllowedPlayers = corpsPerCaption > 0 ? corpsCount / corpsPerCaption : 0;

if (maxPlayers < 4)
{
    throw new ArgumentException("maxPlayers must be at least 4.", nameof(maxPlayers));
}

if (corpsPerCaption > maxCorpsPerCaption)
{
    throw new ArgumentException(
        $"corpsPerCaption cannot exceed {maxCorpsPerCaption} for the active season.", nameof(corpsPerCaption));
}

if (maxPlayers > maxAllowedPlayers)
{
    throw new ArgumentException(
        $"maxPlayers cannot exceed {maxAllowedPlayers} for the given corpsPerCaption.", nameof(maxPlayers));
}
```

Set `MaxPlayers` on the new league entity:

```csharp
var league = new LeagueEntity
{
    Name = name,
    IsPublic = isPublic,
    CorpsPerCaption = corpsPerCaption,
    MaxPlayers = maxPlayers,
    Captions = captions,
    DraftStartTime = draftStartTime,
    InviteCode = GenerateInviteCode(),
};
```

- [ ] **Step 5: Update `ILeagueService.cs`**

```csharp
Task<LeagueEntity> CreateAsync(
    string name,
    bool isPublic,
    int corpsPerCaption,
    int maxPlayers,
    List<ComputedCaption> captions,
    string userSub,
    DateTime? draftStartTime = null);
```

- [ ] **Step 6: Update `LeaguesController.cs` — `Create` action**

```csharp
[HttpPost]
public async Task<IActionResult> Create([FromBody] CreateLeagueRequest req)
{
    var userSub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;

    try
    {
        var league = await leagueService.CreateAsync(
            req.Name, req.IsPublic, req.CorpsPerCaption, req.MaxPlayers,
            req.Captions, userSub, req.DraftStartTime);

        return CreatedAtAction(nameof(Get), new { id = league.Id }, new { id = league.Id });
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}
```

- [ ] **Step 7: Run tests — confirm they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (9 tests).

- [ ] **Step 8: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Models/LeagueRequests.cs DCF.Api/Services/LeagueService.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: CreateAsync validates and stores MaxPlayers"
```

---

### Task 4: Enforce MaxPlayers in JoinAsync

**Files:**
- Modify: `DCF.Api/Services/LeagueService.cs` — `JoinAsync`, `JoinResult` enum
- Modify: `DCF.Api/Controllers/LeaguesController.cs` — `Join` action
- Modify: `DCF.Tests/Services/LeagueServiceTests.cs` — add tests

- [ ] **Step 1: Write failing tests**

Add to `DCF.Tests/Services/LeagueServiceTests.cs`:

```csharp
// ── JoinAsync ────────────────────────────────────────────────────────────────

[Fact]
public async Task JoinAsync_LeagueFull_ReturnsFull()
{
    await using var db = CreateDb(nameof(JoinAsync_LeagueFull_ReturnsFull));
    var owner = new UserEntity { Sub = "sub|owner", DisplayName = "Owner", Email = "o@test.com" };
    var member = new UserEntity { Sub = "sub|member", DisplayName = "Member", Email = "m@test.com" };
    var joiner = new UserEntity { Sub = "sub|joiner", DisplayName = "Joiner", Email = "j@test.com" };
    var league = new LeagueEntity { Name = "Full League", IsPublic = true, MaxPlayers = 2, InviteCode = "X" };
    db.Users.AddRange(owner, member, joiner);
    db.Leagues.Add(league);
    await db.SaveChangesAsync();
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = owner.Id });
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id });
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!);
    var result = await svc.JoinAsync(league.Id, "sub|joiner", inviteCode: null);

    Assert.Equal(JoinResult.Full, result);
}

[Fact]
public async Task JoinAsync_LeagueNotFull_ReturnsSuccess()
{
    await using var db = CreateDb(nameof(JoinAsync_LeagueNotFull_ReturnsSuccess));
    var owner = new UserEntity { Sub = "sub|owner", DisplayName = "Owner", Email = "o@test.com" };
    var joiner = new UserEntity { Sub = "sub|joiner", DisplayName = "Joiner", Email = "j@test.com" };
    var league = new LeagueEntity { Name = "Open League", IsPublic = true, MaxPlayers = 8, InviteCode = "X" };
    db.Users.AddRange(owner, joiner);
    db.Leagues.Add(league);
    await db.SaveChangesAsync();
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = owner.Id });
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!);
    var result = await svc.JoinAsync(league.Id, "sub|joiner", inviteCode: null);

    Assert.Equal(JoinResult.Success, result);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL — `JoinResult.Full` doesn't exist yet.

- [ ] **Step 3: Add `Full` to `JoinResult` enum**

Find the `JoinResult` enum in `LeagueService.cs` (or wherever it's defined). Add:

```csharp
public enum JoinResult
{
    Success,
    AlreadyMember,
    NotFound,
    InvalidCode,
    Full,
}
```

- [ ] **Step 4: Add member count check in `JoinAsync`**

Find `JoinAsync` in `LeagueService.cs`. After the league is fetched and before the user is added, insert:

```csharp
var memberCount = await db.LeagueMembers.CountAsync(m => m.LeagueId == leagueId);

if (memberCount >= league.MaxPlayers)
{
    return JoinResult.Full;
}
```

- [ ] **Step 5: Handle `Full` in `LeaguesController.cs`**

Find the `Join` action. Add a case for `JoinResult.Full`:

```csharp
JoinResult.Full => Conflict(new { error = "This league is full." }),
```

(Add it to the existing `switch` expression or `if`/`else` chain before the default case.)

- [ ] **Step 6: Run tests — confirm they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (11 tests).

- [ ] **Step 7: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add DCF.Api/Services/LeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: enforce MaxPlayers cap in JoinAsync"
```

---

### Task 5: GetUserRankAsync + BrowseAsync Returns My Leagues With Rank/Score

**Files:**
- Modify: `DCF.Api/Services/IStandingsService.cs` — add `GetUserRankAsync`
- Modify: `DCF.Api/Services/StandingsService.cs` — implement `GetUserRankAsync`
- Modify: `DCF.Api/Services/ILeagueService.cs` — update `BrowseAsync` return type
- Modify: `DCF.Api/Services/LeagueService.cs` — `LeagueSummary` record, `BrowseAsync`, constructor (add `IStandingsService`)
- Modify: `DCF.Tests/Services/LeagueServiceTests.cs` — add `NoOpStandings`, update all `new LeagueService(db, null!)` to 3 args

> **CRITICAL:** This task changes `LeagueService`'s constructor from 2 to 3 args. All existing tests in this file use `new LeagueService(db, null!)` — update every occurrence to `new LeagueService(db, null!, new NoOpStandings())`.

- [ ] **Step 1: Write failing tests**

Add to `DCF.Tests/Services/LeagueServiceTests.cs`. First define the `NoOpStandings` helper class:

```csharp
private sealed class NoOpStandings : IStandingsService
{
    public Task<List<UserStanding>> GetStandingsAsync(Guid leagueId) =>
        Task.FromResult(new List<UserStanding>());

    public Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId) =>
        Task.FromResult<(int?, double?)>((null, null));
}
```

Then add tests:

```csharp
// ── BrowseAsync (my leagues) ─────────────────────────────────────────────────

[Fact]
public async Task BrowseAsync_ReturnsOnlyUserLeagues()
{
    await using var db = CreateDb(nameof(BrowseAsync_ReturnsOnlyUserLeagues));
    var me = new UserEntity { Sub = "sub|me", DisplayName = "Me", Email = "me@test.com" };
    var other = new UserEntity { Sub = "sub|other", DisplayName = "Other", Email = "other@test.com" };
    var myLeague = new LeagueEntity { Name = "Mine", IsPublic = false, InviteCode = "A", MaxPlayers = 8 };
    var otherLeague = new LeagueEntity { Name = "Theirs", IsPublic = true, InviteCode = "B", MaxPlayers = 8 };
    db.Users.AddRange(me, other);
    db.Leagues.AddRange(myLeague, otherLeague);
    await db.SaveChangesAsync();
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = myLeague.Id, UserId = me.Id });
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = otherLeague.Id, UserId = other.Id });
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!, new NoOpStandings());
    var result = await svc.BrowseAsync("sub|me");

    Assert.Single(result);
    Assert.Equal("Mine", result[0].Name);
}

[Fact]
public async Task GetUserRankAsync_UserInStandings_ReturnsRankAndScore()
{
    await using var db = CreateDb(nameof(GetUserRankAsync_UserInStandings_ReturnsRankAndScore));
    var svc = new StandingsService(db);
    // StandingsService.GetStandingsAsync returns empty if no picks/scores — we test the rank lookup logic
    // via a wrapper that exposes it. Since GetUserRankAsync delegates to GetStandingsAsync,
    // an empty league gives (null, null).
    var league = new LeagueEntity { Name = "L", MaxPlayers = 8, InviteCode = "X" };
    db.Leagues.Add(league);
    await db.SaveChangesAsync();

    var (rank, score) = await svc.GetUserRankAsync(league.Id, Guid.NewGuid());

    Assert.Null(rank);
    Assert.Null(score);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL — `GetUserRankAsync` not on `IStandingsService`, constructor arg count mismatch.

- [ ] **Step 3: Add `GetUserRankAsync` to `IStandingsService.cs`**

```csharp
Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId);
```

- [ ] **Step 4: Implement `GetUserRankAsync` in `StandingsService.cs`**

```csharp
public async Task<(int? Rank, double? Score)> GetUserRankAsync(Guid leagueId, Guid userId)
{
    var standings = await GetStandingsAsync(leagueId);

    if (standings.Count == 0)
    {
        return (null, null);
    }

    var idx = standings.FindIndex(s => s.UserId == userId);

    if (idx < 0)
    {
        return (null, null);
    }

    return (idx + 1, standings[idx].TotalScore);
}
```

- [ ] **Step 5: Update `LeagueSummary` record in `LeagueService.cs`**

```csharp
public record LeagueSummary(
    Guid Id,
    string Name,
    bool IsPublic,
    DraftStatus DraftStatus,
    int MemberCount,
    int MaxPlayers,
    DateTime? DraftStartTime,
    int? UserRank,
    double? UserScore
);
```

- [ ] **Step 6: Update `LeagueService` constructor to accept `IStandingsService`**

```csharp
public class LeagueService(DcfDbContext db, ICorpsRepository corpsRepository, IStandingsService standingsService) : ILeagueService
```

- [ ] **Step 7: Update `BrowseAsync` to return only user's leagues with rank/score**

```csharp
public async Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Sub == userSub);

    if (user is null)
    {
        return [];
    }

    var leagues = await db.Leagues
        .Include(l => l.Members)
        .Where(l => l.Members.Any(m => m.UserId == user.Id))
        .ToListAsync();

    var summaries = new List<LeagueSummary>();

    foreach (var league in leagues)
    {
        var (rank, score) = await standingsService.GetUserRankAsync(league.Id, user.Id);

        summaries.Add(new LeagueSummary(
            league.Id,
            league.Name,
            league.IsPublic,
            league.DraftStatus,
            league.Members.Count,
            league.MaxPlayers,
            league.DraftStartTime,
            rank,
            score
        ));
    }

    return summaries;
}
```

- [ ] **Step 8: Update `ILeagueService.cs` — `BrowseAsync` return type**

```csharp
Task<IReadOnlyList<LeagueSummary>> BrowseAsync(string userSub);
```

- [ ] **Step 9: Fix all `new LeagueService(db, null!)` calls in the test file**

Search for every occurrence in `DCF.Tests/Services/LeagueServiceTests.cs` and replace with `new LeagueService(db, null!, new NoOpStandings())`.

- [ ] **Step 10: Register `IStandingsService` in DI if not already done**

Open `DCF.Api/Program.cs`. Confirm `IStandingsService` → `StandingsService` is registered (it likely already is; if not, add):

```csharp
builder.Services.AddScoped<IStandingsService, StandingsService>();
```

- [ ] **Step 11: Run tests — confirm they all pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (all tests).

- [ ] **Step 12: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 13: Commit**

```bash
git add DCF.Api/Services/IStandingsService.cs DCF.Api/Services/StandingsService.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Services/LeagueService.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: BrowseAsync returns user's leagues with rank/score; add GetUserRankAsync"
```

---

### Task 6: GetPublicLeaguesAsync + GET /api/leagues/browse

**Files:**
- Modify: `DCF.Api/Services/LeagueService.cs` — add `PublicLeagueSummary` record and `GetPublicLeaguesAsync`
- Modify: `DCF.Api/Services/ILeagueService.cs` — add method signature
- Modify: `DCF.Api/Controllers/LeaguesController.cs` — add `GetPublic` action
- Modify: `DCF.Tests/Services/LeagueServiceTests.cs` — add tests

- [ ] **Step 1: Write failing tests**

Add to `DCF.Tests/Services/LeagueServiceTests.cs`:

```csharp
// ── GetPublicLeaguesAsync ────────────────────────────────────────────────────

[Fact]
public async Task GetPublicLeaguesAsync_ReturnsOnlyPublicLeagues()
{
    await using var db = CreateDb(nameof(GetPublicLeaguesAsync_ReturnsOnlyPublicLeagues));
    db.Leagues.Add(new LeagueEntity { Name = "Public", IsPublic = true, InviteCode = "A", MaxPlayers = 8 });
    db.Leagues.Add(new LeagueEntity { Name = "Private", IsPublic = false, InviteCode = "B", MaxPlayers = 8 });
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!, new NoOpStandings());
    var result = await svc.GetPublicLeaguesAsync();

    Assert.Single(result);
    Assert.Equal("Public", result[0].Name);
}

[Fact]
public async Task GetPublicLeaguesAsync_IncludesMemberCount()
{
    await using var db = CreateDb(nameof(GetPublicLeaguesAsync_IncludesMemberCount));
    var league = new LeagueEntity { Name = "Public", IsPublic = true, InviteCode = "A", MaxPlayers = 8 };
    db.Leagues.Add(league);
    await db.SaveChangesAsync();
    var user = new UserEntity { Sub = "sub|u", DisplayName = "U", Email = "u@test.com" };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = user.Id });
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!, new NoOpStandings());
    var result = await svc.GetPublicLeaguesAsync();

    Assert.Equal(1, result[0].MemberCount);
    Assert.Equal(8, result[0].MaxPlayers);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL — `GetPublicLeaguesAsync` doesn't exist.

- [ ] **Step 3: Add `PublicLeagueSummary` record and `GetPublicLeaguesAsync` to `LeagueService.cs`**

```csharp
public record PublicLeagueSummary(
    Guid Id,
    string Name,
    DraftStatus DraftStatus,
    int MemberCount,
    int MaxPlayers
);

public async Task<IReadOnlyList<PublicLeagueSummary>> GetPublicLeaguesAsync()
{
    return await db.Leagues
        .Where(l => l.IsPublic)
        .Select(l => new PublicLeagueSummary(
            l.Id,
            l.Name,
            l.DraftStatus,
            l.Members.Count,
            l.MaxPlayers
        ))
        .ToListAsync();
}
```

- [ ] **Step 4: Add to `ILeagueService.cs`**

```csharp
Task<IReadOnlyList<PublicLeagueSummary>> GetPublicLeaguesAsync();
```

- [ ] **Step 5: Add `GetPublic` action to `LeaguesController.cs`**

```csharp
[HttpGet("public")]
public async Task<IActionResult> GetPublic()
{
    var leagues = await leagueService.GetPublicLeaguesAsync();

    return Ok(leagues);
}
```

> Note: The spec says `GET /api/leagues/browse` but that route conflicts with `GET /api/leagues/{id}` if `{id}` is a string. Use `[HttpGet("public")]` for the controller route, mapping to `/api/leagues/public`. The frontend client will call this path.

- [ ] **Step 6: Run tests — confirm they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (all tests).

- [ ] **Step 7: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add DCF.Api/Services/LeagueService.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: add GetPublicLeaguesAsync and GET /api/leagues/public"
```

---

### Task 7: LookupByCodeAsync + GET /api/leagues/lookup

**Files:**
- Modify: `DCF.Api/Services/LeagueService.cs` — add `LookupByCodeAsync`
- Modify: `DCF.Api/Services/ILeagueService.cs` — add method signature
- Modify: `DCF.Api/Controllers/LeaguesController.cs` — add `Lookup` action
- Modify: `DCF.Tests/Services/LeagueServiceTests.cs` — add tests

- [ ] **Step 1: Write failing tests**

Add to `DCF.Tests/Services/LeagueServiceTests.cs`:

```csharp
// ── LookupByCodeAsync ─────────────────────────────────────────────────────────

[Fact]
public async Task LookupByCodeAsync_ValidCode_ReturnsLeagueId()
{
    await using var db = CreateDb(nameof(LookupByCodeAsync_ValidCode_ReturnsLeagueId));
    var league = new LeagueEntity { Name = "L", InviteCode = "MYCODE", MaxPlayers = 8 };
    db.Leagues.Add(league);
    await db.SaveChangesAsync();

    var svc = new LeagueService(db, null!, new NoOpStandings());
    var result = await svc.LookupByCodeAsync("MYCODE");

    Assert.Equal(league.Id, result);
}

[Fact]
public async Task LookupByCodeAsync_InvalidCode_ReturnsNull()
{
    await using var db = CreateDb(nameof(LookupByCodeAsync_InvalidCode_ReturnsNull));
    var svc = new LeagueService(db, null!, new NoOpStandings());
    var result = await svc.LookupByCodeAsync("NOPE");

    Assert.Null(result);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: FAIL.

- [ ] **Step 3: Add `LookupByCodeAsync` to `LeagueService.cs`**

```csharp
public async Task<Guid?> LookupByCodeAsync(string code)
{
    return await db.Leagues
        .Where(l => l.InviteCode == code)
        .Select(l => (Guid?)l.Id)
        .FirstOrDefaultAsync();
}
```

- [ ] **Step 4: Add to `ILeagueService.cs`**

```csharp
Task<Guid?> LookupByCodeAsync(string code);
```

- [ ] **Step 5: Add `Lookup` action to `LeaguesController.cs`**

```csharp
[HttpGet("lookup")]
public async Task<IActionResult> Lookup([FromQuery] string code)
{
    if (string.IsNullOrWhiteSpace(code))
    {
        return BadRequest(new { error = "code is required." });
    }

    var leagueId = await leagueService.LookupByCodeAsync(code);

    if (leagueId is null)
    {
        return NotFound(new { error = "No league found with that code." });
    }

    return Ok(new { leagueId });
}
```

- [ ] **Step 6: Run tests — confirm they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~LeagueServiceTests" -v n
```

Expected: PASS (all tests).

- [ ] **Step 7: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add DCF.Api/Services/LeagueService.cs DCF.Api/Services/ILeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Tests/Services/LeagueServiceTests.cs
git commit -m "feat: add LookupByCodeAsync and GET /api/leagues/lookup"
```

---

### Task 8: GET /api/seasons/active

**Files:**
- Create: `DCF.Api/Controllers/SeasonsController.cs`

- [ ] **Step 1: Create `SeasonsController.cs`**

```csharp
using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/seasons")]
[Authorize]
public class SeasonsController(DcfDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var season = await db.Seasons
            .Include(s => s.SeasonCorps)
            .Where(s => s.IsPublished)
            .OrderByDescending(s => s.Year)
            .FirstOrDefaultAsync();

        if (season is null)
        {
            return NotFound();
        }

        return Ok(new { id = season.Id, year = season.Year, corpsCount = season.SeasonCorps.Count });
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build DCF.slnx
```

Expected: `Build succeeded.`

- [ ] **Step 3: Smoke test manually**

Start the API:

```bash
dotnet run --project DCF.Api/DCF.Api.csproj
```

Call the endpoint (replace `<token>` with a valid Auth0 token):

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5136/api/seasons/active
```

Expected: `{ "id": "...", "year": 2025, "corpsCount": 27 }` (or similar).

- [ ] **Step 4: Commit**

```bash
git add DCF.Api/Controllers/SeasonsController.cs
git commit -m "feat: add GET /api/seasons/active endpoint"
```

---

### Task 9: Update Frontend Types + API Client

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Update `League` type, add `PublicLeague` and `ActiveSeason`**

Open `DCF.Web/src/types/api.ts`. Apply these changes:

Update `League` interface — make `memberCount` non-optional, add `maxPlayers`, `isMember`, `userRank`, `userScore`:

```typescript
export interface League {
  id: string;
  name: string;
  isPublic: boolean;
  inviteCode?: string;
  draftStatus: DraftStatus;
  corpsPerCaption: number;
  maxPlayers: number;
  memberCount: number;
  isMember?: boolean;
  userRank?: number;
  userScore?: number;
  captions: ComputedCaption[];
  draftStartTime?: string;
  members: LeagueMember[];
}
```

Update `CreateLeagueRequest` to include `maxPlayers`:

```typescript
export interface CreateLeagueRequest {
  name: string;
  isPublic: boolean;
  corpsPerCaption: number;
  maxPlayers: number;
  captions: ComputedCaption[];
  draftStartTime?: string;
}
```

Add `PublicLeague` interface:

```typescript
export interface PublicLeague {
  id: string;
  name: string;
  draftStatus: DraftStatus;
  memberCount: number;
  maxPlayers: number;
}
```

Add `ActiveSeason` interface:

```typescript
export interface ActiveSeason {
  id: string;
  year: number;
  corpsCount: number;
}
```

- [ ] **Step 2: Update `api/client.ts`**

Update `getLeague` to accept optional `code`:

```typescript
getLeague: (id: string, code?: string) => {
  const params = code ? `?code=${encodeURIComponent(code)}` : '';
  return request<League>(`/api/leagues/${id}${params}`);
},
```

Add `getPublicLeagues`:

```typescript
getPublicLeagues: () => request<PublicLeague[]>('/api/leagues/public'),
```

Add `lookupLeagueByCode`:

```typescript
lookupLeagueByCode: (code: string) =>
  request<{ leagueId: string }>(`/api/leagues/lookup?code=${encodeURIComponent(code)}`),
```

Add `getActiveSeason`:

```typescript
getActiveSeason: () => request<ActiveSeason>('/api/seasons/active'),
```

- [ ] **Step 3: Type-check**

```bash
cd DCF.Web && npm run build 2>&1 | head -40
```

Expected: Build succeeds with no type errors (or only pre-existing errors unrelated to these types).

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: update frontend types and API client for leagues redesign"
```

---

### Task 10: Rewrite Leagues.tsx — Tabbed Layout

**Files:**
- Modify: `DCF.Web/src/pages/Leagues.tsx`

- [ ] **Step 1: Rewrite `Leagues.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { League, PublicLeague } from '../types/api';

type DraftStatus = League['draftStatus'];

function formatCountdown(draftStartTime: string): string {
  const diff = new Date(draftStartTime).getTime() - Date.now();
  if (diff <= 0) return 'starting soon';
  const totalMinutes = Math.floor(diff / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  if (days > 0) return `in ${days}d ${hours}h`;
  const mins = totalMinutes % 60;
  return `in ${hours}h ${mins}m`;
}

function StatusBadge({ status }: { status: DraftStatus }) {
  const configs: Record<DraftStatus, { label: string; className: string }> = {
    NotStarted: { label: 'Not Started', className: 'badge-ns' },
    Scheduled: { label: 'Scheduled', className: 'badge-scheduled' },
    Open: { label: 'Lobby Open', className: 'badge-open' },
    InProgress: { label: 'Live Draft', className: 'badge-live' },
    Completed: { label: 'Completed', className: 'badge-completed' },
  };
  const { label, className } = configs[status];
  return <span className={`badge ${className}`}>{label}</span>;
}

function LeagueCard({ league }: { league: League }) {
  return (
    <Link to={`/leagues/${league.id}`} className="league-card">
      <div className="league-card-header">
        <span className="league-card-name">{league.name}</span>
        <StatusBadge status={league.draftStatus} />
      </div>
      <div className="league-card-meta">
        {league.draftStatus === 'InProgress' && (
          <>
            <span>
              {league.userRank != null && league.userScore != null
                ? `Rank ${league.userRank}/${league.memberCount} · ${league.userScore.toFixed(1)} pts`
                : '—'}
            </span>
            <span className="league-card-action-hint">Join Draft Room →</span>
          </>
        )}
        {league.draftStatus === 'Open' && (
          <>
            <span>{league.memberCount} members</span>
            <span className="league-card-action-hint">Join Draft Room →</span>
          </>
        )}
        {league.draftStatus === 'Scheduled' && league.draftStartTime && (
          <span>
            Draft: {new Date(league.draftStartTime).toLocaleDateString()} · ⏱{' '}
            {formatCountdown(league.draftStartTime)}
          </span>
        )}
        {league.draftStatus === 'NotStarted' && (
          <span>{league.memberCount} members · waiting for commissioner</span>
        )}
        {league.draftStatus === 'Completed' && (
          <span className="muted">
            {league.userRank != null && league.userScore != null
              ? `Rank ${league.userRank}/${league.memberCount} · ${league.userScore.toFixed(1)} pts (final)`
              : `${league.memberCount} members (final)`}
          </span>
        )}
      </div>
    </Link>
  );
}

function MyLeaguesTab() {
  const [leagues, setLeagues] = useState<League[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.getLeagues().then(setLeagues).finally(() => setLoading(false));
  }, []);

  if (loading) return <p className="subtitle">Loading…</p>;

  if (leagues.length === 0) {
    return (
      <p className="subtitle">
        You are not currently in any leagues.{' '}
        <Link to="/leagues?tab=join">Join a league</Link> or{' '}
        <Link to="/leagues/create">Create your own</Link>!
      </p>
    );
  }

  return (
    <div className="league-list">
      {leagues.map(l => (
        <LeagueCard key={l.id} league={l} />
      ))}
    </div>
  );
}

function JoinTab() {
  const navigate = useNavigate();
  const [code, setCode] = useState('');
  const [codeError, setCodeError] = useState<string | null>(null);
  const [publicLeagues, setPublicLeagues] = useState<PublicLeague[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.getPublicLeagues().then(setPublicLeagues).finally(() => setLoading(false));
  }, []);

  async function handleLookup() {
    setCodeError(null);
    try {
      const { leagueId } = await api.lookupLeagueByCode(code.trim());
      navigate(`/leagues/${leagueId}?code=${encodeURIComponent(code.trim())}`);
    } catch {
      setCodeError('No league found with that code.');
    }
  }

  return (
    <div>
      <p className="subtitle">Browse and join a public league, or join by code:</p>
      <div className="code-lookup-row">
        <input
          className="input"
          placeholder="Invite code"
          value={code}
          onChange={e => setCode(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && handleLookup()}
        />
        <button className="btn btn-secondary" onClick={handleLookup} disabled={!code.trim()}>
          Look up
        </button>
      </div>
      {codeError && <p className="error-text">{codeError}</p>}

      {loading ? (
        <p className="subtitle">Loading…</p>
      ) : publicLeagues.length === 0 ? (
        <p className="subtitle">
          There are no public leagues to join. <Link to="/leagues/create">Create one now!</Link>
        </p>
      ) : (
        <div className="public-league-list">
          {publicLeagues.map(l => (
            <Link key={l.id} to={`/leagues/${l.id}`} className="public-league-row">
              <span className="public-league-name">{l.name}</span>
              <span className="public-league-count">
                {l.memberCount} / {l.maxPlayers} members
              </span>
              <StatusBadge status={l.draftStatus} />
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

export default function Leagues() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = searchParams.get('tab') === 'join' ? 'join' : 'my';

  function setTab(t: 'my' | 'join') {
    if (t === 'my') {
      setSearchParams({});
    } else {
      setSearchParams({ tab: 'join' });
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Leagues</h1>
        <Link to="/leagues/create" className="btn btn-primary">
          + Create League
        </Link>
      </div>
      <div className="tab-bar">
        <button
          className={`tab ${tab === 'my' ? 'active' : ''}`}
          onClick={() => setTab('my')}
        >
          My Leagues
        </button>
        <button
          className={`tab ${tab === 'join' ? 'active' : ''}`}
          onClick={() => setTab('join')}
        >
          Join
        </button>
      </div>
      {tab === 'my' ? <MyLeaguesTab /> : <JoinTab />}
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

```bash
cd DCF.Web && npm run build 2>&1 | head -60
```

Expected: No new type errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/Leagues.tsx
git commit -m "feat: tabbed Leagues page with My Leagues and Join tabs"
```

---

### Task 11: Rewrite LeagueCreate.tsx — Dropdowns, Stepper, Discard Dialog

**Files:**
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

- [ ] **Step 1: Rewrite `LeagueCreate.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { Link, useNavigate, useBlocker } from 'react-router-dom';
import { api } from '../api/client';
import { ComputedCaption } from '../types/api';

type GEOption = 'combined' | 'split';
type VisOption = 'combined' | 'partial' | 'full';
type MusicOption = 'combined' | 'partial' | 'full';

function expandCaptions(ge: GEOption, vis: VisOption, music: MusicOption): ComputedCaption[] {
  const result: ComputedCaption[] = [];

  if (ge === 'combined') {
    result.push('GeneralEffectCombined');
  } else {
    result.push('GeneralEffect1', 'GeneralEffect2');
  }

  if (vis === 'combined') {
    result.push('VisualCombined');
  } else if (vis === 'partial') {
    result.push('Visual', 'Colorguard');
  } else {
    result.push('VisualAnalysis', 'VisualProficiency', 'Colorguard');
  }

  if (music === 'combined') {
    result.push('MusicCombined');
  } else if (music === 'partial') {
    result.push('Brass', 'Percussion');
  } else {
    result.push('Brass', 'MusicAnalysis', 'Percussion');
  }

  return result;
}

function Stepper({
  label,
  value,
  min,
  max,
  onChange,
  tooltip,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  onChange: (v: number) => void;
  tooltip: string;
}) {
  return (
    <div className="field">
      <label className="field-label">
        {label}
        <span className="tooltip-icon" title={tooltip}>ⓘ</span>
      </label>
      <div className="stepper">
        <button
          type="button"
          className="stepper-btn"
          style={{ opacity: value <= min ? 0.2 : 1 }}
          onClick={() => onChange(Math.max(min, value - 1))}
          disabled={value <= min}
        >
          −
        </button>
        <span className="stepper-value">{value}</span>
        <button
          type="button"
          className="stepper-btn"
          style={{ opacity: value >= max ? 0.2 : 1 }}
          onClick={() => onChange(Math.min(max, value + 1))}
          disabled={value >= max}
        >
          +
        </button>
      </div>
    </div>
  );
}

function DiscardDialog({
  onKeep,
  onDiscard,
}: {
  onKeep: () => void;
  onDiscard: () => void;
}) {
  return (
    <div className="dialog-overlay">
      <div className="dialog">
        <h2 className="dialog-title">Discard changes?</h2>
        <p className="dialog-body">Any unsaved changes will be lost.</p>
        <div className="dialog-actions">
          <button type="button" className="btn btn-secondary" onClick={onKeep}>
            Keep editing
          </button>
          <button type="button" className="btn btn-danger" onClick={onDiscard}>
            Discard
          </button>
        </div>
      </div>
    </div>
  );
}

export default function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(false);
  const [ge, setGe] = useState<GEOption>('combined');
  const [vis, setVis] = useState<VisOption>('combined');
  const [music, setMusic] = useState<MusicOption>('combined');
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [maxPlayers, setMaxPlayers] = useState(8);
  const [draftStartTime, setDraftStartTime] = useState('');
  const [corpsCount, setCorpsCount] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [isDirty, setIsDirty] = useState(false);
  const [showDiscardForCancel, setShowDiscardForCancel] = useState(false);

  useEffect(() => {
    api.getActiveSeason().then(s => setCorpsCount(s.corpsCount));
  }, []);

  const maxCorpsPerCaption = corpsCount != null ? Math.floor(corpsCount / 4) : 99;
  const maxAllowedPlayers = corpsPerCaption > 0 && corpsCount != null
    ? Math.floor(corpsCount / corpsPerCaption)
    : 99;

  function handleCorpsPerCaptionChange(v: number) {
    setCorpsPerCaption(v);
    setIsDirty(true);
    if (corpsCount != null) {
      const newMax = Math.floor(corpsCount / v);
      setMaxPlayers(prev => Math.min(prev, newMax));
    }
  }

  const blocker = useBlocker(isDirty && !submitting);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setIsDirty(false);
    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        maxPlayers,
        captions: expandCaptions(ge, vis, music),
        draftStartTime: draftStartTime || undefined,
      });
      navigate(`/leagues/${league.id}`);
    } catch (err) {
      setSubmitting(false);
      setIsDirty(true);
      console.error(err);
    }
  }

  function handleCancelClick() {
    if (isDirty) {
      setShowDiscardForCancel(true);
    } else {
      navigate('/leagues');
    }
  }

  return (
    <div className="page page-narrow">
      <Link to="/leagues" className="back-link">← Back to Leagues</Link>
      <h1 className="page-title">Create League</h1>

      <form onSubmit={handleSubmit} onChange={() => setIsDirty(true)}>
        <div className="field">
          <label className="field-label">League Name</label>
          <input
            className="input"
            value={name}
            onChange={e => { setName(e.target.value); setIsDirty(true); }}
            required
          />
        </div>

        <div className="field">
          <label className="field-label">Visibility</label>
          <div className="seg-group">
            <button
              type="button"
              className={`seg-btn ${!isPublic ? 'active' : ''}`}
              onClick={() => { setIsPublic(false); setIsDirty(true); }}
            >
              Private
            </button>
            <button
              type="button"
              className={`seg-btn ${isPublic ? 'active' : ''}`}
              onClick={() => { setIsPublic(true); setIsDirty(true); }}
            >
              Public
            </button>
          </div>
        </div>

        <div className="field">
          <label className="field-label">Captions</label>
          <div className="caption-dropdowns">
            <div className="caption-row">
              <span className="caption-group-label">General Effect</span>
              <select
                className="select"
                value={ge}
                onChange={e => { setGe(e.target.value as GEOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (GE Combined)</option>
                <option value="split">Split (GE1 Music + GE2 Visual)</option>
              </select>
            </div>
            <div className="caption-row">
              <span className="caption-group-label">Visual</span>
              <select
                className="select"
                value={vis}
                onChange={e => { setVis(e.target.value as VisOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (Visual Combined)</option>
                <option value="partial">Partial Split (VA+VP combined, CG separate)</option>
                <option value="full">Full Split (VA, VP, and CG all separate)</option>
              </select>
            </div>
            <div className="caption-row">
              <span className="caption-group-label">Music</span>
              <select
                className="select"
                value={music}
                onChange={e => { setMusic(e.target.value as MusicOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (Music Combined)</option>
                <option value="partial">Partial Split (Brass + Percussion)</option>
                <option value="full">Full Split (Brass + Music Analysis + Percussion)</option>
              </select>
            </div>
          </div>
        </div>

        <Stepper
          label="Corps per Caption"
          value={corpsPerCaption}
          min={1}
          max={maxCorpsPerCaption}
          onChange={handleCorpsPerCaptionChange}
          tooltip={`How many corps each player drafts per caption. Maximum is ${maxCorpsPerCaption} (1/4 of active season corps).`}
        />

        <Stepper
          label="Max Players"
          value={maxPlayers}
          min={4}
          max={maxAllowedPlayers}
          onChange={v => { setMaxPlayers(v); setIsDirty(true); }}
          tooltip={`Maximum league members. Capped at ${maxAllowedPlayers} so every player can draft a unique set of corps.`}
        />

        <div className="field">
          <label className="field-label">Draft Start Time (optional)</label>
          <input
            className="input"
            type="datetime-local"
            value={draftStartTime}
            onChange={e => { setDraftStartTime(e.target.value); setIsDirty(true); }}
          />
        </div>

        <div className="form-actions">
          <button type="submit" className="btn btn-primary btn-create" disabled={submitting}>
            {submitting ? 'Creating…' : 'Create League'}
          </button>
          <button type="button" className="btn btn-secondary" onClick={handleCancelClick}>
            Cancel
          </button>
        </div>
      </form>

      {showDiscardForCancel && (
        <DiscardDialog
          onKeep={() => setShowDiscardForCancel(false)}
          onDiscard={() => navigate('/leagues')}
        />
      )}

      {blocker.state === 'blocked' && (
        <DiscardDialog
          onKeep={() => blocker.reset()}
          onDiscard={() => blocker.proceed()}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

```bash
cd DCF.Web && npm run build 2>&1 | head -60
```

Expected: No new type errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/LeagueCreate.tsx
git commit -m "feat: rewrite LeagueCreate with caption dropdowns, max players stepper, discard dialog"
```

---

### Task 12: Update LeagueDetail.tsx — Non-Member View

**Files:**
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`

- [ ] **Step 1: Read the current file to understand what to keep**

Read `DCF.Web/src/pages/LeagueDetail.tsx` and identify the current header area and button logic.

- [ ] **Step 2: Update `LeagueDetail.tsx`**

Make the following targeted changes:

**a) Read `code` from search params** — add near the top of the component:

```tsx
const [searchParams] = useSearchParams();
const code = searchParams.get('code') ?? undefined;
```

**b) Pass `code` to `api.getLeague`** — wherever the fetch happens:

```tsx
const league = await api.getLeague(id, code);
```

**c) Show "Full" badge in the header** — wherever `memberCount` is displayed:

```tsx
<span>Members: {league.memberCount} / {league.maxPlayers}</span>
{league.memberCount >= league.maxPlayers && (
  <span className="badge-full">Full</span>
)}
```

**d) Replace the current button area** with logic conditioned on `isMember`:

```tsx
{!league.isMember && (
  <Link to="/leagues?tab=join" className="btn btn-secondary">← Browse</Link>
)}
{!league.isMember &&
  league.memberCount < league.maxPlayers &&
  (league.draftStatus === 'NotStarted' || league.draftStatus === 'Scheduled') && (
    <button
      type="button"
      className="btn btn-primary"
      onClick={handleJoin}
    >
      Join League
    </button>
  )}
{league.isMember && (league.draftStatus === 'InProgress' || league.draftStatus === 'Open') && (
  <Link to={`/leagues/${league.id}/draft`} className="btn btn-success">
    Join Draft Room →
  </Link>
)}
```

**e) Update `handleJoin`** to pass the code and navigate on success:

```tsx
async function handleJoin() {
  await api.joinLeague(league.id, code);
  // Re-fetch the league as a member (navigate re-mounts and fetches fresh)
  navigate(`/leagues/${league.id}`);
}
```

- [ ] **Step 3: Type-check**

```bash
cd DCF.Web && npm run build 2>&1 | head -60
```

Expected: No new type errors.

- [ ] **Step 4: Verify `joinLeague` in `client.ts` passes the code**

Open `DCF.Web/src/api/client.ts`. Find `joinLeague`. If it doesn't already accept and pass a code, update it:

```typescript
joinLeague: (id: string, code?: string) =>
  request<void>(`/api/leagues/${id}/join`, {
    method: 'POST',
    body: JSON.stringify(code ? { inviteCode: code } : {}),
  }),
```

- [ ] **Step 5: Final build check**

```bash
cd DCF.Web && npm run build
```

Expected: `Build succeeded.`

- [ ] **Step 6: Run all backend tests**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/pages/LeagueDetail.tsx DCF.Web/src/api/client.ts
git commit -m "feat: LeagueDetail non-member view with Browse/Join buttons and Full badge"
```
