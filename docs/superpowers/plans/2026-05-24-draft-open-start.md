# Draft Open/Start Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the draft lifecycle into two distinct phases — *open* (order shuffled, `DraftStatus.Open`, MQTT initialized) and *start* (`InProgress`, picks go live) — for both the scheduled auto-path and the commissioner manual path.

**Architecture:** A new `DraftStatus.Open` enum value is the authoritative signal that a draft has been initialized. `DraftService` gains `OpenDraftAsync` methods and `StartDraftAsync` is tightened to require `Open` status. `DraftSchedulerService` becomes a next-action scheduler (single chained task per league) that fires open at T-10min then start at T, with correct restart recovery. The frontend gates the Draft Room behind `Open`+status via retained MQTT; `LeagueDetail` shows an "Open Draft" button for the commissioner.

**Tech Stack:** .NET 10 / C# 13, EF Core 10 InMemory (tests), xUnit, MQTTnet, React 19, TypeScript, React Router v6

---

## File Map

| File | Change |
|---|---|
| `DCF.Data/Models/DraftStatus.cs` | Add `Open` value |
| `DCF.Api/Services/IMqttPublisherService.cs` | Add `bool retain = false` param |
| `DCF.Api/Services/MqttPublisherService.cs` | Implement `retain` flag |
| `DCF.Api/Services/IDraftService.cs` | Add `OpenDraftAsync` overloads |
| `DCF.Api/Services/DraftService.cs` | Split open/start, add `OpenDraftAsync`, update validations, MQTT retain + `DraftOrder` |
| `DCF.Api/Services/DraftSchedulerService.cs` | Replace `ScheduleDraftStart` with `ScheduleNext` next-action logic |
| `DCF.Api/Services/LeagueService.cs` | Update `ScheduleDraftStart` call to `ScheduleNext` |
| `DCF.Api/Controllers/DraftController.cs` | Add `POST /draft/open` endpoint |
| `DCF.Tests/Services/DraftServiceTests.cs` | Add `OpenDraftAsync` and `StartDraftAsync` validation tests |
| `DCF.Web/src/types/api.ts` | Add `'Open'` to `DraftStatus`, add `draftOrder` to `DraftState` |
| `DCF.Web/src/api/client.ts` | Add `openDraft` method |
| `DCF.Web/src/pages/LeagueDetail.tsx` | MQTT gate on "Join Draft Room", add "Open Draft" commissioner button |
| `DCF.Web/src/pages/DraftRoom.tsx` | Redirect guard, Open lobby view, commissioner "Start Draft" button |

---

## Task 1: Add `DraftStatus.Open`

**Files:**
- Modify: `DCF.Data/Models/DraftStatus.cs`
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `DCF.Tests/Services/DraftServiceTests.cs` inside the existing `DraftServiceTests` class:

```csharp
[Fact]
public void DraftStatus_Open_ExistsBetweenScheduledAndInProgress()
{
    var values = Enum.GetValues<DraftStatus>().ToList();
    int scheduledIdx = values.IndexOf(DraftStatus.Scheduled);
    int inProgressIdx = values.IndexOf(DraftStatus.InProgress);

    Assert.True(Enum.IsDefined(typeof(DraftStatus), "Open"));
    int openIdx = values.IndexOf(DraftStatus.Open);
    Assert.True(openIdx > scheduledIdx && openIdx < inProgressIdx);
}
```

- [ ] **Step 2: Run test to confirm it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftStatus_Open"
```

Expected: compile error or FAIL — `DraftStatus.Open` does not exist.

- [ ] **Step 3: Add `Open` to the enum**

Replace `DCF.Data/Models/DraftStatus.cs` entirely:

```csharp
namespace DCF.Data.Models;

public enum DraftStatus { NotStarted, Scheduled, Open, InProgress, Completed }
```

- [ ] **Step 4: Run test to confirm it passes**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftStatus_Open"
```

Expected: PASS

- [ ] **Step 5: Commit**

```
git add DCF.Data/Models/DraftStatus.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: add DraftStatus.Open between Scheduled and InProgress"
```

---

## Task 2: Add `retain` parameter to MQTT publisher

**Files:**
- Modify: `DCF.Api/Services/IMqttPublisherService.cs`
- Modify: `DCF.Api/Services/MqttPublisherService.cs`

- [ ] **Step 1: Update the interface**

Replace `DCF.Api/Services/IMqttPublisherService.cs` entirely:

```csharp
namespace DCF.Api.Services;

public interface IMqttPublisherService
{
    Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update the implementation**

In `DCF.Api/Services/MqttPublisherService.cs`, replace the `PublishAsync` method (lines 52–84):

```csharp
public async Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
{
    if (!_client.IsConnected)
    {
        return;
    }

    await _lock.WaitAsync(ct);

    try
    {
        if (!_client.IsConnected)
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetain(retain)
            .Build();

        await _client.PublishAsync(message, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "MQTT publish failed for topic {Topic}", topic);
    }
    finally
    {
        _lock.Release();
    }
}
```

- [ ] **Step 3: Verify the project builds**

```
dotnet build DCF.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```
git add DCF.Api/Services/IMqttPublisherService.cs DCF.Api/Services/MqttPublisherService.cs
git commit -m "feat: add retain parameter to IMqttPublisherService.PublishAsync"
```

---

## Task 3: Implement `OpenDraftAsync` in `DraftService`

**Files:**
- Modify: `DCF.Api/Services/IDraftService.cs`
- Modify: `DCF.Api/Services/DraftService.cs`
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add the following to `DCF.Tests/Services/DraftServiceTests.cs`. Add the new using directives at the top of the file and the helper classes + test class inside the namespace but outside the existing `DraftServiceTests` class:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
```

Add this helper after the existing `DraftServiceTests` class (still inside the namespace):

```csharp
file sealed class SpyMqtt : IMqttPublisherService
{
    public record Publish(string Topic, bool Retain);
    public List<Publish> Messages { get; } = new();

    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        Messages.Add(new(topic, retain));
        return Task.CompletedTask;
    }
}

public class OpenDraftTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, SpyMqtt Mqtt, Guid CommissionerId, Guid MemberId, Guid LeagueId) Seed(
        DraftStatus status = DraftStatus.NotStarted)
    {
        var db = CreateDb();
        var mqtt = new SpyMqtt();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = status,
            DraftOrderJson = "[]",
            InviteCode = "TESTCODE",
            DraftableCaptions = [Caption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, mqtt), mqtt, commissioner.Id, member.Id, league.Id);
    }

    [Fact]
    public async Task SchedulerPath_SetsStatusToOpen()
    {
        var (db, svc, _, _, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Open, league!.DraftStatus);
    }

    [Fact]
    public async Task SchedulerPath_PopulatesDraftOrder()
    {
        var (db, svc, _, commId, memId, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.NotEqual("[]", league!.DraftOrderJson);
        Assert.Contains(commId.ToString(), league.DraftOrderJson);
        Assert.Contains(memId.ToString(), league.DraftOrderJson);
    }

    [Fact]
    public async Task SchedulerPath_IsIdempotent_WhenAlreadyOpen()
    {
        var (db, svc, mqtt, _, _, leagueId) = Seed(DraftStatus.Open);
        var league = db.Leagues.Find(leagueId)!;
        league.DraftOrderJson = "[\"existing\"]";
        db.SaveChanges();

        await svc.OpenDraftAsync(leagueId);

        var updated = await db.Leagues.FindAsync(leagueId);
        Assert.Equal("[\"existing\"]", updated!.DraftOrderJson);
        Assert.Empty(mqtt.Messages);
    }

    [Fact]
    public async Task CommissionerPath_SetsStatusToOpen()
    {
        var (db, svc, _, commId, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.Open, league!.DraftStatus);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotCommissioner()
    {
        var (_, svc, _, _, _, leagueId) = Seed();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.OpenDraftAsync(leagueId, "auth|mem"));
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenStatusIsNotNotStarted()
    {
        var (_, svc, _, _, _, leagueId) = Seed(DraftStatus.Scheduled);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.OpenDraftAsync(leagueId, "auth|comm"));
    }

    [Fact]
    public async Task PublishesRetainedMqttMessage()
    {
        var (_, svc, mqtt, _, _, leagueId) = Seed();

        await svc.OpenDraftAsync(leagueId);

        Assert.Single(mqtt.Messages);
        Assert.True(mqtt.Messages[0].Retain);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~OpenDraftTests"
```

Expected: compile error — `OpenDraftAsync` not defined on `IDraftService`.

- [ ] **Step 3: Add `OpenDraftAsync` to the interface**

Replace `DCF.Api/Services/IDraftService.cs` entirely:

```csharp
using DCF.Data.Models;

namespace DCF.Api.Services;

public interface IDraftService
{
    Task OpenDraftAsync(Guid leagueId);
    Task OpenDraftAsync(Guid leagueId, string userSub);
    Task StartDraftAsync(Guid leagueId);
    Task StartDraftAsync(Guid leagueId, string userSub);
    Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, Caption caption);
    Task SkipCurrentPickAsync(Guid leagueId, string userSub);
}
```

- [ ] **Step 4: Implement `OpenDraftAsync` in `DraftService`**

Replace `DCF.Api/Services/DraftService.cs` entirely:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DCF.Api.Services;

public class DraftService(DcfDbContext db, IMqttPublisherService mqtt) : IDraftService
{
    public static string GetCurrentDrafter(string[] draftOrder, int currentPickNumber)
    {
        int n = draftOrder.Length;
        int round = currentPickNumber / n;
        int positionInRound = currentPickNumber % n;
        int index = round % 2 == 0 ? positionInRound : n - 1 - positionInRound;

        return draftOrder[index];
    }

    public async Task OpenDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus == DraftStatus.Open)
        {
            return;
        }

        await OpenDraftCoreAsync(league);
    }

    public async Task OpenDraftAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can open the draft");
        }

        if (league.DraftStatus != DraftStatus.NotStarted)
        {
            throw new InvalidOperationException("Draft can only be opened from NotStarted status");
        }

        await OpenDraftCoreAsync(league);
    }

    private async Task OpenDraftCoreAsync(LeagueEntity league)
    {
        var shuffled = league.Members
            .Select(m => m.UserId.ToString())
            .ToArray();
        Random.Shared.Shuffle(shuffled);

        league.DraftOrderJson = JsonSerializer.Serialize(shuffled);
        league.DraftStatus = DraftStatus.Open;

        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);
    }

    public async Task StartDraftAsync(Guid leagueId)
    {
        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.DraftStatus != DraftStatus.Open)
        {
            throw new InvalidOperationException("Draft must be opened before starting");
        }

        await StartDraftCoreAsync(league);
    }

    public async Task StartDraftAsync(Guid leagueId, string userSub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == userSub)
            ?? throw new UnauthorizedAccessException("User not found");

        var league = await db.Leagues
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == leagueId)
            ?? throw new ArgumentException("League not found");

        if (league.CommissionerUserId != user.Id)
        {
            throw new UnauthorizedAccessException("Only the commissioner can start the draft");
        }

        if (league.DraftStatus != DraftStatus.Open)
        {
            throw new InvalidOperationException("Draft must be opened before starting");
        }

        await StartDraftCoreAsync(league);
    }

    private async Task StartDraftCoreAsync(LeagueEntity league)
    {
        league.CurrentPickNumber = 0;
        league.DraftStatus = DraftStatus.InProgress;

        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);
    }

    public async Task<(Guid Id, int PickNumber)> SubmitPickAsync(
        Guid leagueId, string userSub, Guid corpsId, Caption caption)
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
        var currentDrafterId = GetCurrentDrafter(draftOrder, league.CurrentPickNumber);

        if (currentDrafterId != user.Id.ToString())
        {
            throw new InvalidOperationException("Not your turn");
        }

        var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
            p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);

        if (alreadyPicked)
        {
            throw new InvalidOperationException("That corps+caption is already drafted in this league");
        }

        int totalPicks = league.Members.Count * league.DraftableCaptions.Length * league.CorpsPerCaption;
        int round = league.CurrentPickNumber / draftOrder.Length;
        var pick = new DraftPickEntity
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, UserId = user.Id,
            CorpsId = corpsId, Caption = caption,
            PickNumber = league.CurrentPickNumber, RoundNumber = round
        };
        db.DraftPicks.Add(pick);

        league.CurrentPickNumber++;

        if (league.CurrentPickNumber >= totalPicks)
        {
            league.DraftStatus = DraftStatus.Completed;
        }

        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);

        return (pick.Id, pick.PickNumber);
    }

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
        int totalPicks = league.Members.Count * league.DraftableCaptions.Length * league.CorpsPerCaption;

        league.CurrentPickNumber++;

        if (league.CurrentPickNumber >= totalPicks)
        {
            league.DraftStatus = DraftStatus.Completed;
        }

        await db.SaveChangesAsync();
        await PublishDraftStateAsync(league);
    }

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

        string? currentDrafterId = league.DraftStatus == DraftStatus.InProgress && draftOrder.Length > 0
            ? GetCurrentDrafter(draftOrder, league.CurrentPickNumber)
            : null;

        var membersByUserId = members.ToDictionary(m => m.UserId.ToString(), m => m.User.DisplayName);
        var draftOrderPayload = draftOrder
            .Where(membersByUserId.ContainsKey)
            .Select(id => new { UserId = id, DisplayName = membersByUserId[id] })
            .ToArray();

        var payload = new
        {
            Status = league.DraftStatus.ToString(),
            league.DraftStartTime,
            league.CurrentPickNumber,
            CurrentDrafterId = currentDrafterId,
            DraftOrder = draftOrderPayload,
            Members = members.Select(m => new { m.UserId, m.User.DisplayName }),
            Picks = picks.Select(p => new
            {
                p.PickNumber, p.RoundNumber,
                UserId = p.UserId, p.User.DisplayName,
                CorpsId = p.CorpsId, CorpsName = p.Corps.Name,
                Caption = p.Caption.ToString()
            })
        };

        await mqtt.PublishAsync($"dcf/leagues/{league.Id}/draft", payload, retain: true);
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~OpenDraftTests"
```

Expected: all 7 tests PASS.

- [ ] **Step 6: Run full test suite to check for regressions**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Services/IDraftService.cs DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: implement OpenDraftAsync — sets DraftStatus.Open, shuffles draft order, publishes retained MQTT"
```

---

## Task 4: Add `StartDraftAsync` validation tests

**Files:**
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add a new test class to `DCF.Tests/Services/DraftServiceTests.cs` after `OpenDraftTests`:

```csharp
public class StartDraftTests
{
    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (DcfDbContext Db, DraftService Service, Guid CommissionerId, Guid LeagueId) Seed(
        DraftStatus status = DraftStatus.Open)
    {
        var db = CreateDb();
        var mqtt = new SpyMqtt();
        var commissioner = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|comm", DisplayName = "Commissioner", Email = "c@test.com" };
        var member = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|mem", DisplayName = "Member", Email = "m@test.com" };
        var draftOrder = JsonSerializer.Serialize(new[] { commissioner.Id.ToString(), member.Id.ToString() });
        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            CommissionerUserId = commissioner.Id,
            DraftStatus = status,
            DraftOrderJson = status == DraftStatus.Open ? draftOrder : "[]",
            InviteCode = "TESTCODE",
            DraftableCaptions = [Caption.Brass],
            CorpsPerCaption = 1
        };
        db.Users.AddRange(commissioner, member);
        db.Leagues.Add(league);
        db.LeagueMembers.AddRange(
            new LeagueMemberEntity { LeagueId = league.Id, UserId = commissioner.Id },
            new LeagueMemberEntity { LeagueId = league.Id, UserId = member.Id }
        );
        db.SaveChanges();
        return (db, new DraftService(db, mqtt), commissioner.Id, league.Id);
    }

    [Fact]
    public async Task SchedulerPath_ThrowsWhenNotOpen()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.NotStarted);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartDraftAsync(leagueId));

        Assert.Contains("opened", ex.Message);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotOpen()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.NotStarted);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartDraftAsync(leagueId, "auth|comm"));

        Assert.Contains("opened", ex.Message);
    }

    [Fact]
    public async Task CommissionerPath_SetsStatusToInProgress_WhenOpen()
    {
        var (db, svc, _, leagueId) = Seed(DraftStatus.Open);

        await svc.StartDraftAsync(leagueId, "auth|comm");

        var league = await db.Leagues.FindAsync(leagueId);
        Assert.Equal(DraftStatus.InProgress, league!.DraftStatus);
    }

    [Fact]
    public async Task CommissionerPath_ThrowsWhenNotCommissioner()
    {
        var (_, svc, _, leagueId) = Seed(DraftStatus.Open);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.StartDraftAsync(leagueId, "auth|mem"));
    }
}
```

Add `using System.Text.Json;` to the top of the file if not already present.

- [ ] **Step 2: Run tests to confirm they pass** (implementation was already done in Task 3)

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~StartDraftTests"
```

Expected: all 4 tests PASS.

- [ ] **Step 3: Commit**

```
git add DCF.Tests/Services/DraftServiceTests.cs
git commit -m "test: add StartDraftAsync validation tests requiring Open status"
```

---

## Task 5: Update `DraftSchedulerService` to next-action logic

**Files:**
- Modify: `DCF.Api/Services/DraftSchedulerService.cs`
- Modify: `DCF.Api/Services/LeagueService.cs`

- [ ] **Step 1: Replace `DraftSchedulerService`**

Replace `DCF.Api/Services/DraftSchedulerService.cs` entirely:

```csharp
using System.Collections.Concurrent;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class DraftSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<DraftSchedulerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private static readonly TimeSpan OpenLeadTime = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();

        var leagues = await db.Leagues
            .Where(l => (l.DraftStatus == DraftStatus.Scheduled || l.DraftStatus == DraftStatus.Open)
                        && l.DraftStartTime != null)
            .ToListAsync(stoppingToken);

        foreach (var league in leagues)
        {
            ScheduleNext(league.Id, league.DraftStartTime!.Value, league.DraftStatus == DraftStatus.Open);
        }
    }

    public void ScheduleNext(Guid leagueId, DateTimeOffset startTime, bool isAlreadyOpened)
    {
        if (_scheduled.TryRemove(leagueId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        _scheduled[leagueId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!isAlreadyOpened)
                {
                    var openDelay = startTime - OpenLeadTime - DateTimeOffset.UtcNow;

                    if (openDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(openDelay, cts.Token);
                    }

                    if (cts.Token.IsCancellationRequested)
                    {
                        return;
                    }

                    using var openScope = scopeFactory.CreateScope();
                    var openService = openScope.ServiceProvider.GetRequiredService<IDraftService>();

                    await openService.OpenDraftAsync(leagueId);
                }

                var startDelay = startTime - DateTimeOffset.UtcNow;

                if (startDelay > TimeSpan.Zero)
                {
                    await Task.Delay(startDelay, cts.Token);
                }

                if (cts.Token.IsCancellationRequested)
                {
                    return;
                }

                using var startScope = scopeFactory.CreateScope();
                var startService = startScope.ServiceProvider.GetRequiredService<IDraftService>();

                await startService.StartDraftAsync(leagueId);
            }
            catch (OperationCanceledException)
            {
                // expected when rescheduled or cancelled
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Draft scheduling failed for league {Id}", leagueId);
            }
        });
    }

    public void CancelScheduled(Guid leagueId)
    {
        if (_scheduled.TryRemove(leagueId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }
}
```

- [ ] **Step 2: Update `LeagueService.CreateAsync` to call `ScheduleNext`**

In `DCF.Api/Services/LeagueService.cs`, find the call to `draftScheduler.ScheduleDraftStart` (line 92) and replace it:

```csharp
// Before:
draftScheduler.ScheduleDraftStart(league.Id, draftStartTime.Value);

// After:
draftScheduler.ScheduleNext(league.Id, draftStartTime.Value, isAlreadyOpened: false);
```

- [ ] **Step 3: Verify build**

```
dotnet build DCF.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run full test suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Step 5: Commit**

```
git add DCF.Api/Services/DraftSchedulerService.cs DCF.Api/Services/LeagueService.cs
git commit -m "feat: replace ScheduleDraftStart with ScheduleNext — open at T-10min, start at T, restart-safe"
```

---

## Task 6: Add `POST /draft/open` controller endpoint

**Files:**
- Modify: `DCF.Api/Controllers/DraftController.cs`

- [ ] **Step 1: Add the open endpoint**

Replace `DCF.Api/Controllers/DraftController.cs` entirely:

```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/leagues/{leagueId}/draft")]
[Authorize]
public class DraftController(IDraftService draftService) : ControllerBase
{
    private string GetSub()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("No sub claim");
    }

    [HttpPost("open")]
    public async Task<IActionResult> Open(Guid leagueId)
    {
        try
        {
            await draftService.OpenDraftAsync(leagueId, GetSub());

            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid leagueId)
    {
        try
        {
            await draftService.StartDraftAsync(leagueId, GetSub());

            return Ok();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick(Guid leagueId, SubmitPickRequest req)
    {
        try
        {
            var (id, pickNumber) = await draftService.SubmitPickAsync(leagueId, GetSub(), req.CorpsId, req.Caption);

            return Ok(new { Id = id, PickNumber = pickNumber });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("skip")]
    public async Task<IActionResult> Skip(Guid leagueId)
    {
        try
        {
            await draftService.SkipCurrentPickAsync(leagueId, GetSub());

            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

- [ ] **Step 2: Verify build**

```
dotnet build DCF.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add DCF.Api/Controllers/DraftController.cs
git commit -m "feat: add POST /draft/open endpoint for commissioner manual draft open"
```

---

## Task 7: Update frontend types and API client

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Add `Open` to `DraftStatus` and `draftOrder` to `DraftState`**

In `DCF.Web/src/types/api.ts`, replace lines 1 and 46–53:

```typescript
export type DraftStatus = 'NotStarted' | 'Scheduled' | 'Open' | 'InProgress' | 'Completed';
```

And update the `DraftState` interface:

```typescript
export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  draftOrder: { userId: string; displayName: string }[];
  members: Member[];
  picks: DraftPick[];
}
```

- [ ] **Step 2: Add `openDraft` to the API client**

In `DCF.Web/src/api/client.ts`, add after the `startDraft` entry (line 48):

```typescript
  openDraft: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/open`, { method: 'POST' }),
```

- [ ] **Step 3: Run lint to catch type errors**

```
cd DCF.Web && npm run lint
```

Expected: no errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: add DraftStatus.Open and draftOrder field to frontend types, add openDraft API method"
```

---

## Task 8: Gate Draft Room in `LeagueDetail.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`

- [ ] **Step 1: Update `LeagueDetail.tsx`**

Replace `DCF.Web/src/pages/LeagueDetail.tsx` entirely:

```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import { useUser } from '../context/UserContext';
import type { DraftState, League, Standing } from '../types/api';

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const { user: _user } = useAuth0();
  const { user } = useUser();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  const [error, setError] = useState<string | null>(null);
  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) api.getLeague(id).then(setLeague).catch(() => setError('Failed to load league.'));
  }, [id]);

  useEffect(() => {
    if (id) api.getStandings(id).then(setStandings).catch(() => {});
  }, [id, scoresUpdated]);

  if (error) return <div>{error}</div>;
  if (!league) return <div>Loading...</div>;

  const isCommissioner = user?.id !== undefined && user.id === league.commissionerUserId;
  const isDraftRoomOpen = draftState?.status === 'Open'
    || draftState?.status === 'InProgress'
    || draftState?.status === 'Completed';

  const joinLeague = async () => {
    const code = league.isPublic ? undefined : prompt('Enter invite code:') ?? undefined;
    await api.joinLeague(league.id, code);
    window.location.reload();
  };

  const openDraft = () => id && api.openDraft(id).catch(() => {});

  return (
    <div>
      <h2>{league.name}</h2>
      <p>Season: {league.seasonYear} | Status: {league.draftStatus}</p>
      {league.inviteCode && <p>Invite code: <code>{league.inviteCode}</code></p>}

      {isDraftRoomOpen
        ? <Link to={`/leagues/${id}/draft`}>Join Draft Room</Link>
        : <span>Draft Room not open yet</span>}

      {isCommissioner && league.draftStatus === 'NotStarted' && !draftState && (
        <button onClick={openDraft}>Open Draft</button>
      )}

      {!league.isMember && <button onClick={joinLeague}>Join League</button>}

      <h3>Standings</h3>
      <ol>
        {standings.map(s => (
          <li key={s.userId}>{s.displayName} — {s.score.toFixed(3)}</li>
        ))}
      </ol>

      <h3>Members ({league.members?.length ?? 0})</h3>
      <ul>
        {league.members?.map(m => <li key={m.userId}>{m.displayName}</li>)}
      </ul>
    </div>
  );
}
```

- [ ] **Step 2: Run lint**

```
cd DCF.Web && npm run lint
```

Expected: no errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/pages/LeagueDetail.tsx
git commit -m "feat: gate Draft Room link on Open status via MQTT, add Open Draft button for commissioner"
```

---

## Task 9: Update `DraftRoom.tsx` — redirect guard, Open lobby, commissioner buttons

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Replace `DraftRoom.tsx`**

Replace `DCF.Web/src/pages/DraftRoom.tsx` entirely:

```tsx
import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import { useUser } from '../context/UserContext';
import type { Corps, DraftState, League } from '../types/api';

export function DraftRoom() {
  const { id } = useParams<{ id: string }>();
  const { user } = useUser();
  const navigate = useNavigate();
  const [league, setLeague] = useState<League | null>(null);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [selectedCorps, setSelectedCorps] = useState('');
  const [selectedCaption, setSelectedCaption] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);

  useEffect(() => {
    if (!id) return;
    api.getLeague(id).then(setLeague).catch(() => setError('Failed to load league.'));
    api.adminGetCorps().then(setCorps).catch(() => {});
  }, [id]);

  useEffect(() => {
    if (!league) return;
    if (league.draftStatus === 'NotStarted' || league.draftStatus === 'Scheduled') {
      navigate(`/leagues/${id}`);
    }
  }, [league, id, navigate]);

  if (error) return <div>{error}</div>;
  if (!league) return <div>Loading...</div>;

  const isMyTurn = draftState?.status === 'InProgress' &&
    draftState.currentDrafterId === user?.id;

  const isCommissioner = user?.id !== undefined &&
    user.id === league.commissionerUserId;

  const takenCombos = new Set(
    (draftState?.picks ?? []).map(p => `${p.corpsId}|${p.caption}`)
  );

  const availableCorps = corps.filter(c =>
    !league.draftableCaptions.every(cap => takenCombos.has(`${c.id}|${cap}`))
  );

  const submitPick = async () => {
    if (!id || !selectedCorps || !selectedCaption || submitting) return;
    setSubmitting(true);
    try {
      await api.submitPick(id, selectedCorps, selectedCaption);
      setSelectedCorps('');
      setSelectedCaption('');
    } finally {
      setSubmitting(false);
    }
  };

  const skipPick = () => id && api.skipPick(id).catch(() => {});
  const startDraft = () => id && api.startDraft(id).catch(() => {});

  // Open lobby
  if (!draftState || draftState.status === 'Open') {
    return (
      <div>
        <h2>{league.name} — Draft Lobby</h2>
        {league.draftStartTime && (
          <p>Draft starts: {new Date(league.draftStartTime).toLocaleString()}</p>
        )}
        {draftState && draftState.draftOrder.length > 0 && (
          <>
            <h3>Draft Order</h3>
            <ol>
              {draftState.draftOrder.map(m => (
                <li key={m.userId}>{m.displayName}</li>
              ))}
            </ol>
          </>
        )}
        <h3>Members</h3>
        <ul>
          {(draftState?.members ?? league.members ?? []).map(m => (
            <li key={m.userId}>{m.displayName}</li>
          ))}
        </ul>
        {isCommissioner && draftState?.status === 'Open' && (
          <button onClick={startDraft}>Start Draft</button>
        )}
      </div>
    );
  }

  // Completed view
  if (draftState.status === 'Completed') {
    return (
      <div>
        <h2>Draft Complete</h2>
        <ol>
          {draftState.picks.map(p => (
            <li key={p.pickNumber}>
              Pick {p.pickNumber + 1}: {p.displayName} → {p.corpsName} ({p.caption})
            </li>
          ))}
        </ol>
      </div>
    );
  }

  // In-progress draft view
  const currentDrafter = draftState.members.find(
    m => m.userId === draftState.currentDrafterId
  );

  return (
    <div>
      <h2>{league.name} — Live Draft</h2>
      <p>Now picking: <strong>{currentDrafter?.displayName ?? '...'}</strong></p>

      {isMyTurn && (
        <div>
          <h3>Your pick</h3>
          <select value={selectedCorps} onChange={e => setSelectedCorps(e.target.value)}>
            <option value="">Select corps...</option>
            {availableCorps.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          <select value={selectedCaption} onChange={e => setSelectedCaption(e.target.value)}>
            <option value="">Select caption...</option>
            {league.draftableCaptions
              .filter(cap => !takenCombos.has(`${selectedCorps}|${cap}`))
              .map(cap => <option key={cap} value={cap}>{cap}</option>)
            }
          </select>
          <button onClick={submitPick} disabled={!selectedCorps || !selectedCaption || submitting}>
            Submit Pick
          </button>
        </div>
      )}

      {isCommissioner && !isMyTurn && (
        <button onClick={skipPick}>Skip Current Pick</button>
      )}

      <h3>Pick History</h3>
      <ol>
        {draftState.picks.map(p => (
          <li key={p.pickNumber}>
            {p.displayName} → {p.corpsName} ({p.caption})
          </li>
        ))}
      </ol>
    </div>
  );
}
```

- [ ] **Step 2: Run lint**

```
cd DCF.Web && npm run lint
```

Expected: no errors.

- [ ] **Step 3: Run frontend build to catch type errors**

```
cd DCF.Web && npm run build
```

Expected: build succeeds, no TypeScript errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: add DraftRoom redirect guard, Open lobby with draft order, commissioner Start Draft button"
```

---

## Final verification

- [ ] **Run full backend test suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests PASS.

- [ ] **Run full frontend build**

```
cd DCF.Web && npm run build
```

Expected: build succeeds.
