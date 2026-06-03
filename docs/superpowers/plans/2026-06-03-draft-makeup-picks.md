# Draft Makeup Picks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a commissioner skips a player's pick during the snake draft, the skipped player earns a makeup pick redeemable any time after the main draft ends; the draft only auto-completes once all makeup picks have been submitted.

**Architecture:** The makeup queue is derived at runtime from gaps in `DraftPicks` — any slot `0..CurrentPickNumber-1` with no matching `DraftPickEntity.PickNumber` represents a skipped player. After the main draft finishes (`CurrentPickNumber >= mainTotalPicks`), the draft stays `InProgress` until all gaps are filled. Makeup picks fill their original gap slot (same `PickNumber`), require no turn order, and are gated only by the normal caption-quota and duplicate-pick guards. No new DB column or entity is needed.

**Tech Stack:** C# / .NET 10 (xUnit, EF Core InMemory), TypeScript / React 19

---

## Files

| File | Change |
|------|--------|
| `DCF.Api/Services/DraftService.cs` | Fix `SkipCurrentPickAsync`; refactor `SubmitPickAsync`; update `PublishDraftStateAsync` |
| `DCF.Tests/Services/DraftServiceTests.cs` | Add `SkipCurrentPickTests`, `SubmitPickMakeupTests`; extend `PublishStateTests` |
| `DCF.Web/src/types/api.ts` | Add `makeupQueue` and `mainTotalPicks` to `DraftState` |
| `DCF.Web/src/pages/DraftRoom.tsx` | `isMyTurn`, makeup bar section, hide skip button during makeup |

---

## Task 1: Fix `SkipCurrentPickAsync` — add makeup guard, remove completion check

**Files:**
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`
- Modify: `DCF.Api/Services/DraftService.cs`

- [ ] **Step 1: Write the failing tests**

Append this class to `DCF.Tests/Services/DraftServiceTests.cs` (after `SubmitPickTests`):

```csharp
public class SkipCurrentPickTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, Guid CommissionerId, Guid MemberId, Guid LeagueId) Seed(
        int currentPickNumber = 0)
    {
        var db = CreateDb();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var draftOrder = JsonSerializer.Serialize(new[] { commissioner.Id.ToString(), member.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = currentPickNumber,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), commissioner.Id, member.Id, league.Id);
    }

    [Fact]
    public async Task Skip_LastMainPick_DraftStaysInProgress()
    {
        // mainTotalPicks = 2 (2 players × 1 caption × 1 corps per caption)
        // CurrentPickNumber = 1 means we are on the last main-draft slot
        var (db, svc, _, _, leagueId) = Seed(currentPickNumber: 1);

        await svc.SkipCurrentPickAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
        Assert.Equal(2, league.CurrentPickNumber);
    }

    [Fact]
    public async Task Skip_DuringMakeupPhase_Throws()
    {
        // CurrentPickNumber = 2 = mainTotalPicks → already in makeup phase
        var (_, svc, _, _, leagueId) = Seed(currentPickNumber: 2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SkipCurrentPickAsync(leagueId, "auth|comm"));

        Assert.Contains("makeup", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SkipCurrentPickTests"
```

Expected: both tests FAIL (first fails because current code sets `Completed`; second fails because no guard exists).

- [ ] **Step 3: Update `SkipCurrentPickAsync` in `DCF.Api/Services/DraftService.cs`**

Replace the method body (lines 191–224) with:

```csharp
public async Task SkipCurrentPickAsync(Guid leagueId, string userSub)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
        ?? throw new UnauthorizedAccessException("User not found");

    var league = await db.Leagues
        .Include(l => l.Members)
        .FirstOrDefaultAsync(l => l.Id == leagueId)
        ?? throw new ArgumentException("League not found");

    if (league.CommissionerUserId != user.Id)
    {
        throw new UnauthorizedAccessException("Only the commissioner can skip picks");
    }

    if (league.DraftStatus != DraftStatus.InProgress)
    {
        throw new InvalidOperationException("Draft is not in progress");
    }

    var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson)!;
    int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;

    if (league.CurrentPickNumber >= mainTotalPicks)
    {
        throw new InvalidOperationException("Cannot skip during the makeup phase");
    }

    league.CurrentPickNumber++;

    await db.SaveChangesAsync();

    await PublishDraftStateAsync(league);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SkipCurrentPickTests"
```

Expected: both tests PASS.

- [ ] **Step 5: Run full test suite to verify no regressions**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "fix: skip cannot complete draft; block skips during makeup phase"
```

---

## Task 2: Refactor `SubmitPickAsync` for makeup phase

**Files:**
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`
- Modify: `DCF.Api/Services/DraftService.cs`

- [ ] **Step 1: Write the failing tests**

Append this class to `DCF.Tests/Services/DraftServiceTests.cs` (after `SkipCurrentPickTests`):

```csharp
public class SubmitPickMakeupTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // Seeds a league where slot 0 was skipped (Player1) and slot 1 was picked by Player2.
    // CurrentPickNumber = 2 = mainTotalPicks → draft is in makeup phase.
    // Player1 still needs a makeup pick; corps1 is taken; corps2 is free.
    private static (DcfDbContext Db, DraftService Service, Guid Player1Id, Guid Player2Id, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) SeedMakeupPhase()
    {
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "Player1", Email = "p1@test.com" };
        var player2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p2", DisplayName = "Player2", Email = "p2@test.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString(), player2.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = 2,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(player1, player2);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player2.Id }
        );
        // Slot 0 has no pick (Player1 was skipped). Slot 1 was picked by Player2.
        db.DraftPicks.Add(new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = league.Id, UserId = player2.Id,
            CorpsId = corps1.Id, Caption = ComputedCaption.Brass,
            PickNumber = 1, RoundNumber = 0
        });
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()),
            player1.Id, player2.Id, league.Id, corps1.Id, corps2.Id);
    }

    // Seeds a league where slot 0 was skipped (Player1) and the main draft is still
    // in progress at slot 1 (Player2's turn). CurrentPickNumber = 1.
    private static (DcfDbContext Db, DraftService Service, Guid Player1Id, Guid Player2Id, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) SeedLastMainPickPending()
    {
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "Player1", Email = "p1@test.com" };
        var player2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p2", DisplayName = "Player2", Email = "p2@test.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString(), player2.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder,
            CurrentPickNumber = 1,
            InviteCode = "TESTCODE",
            DraftableCaptions = [ComputedCaption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(player1, player2);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = player2.Id }
        );
        // No DraftPick at slot 0 — Player1 was skipped. Slot 1 is pending (Player2's turn).
        db.SaveChanges();
        return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()),
            player1.Id, player2.Id, league.Id, corps1.Id, corps2.Id);
    }

    [Fact]
    public async Task LastMainPick_WithPendingSkip_DraftStaysInProgress()
    {
        var (db, svc, _, _, leagueId, corps1Id, _) = SeedLastMainPickPending();

        await svc.SubmitPickAsync(leagueId, "auth|p2", corps1Id, ComputedCaption.Brass);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
    }

    [Fact]
    public async Task MakeupPick_CreatesPickAtGapSlot_AndCompletesDraft()
    {
        var (db, svc, _, _, leagueId, _, corps2Id) = SeedMakeupPhase();

        var (id, pickNumber) = await svc.SubmitPickAsync(leagueId, "auth|p1", corps2Id, ComputedCaption.Brass);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(0, pickNumber); // gap was at slot 0

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Completed, league!.DraftStatus);
    }

    [Fact]
    public async Task MakeupPick_NonSkippedUser_Throws()
    {
        var (_, svc, _, _, leagueId, _, corps2Id) = SeedMakeupPhase();
        // Player2 was not skipped — they have no makeup picks

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitPickAsync(leagueId, "auth|p2", corps2Id, ComputedCaption.Brass));

        Assert.Contains("no makeup picks", ex.Message);
    }

    [Fact]
    public async Task MakeupPick_UserSkippedTwice_FirstPickFillsEarliestGap_DraftStaysInProgress()
    {
        // 3 players, 2 captions, 1 corps per caption → mainTotalPicks = 6
        // draftOrder: [Player1, Player2, Player3]
        // Slots 0 and 3 were skipped (both belong to Player1 in a 3-player snake draft)
        // Snake: R0=P1,P2,P3  R1=P3,P2,P1 → slot 3 is P3, not P1
        // Let's use: draftOrder = [Player1, Player1b, Player2] and skip slots 0 and 2
        // Actually for simplicity: 1 player, 2 captions, 1 corps per caption → mainTotalPicks = 2
        // Skip both slots (slot 0 and slot 1) → 2 makeup picks for Player1
        var db = CreateDb();
        var player1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|p1", DisplayName = "P1", Email = "p1@t.com" };
        var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
        var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
        var draftOrder = JsonSerializer.Serialize(new[] { player1.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(), Name = "T", CommissionerUserId = player1.Id,
            DraftStatus = DraftStatus.InProgress,
            DraftOrderJson = draftOrder, CurrentPickNumber = 2,
            InviteCode = "T", DraftableCaptions = [ComputedCaption.Brass, ComputedCaption.Percussion],
            CorpsPerCaption = 1
        };
        db.Users.Add(player1);
        db.Corps.AddRange(corps1, corps2);
        db.Leagues.Add(league);
        db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = player1.Id });
        // Slots 0 and 1 both skipped — no DraftPicks
        db.SaveChanges();
        var svc = new DraftService(db, new NullMqtt(), new NullPresenceService());

        await svc.SubmitPickAsync(league.Id, "auth|p1", corps1.Id, ComputedCaption.Brass);

        var updatedLeague = await db.Leagues.FindAsync(league.Id);
        Assert.Equal(DraftStatus.InProgress, updatedLeague!.DraftStatus); // one gap still remains

        var pick = await db.DraftPicks.SingleAsync(p => p.LeagueId == league.Id);
        Assert.Equal(0, pick.PickNumber); // filled earliest gap
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SubmitPickMakeupTests"
```

Expected: all four tests FAIL.

- [ ] **Step 3: Refactor `SubmitPickAsync` in `DCF.Api/Services/DraftService.cs`**

Replace the entire `SubmitPickAsync` method (lines 127–189) with:

```csharp
public async Task<(Guid Id, int PickNumber)> SubmitPickAsync(
    Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption)
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
        ?? throw new UnauthorizedAccessException("User not found");

    var league = await db.Leagues
        .Include(l => l.Members)
        .Include(l => l.DraftPicks)
        .FirstOrDefaultAsync(l => l.Id == leagueId)
        ?? throw new ArgumentException("League not found");

    if (league.DraftStatus != DraftStatus.InProgress)
    {
        throw new InvalidOperationException("Draft is not in progress");
    }

    var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson)!;
    int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;
    var completedPickNumbers = new HashSet<int>(league.DraftPicks.Select(p => p.PickNumber));
    bool inMakeupPhase = league.CurrentPickNumber >= mainTotalPicks;

    List<string>? makeupQueue = null;

    if (inMakeupPhase)
    {
        makeupQueue = Enumerable
            .Range(0, mainTotalPicks)
            .Where(i => !completedPickNumbers.Contains(i))
            .Select(i => GetCurrentDrafter(draftOrder, i))
            .ToList();

        if (!makeupQueue.Contains(user.Id.ToString()))
        {
            throw new InvalidOperationException("You have no makeup picks remaining");
        }
    }
    else
    {
        var currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);

        if (currentDrafterId != user.Id.ToString())
        {
            throw new InvalidOperationException("Not your turn");
        }
    }

    var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
        p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);

    if (alreadyPicked)
    {
        throw new InvalidOperationException("That corps+caption is already drafted in this league");
    }

    var picksForCaption = league.DraftPicks.Count(p => p.UserId == user.Id && p.Caption == caption);

    if (picksForCaption >= league.CorpsPerCaption)
    {
        throw new InvalidOperationException($"You have already drafted the maximum {league.CorpsPerCaption} corps for this caption");
    }

    DraftPickEntity pick;

    if (!inMakeupPhase)
    {
        int round = league.CurrentPickNumber / draftOrder.Length;

        pick = new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
            CorpsId = corpsId, Caption = caption,
            PickNumber = league.CurrentPickNumber, RoundNumber = round
        };

        db.DraftPicks.Add(pick);

        league.CurrentPickNumber++;

        if (league.CurrentPickNumber >= mainTotalPicks)
        {
            completedPickNumbers.Add(pick.PickNumber);
            bool noMakeupPicks = !Enumerable.Range(0, mainTotalPicks).Any(i => !completedPickNumbers.Contains(i));

            if (noMakeupPicks)
            {
                league.DraftStatus = DraftStatus.Completed;
            }
        }
    }
    else
    {
        int gapSlot = Enumerable
            .Range(0, mainTotalPicks)
            .First(i => !completedPickNumbers.Contains(i) && GetCurrentDrafter(draftOrder, i) == user.Id.ToString());

        pick = new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
            CorpsId = corpsId, Caption = caption,
            PickNumber = gapSlot, RoundNumber = gapSlot / draftOrder.Length
        };

        db.DraftPicks.Add(pick);

        if (makeupQueue!.Count == 1)
        {
            league.DraftStatus = DraftStatus.Completed;
        }
    }

    await db.SaveChangesAsync();

    await PublishDraftStateAsync(league);

    return (pick.Id, pick.PickNumber);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SubmitPickMakeupTests"
```

Expected: all four tests PASS.

- [ ] **Step 5: Run full test suite to verify no regressions**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: implement makeup phase in SubmitPickAsync"
```

---

## Task 3: Update `PublishDraftStateAsync` — add `makeupQueue` and `mainTotalPicks`

**Files:**
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`
- Modify: `DCF.Api/Services/DraftService.cs`

- [ ] **Step 1: Write the failing test**

Append this test to the existing `PublishStateTests` class in `DCF.Tests/Services/DraftServiceTests.cs`:

```csharp
[Fact]
public async Task PublishStateAsync_WithSkip_IncludesMakeupQueueAndMainTotalPicks()
{
    var db = CreateDb();
    var mqtt = new CapturingMqtt();
    var user1 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "a1", DisplayName = "U1", Email = "u1@t.com" };
    var user2 = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "a2", DisplayName = "U2", Email = "u2@t.com" };
    var svc = new DraftService(db, mqtt, new NullPresenceService());
    var draftOrder = JsonSerializer.Serialize(new[] { user1.Id.ToString(), user2.Id.ToString() });

    var league = new LeagueEntity
    {
        Id = Guid.NewGuid(), Name = "T", CommissionerUserId = user1.Id,
        DraftStatus = DraftStatus.InProgress,
        DraftOrderJson = draftOrder, CurrentPickNumber = 1,
        InviteCode = "ABCD",
        DraftableCaptions = [ComputedCaption.Brass], CorpsPerCaption = 1
    };

    db.Users.AddRange(user1, user2);
    db.Leagues.Add(league);
    db.LeagueMembers.AddRange(
        new LeagueMemberEntity { LeagueId = league.Id, UserId = user1.Id },
        new LeagueMemberEntity { LeagueId = league.Id, UserId = user2.Id }
    );
    // No DraftPick at slot 0 — user1 was skipped; slot 1 is the current pick (not yet made)
    await db.SaveChangesAsync();

    await svc.PublishStateAsync(league.Id);

    Assert.NotNull(mqtt.LastPayloadJson);
    using var doc = JsonDocument.Parse(mqtt.LastPayloadJson!);
    var mainTotalPicks = doc.RootElement.GetProperty("mainTotalPicks").GetInt32();
    var makeupQueue = doc.RootElement.GetProperty("makeupQueue")
        .EnumerateArray().Select(e => e.GetString()!).ToList();

    Assert.Equal(2, mainTotalPicks);
    Assert.Single(makeupQueue);
    Assert.Equal(user1.Id.ToString(), makeupQueue[0]);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PublishStateTests.PublishStateAsync_WithSkip"
```

Expected: FAIL — `makeupQueue` property not found in payload.

- [ ] **Step 3: Update `PublishDraftStateAsync` in `DCF.Api/Services/DraftService.cs`**

Replace the entire `PublishDraftStateAsync` method (lines 238–287) with:

```csharp
private async Task PublishDraftStateAsync(LeagueEntity league)
{
    var draftOrder = JsonSerializer.Deserialize<string[]>(league.DraftOrderJson) ?? [];

    var picks = await db.DraftPicks
        .Include(p => p.Corps)
        .Include(p => p.User)
        .Where(p => p.LeagueId == league.Id)
        .OrderBy(p => p.PickNumber)
        .ToListAsync();

    var members = await db.LeagueMembers
        .Include(m => m.User)
        .Where(m => m.LeagueId == league.Id)
        .ToListAsync();

    int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;
    bool inMakeupPhase = draftOrder.Length > 0 && league.CurrentPickNumber >= mainTotalPicks;

    var completedPickNumbers = new HashSet<int>(picks.Select(p => p.PickNumber));
    var makeupQueue = Enumerable
        .Range(0, Math.Min(league.CurrentPickNumber, mainTotalPicks))
        .Where(i => !completedPickNumbers.Contains(i))
        .Select(i => GetCurrentDrafter(draftOrder, i))
        .ToList();

    string? currentDrafterId = null;

    if (league.DraftStatus == DraftStatus.InProgress && draftOrder.Length > 0 && !inMakeupPhase)
    {
        currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);
    }

    var membersByUserId = members.ToDictionary(m => m.UserId.ToString(), m => m.User.DisplayName);
    var draftOrderPayload = draftOrder
        .Where(membersByUserId.ContainsKey)
        .Select(id => new { UserId = id, DisplayName = membersByUserId[id] })
        .ToArray();

    var onlineUserIds = presenceService.GetOnline(league.Id)
        .Select(id => id.ToString())
        .ToArray();

    var payload = new
    {
        Status = league.DraftStatus.ToString(),
        league.DraftStartTime,
        league.CurrentPickNumber,
        MainTotalPicks = mainTotalPicks,
        MakeupQueue = makeupQueue,
        CurrentDrafterId = currentDrafterId,
        DraftOrder = draftOrderPayload,
        Members = members.Select(m => new { m.UserId, m.User.DisplayName }),
        Picks = picks.Select(p => new
        {
            p.PickNumber, p.RoundNumber,
            UserId = p.UserId, p.User.DisplayName,
            CorpsId = p.CorpsId, CorpsName = p.Corps.Name,
            Caption = p.Caption.ToString()
        }),
        OnlineUserIds = onlineUserIds
    };

    await mqtt.PublishAsync($"dcf/leagues/{league.Id}/draft", payload, retain: true);
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PublishStateTests"
```

Expected: all `PublishStateTests` PASS.

- [ ] **Step 5: Run full test suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: include makeupQueue and mainTotalPicks in draft MQTT payload"
```

---

## Task 4: Update TypeScript `DraftState` type

**Files:**
- Modify: `DCF.Web/src/types/api.ts`

- [ ] **Step 1: Add `makeupQueue` and `mainTotalPicks` to `DraftState`**

In `DCF.Web/src/types/api.ts`, replace the `DraftState` interface:

```ts
export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  onlineUserIds?: string[];
  draftOrder: { userId: string; displayName: string }[];
  members: Member[];
  picks: DraftPick[];
  makeupQueue: string[];
  mainTotalPicks: number;
}
```

- [ ] **Step 2: Verify compilation**

```
cd DCF.Web && npm run build
```

Expected: build succeeds (TypeScript will flag any property accesses that are now inconsistent).

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/types/api.ts
git commit -m "feat: add makeupQueue and mainTotalPicks to DraftState type"
```

---

## Task 5: Update `DraftRoom.tsx` — makeup phase UI

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Add `inMakeupPhase` constant and update `isMyTurn`**

In `DraftRoom.tsx`, find this block (around line 78):

```ts
const isMyTurn = status === 'InProgress' && draftState.currentDrafterId === user?.id;
```

Replace it with:

```ts
const mainTotalPicks = draftState.mainTotalPicks ?? 0;
const inMakeupPhase = status === 'InProgress' && mainTotalPicks > 0 && draftState.currentPickNumber >= mainTotalPicks;
const isMyTurn = status === 'InProgress' && (
  inMakeupPhase
    ? (draftState.makeupQueue ?? []).includes(user?.id ?? '')
    : draftState.currentDrafterId === user?.id
);
```

- [ ] **Step 2: Add makeup phase section to `renderStatus()` inside `renderBar()`**

In `renderStatus()`, find the `if (status === 'InProgress')` block (around line 204). Replace its entire contents with:

```tsx
if (status === 'InProgress') {
  if (inMakeupPhase) {
    const makeupCounts: Record<string, number> = {};
    (draftState.makeupQueue ?? []).forEach(id => {
      makeupCounts[id] = (makeupCounts[id] ?? 0) + 1;
    });
    const pendingPlayers = Object.entries(makeupCounts).map(([userId, count]) => {
      const member = draftState.members.find(m => m.userId === userId);
      return { userId, displayName: member?.displayName ?? 'Unknown', count };
    });

    return (
      <>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--accent)', fontWeight: 700 }}>
            Makeup Picks
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
            {pendingPlayers.map(p =>
              p.count > 1 ? `${p.displayName} ×${p.count}` : p.displayName
            ).join(', ')} still to pick
          </div>
        </div>
        {isMyTurn && (
          <>
            <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
            <div style={{ flexShrink: 0 }}>
              <div style={{ fontSize: 7, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Selected</div>
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{selectionLabel}</div>
            </div>
            <button
              onClick={submitPick}
              disabled={!canSubmit}
              style={{
                background: canSubmit ? 'var(--accent)' : 'var(--border)',
                color: canSubmit ? '#0d0f14' : 'var(--text-faint)',
                border: 'none', borderRadius: 5, padding: '5px 14px',
                fontSize: 10, fontWeight: 800, letterSpacing: '0.5px',
                textTransform: 'uppercase', cursor: canSubmit ? 'pointer' : 'not-allowed',
                flexShrink: 0,
              }}
            >
              Submit Pick
            </button>
          </>
        )}
      </>
    );
  }

  return (
    <>
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--accent)', fontWeight: 700 }}>
          {isMyTurn ? 'On the Clock' : 'Now Picking'}
        </div>
        <div style={{ fontSize: 14, fontWeight: 800, color: 'var(--text-heading)' }}>
          {isMyTurn ? (user?.displayName ?? '—') : (currentDrafter?.displayName ?? '—')}
          <span style={{ fontSize: 9, fontWeight: 400, color: 'var(--text-muted)', marginLeft: 6 }}>· Round {round} · Pick {pick}</span>
        </div>
      </div>
      {isMyTurn && (
        <>
          <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
          <div style={{ flexShrink: 0 }}>
            <div style={{ fontSize: 7, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Selected</div>
            <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{selectionLabel}</div>
          </div>
          <button
            onClick={submitPick}
            disabled={!canSubmit}
            style={{
              background: canSubmit ? 'var(--accent)' : 'var(--border)',
              color: canSubmit ? '#0d0f14' : 'var(--text-faint)',
              border: 'none', borderRadius: 5, padding: '5px 14px',
              fontSize: 10, fontWeight: 800, letterSpacing: '0.5px',
              textTransform: 'uppercase', cursor: canSubmit ? 'pointer' : 'not-allowed',
              flexShrink: 0,
            }}
          >
            Submit Pick
          </button>
        </>
      )}
      {!isMyTurn && !inMakeupPhase && league.isCommissioner && (
        <button
          onClick={skipPick}
          style={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)', borderRadius: 5, padding: '5px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600, flexShrink: 0 }}
        >
          Skip Pick
        </button>
      )}
    </>
  );
}
```

Note: the skip button condition now includes `&& !inMakeupPhase` — makeup picks are unskippable.

- [ ] **Step 3: Verify compilation**

```
cd DCF.Web && npm run build
```

Expected: build succeeds with no TypeScript errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: makeup phase UI — isMyTurn, bar section, hide skip button"
```

---

## Task 6: Update memory

- [ ] **Update the project memory for draft caption quota**

Update `C:\Users\ZugZug\.claude\projects\C--Users-ZugZug-Projects-DCF\memory\project_draft_caption_quota.md` to record that makeup picks are now implemented:

```markdown
---
name: project-draft-caption-quota
description: Draft caption quota feature and makeup picks — both complete
metadata:
  type: project
---

Caption quota counter + makeup picks are both complete.

**Quota (pushed 2026-05-31):** backend quota guard in SubmitPickAsync, sticky headers + x/y counter + full-column dim in draft grid.

**Makeup picks (spec: 2026-06-03-draft-makeup-picks-design.md, plan: 2026-06-03-draft-makeup-picks.md):**
- Skipped picks create gaps in DraftPicks table; makeup queue reconstructed from gaps at runtime — no new DB column needed.
- After main draft ends with gaps, draft stays InProgress; skipped users pick freely (no turn order) until all gaps filled.
- Makeup picks fill original gap slot (PickNumber = gap index); commissioner skip button hidden in makeup phase.

**Why:** Skipped users were permanently losing their picks — unfair in a fantasy league context.

**How to apply:** Draft completion logic in SubmitPickAsync checks both CurrentPickNumber >= mainTotalPicks AND makeupQueue.Count == 0. SkipCurrentPickAsync throws if CurrentPickNumber >= mainTotalPicks.
```
