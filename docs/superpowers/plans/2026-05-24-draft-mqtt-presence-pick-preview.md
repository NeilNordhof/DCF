# Draft MQTT Presence & Pick Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add MQTT presence tracking (who is in the Draft Room) and live pick preview (observers see the drafter's tentative selection in real time), paired with a full Draft Room UI redesign replacing dropdowns with a corps × caption pick grid.

**Architecture:** The server gains MQTT subscription capability (renamed `MqttService`) and a singleton `PresenceService` that tracks connected users per league in memory, re-publishing the retained draft state with an `onlineUserIds` array whenever presence changes. The frontend adds a `useDraftPresence` hook that maintains a dedicated MQTT connection with LWT for join/leave signalling and exposes a `publishPickPreview` function called on cell click. `DraftRoom.tsx` is fully rewritten around the pick grid design from the site design spec.

**Tech Stack:** .NET 10 / C# 13, MQTTnet 4.3.7, xUnit, EF Core InMemory (tests), React 19, TypeScript, mqtt.js

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `DCF.Api/Services/IMqttService.cs` | Rename from `IMqttPublisherService.cs` | Publish interface (unchanged shape) |
| `DCF.Api/Services/MqttService.cs` | Rename + extend `MqttPublisherService.cs` | MQTT client — publishes + subscribes to presence topic |
| `DCF.Api/Services/IPresenceService.cs` | Create | In-memory presence tracking interface |
| `DCF.Api/Services/PresenceService.cs` | Create | In-memory presence per league, triggers draft state re-publish |
| `DCF.Api/Services/IDraftService.cs` | Modify | Add `PublishStateAsync(Guid leagueId)` |
| `DCF.Api/Services/DraftService.cs` | Modify | Inject `IPresenceService`, implement `PublishStateAsync`, add `onlineUserIds` to payload |
| `DCF.Api/Program.cs` | Modify | Register `IPresenceService`, update MQTT registration |
| `DCF.Tests/Services/PresenceServiceTests.cs` | Create | Unit tests for `PresenceService` |
| `DCF.Tests/Services/DraftServiceTests.cs` | Modify | Add `NullPresenceService`, update constructors, add `PublishStateAsync` tests |
| `DCF.Web/src/types/api.ts` | Modify | Add `onlineUserIds` to `DraftState`, add `PickPreview` type |
| `DCF.Web/src/mqtt/useDraftPresence.ts` | Create | Dedicated MQTT connection with LWT, publishes presence + pick preview |
| `DCF.Web/src/pages/DraftRoom.tsx` | Rewrite | Pick grid UI, top bar, side panel, presence display, pick preview |

---

## Task 1: Rename `IMqttPublisherService` → `IMqttService`

**Files:**
- Delete + recreate: `DCF.Api/Services/IMqttPublisherService.cs` → `DCF.Api/Services/IMqttService.cs`
- Rename class in: `DCF.Api/Services/MqttPublisherService.cs` → `DCF.Api/Services/MqttService.cs`
- Modify: `DCF.Api/Program.cs`
- Modify: `DCF.Api/Services/DraftService.cs`
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`

- [ ] **Step 1: Create `IMqttService.cs`**

Delete `DCF.Api/Services/IMqttPublisherService.cs` and create `DCF.Api/Services/IMqttService.cs`:

```csharp
namespace DCF.Api.Services;

public interface IMqttService
{
    Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default);
}
```

- [ ] **Step 2: Rename class in `MqttService.cs`**

Delete `DCF.Api/Services/MqttPublisherService.cs` and create `DCF.Api/Services/MqttService.cs` with identical content except:
- Class declaration: `public class MqttService : IMqttService, IHostedService`
- Constructor: `public MqttService(IConfiguration config, ILogger<MqttService> logger)`
- Logger field type: `ILogger<MqttService>`

Full file:

```csharp
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;

namespace DCF.Api.Services;

public class MqttService : IMqttService, IHostedService
{
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<MqttService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MqttService(IConfiguration config, ILogger<MqttService> logger)
    {
        _host = config["Mqtt:Host"] ?? "localhost";
        _port = config.GetValue<int>("Mqtt:Port", 1883);
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithCleanStart()
            .Build();

        try
        {
            await _client.ConnectAsync(options, ct);

            _logger.LogInformation("MQTT connected to {Host}:{Port}", _host, _port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT connection failed — publishing will be silently skipped");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(cancellationToken: ct);
        }
    }

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
                .WithRetainFlag(retain)
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
}
```

- [ ] **Step 3: Update `DraftService.cs` constructor parameter type**

In `DCF.Api/Services/DraftService.cs`, change the constructor:

```csharp
// Before:
public class DraftService(DcfDbContext db, IMqttPublisherService mqtt) : IDraftService

// After:
public class DraftService(DcfDbContext db, IMqttService mqtt) : IDraftService
```

- [ ] **Step 4: Update `Program.cs` MQTT registration**

In `DCF.Api/Program.cs`, replace lines 29–30:

```csharp
// Before:
builder.Services.AddSingleton<IMqttPublisherService, MqttPublisherService>();
builder.Services.AddHostedService(sp => (MqttPublisherService)sp.GetRequiredService<IMqttPublisherService>());

// After:
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService(sp => (MqttService)sp.GetRequiredService<IMqttService>());
```

- [ ] **Step 5: Update `DraftServiceTests.cs` spy/null implementations**

In `DCF.Tests/Services/DraftServiceTests.cs`, rename both `SpyMqtt` and `NullMqtt` to implement `IMqttService`:

```csharp
// In OpenDraftTests:
private sealed class SpyMqtt : IMqttService
{
    public record Publish(string Topic, bool Retain);
    public List<Publish> Messages { get; } = new();

    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        Messages.Add(new(topic, retain));
        return Task.CompletedTask;
    }
}

// In StartDraftTests:
private sealed class NullMqtt : IMqttService
{
    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Build and run tests**

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: Build succeeded, 32 tests pass.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Services/IMqttService.cs DCF.Api/Services/MqttService.cs DCF.Api/Services/DraftService.cs DCF.Api/Program.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "refactor: rename IMqttPublisherService/MqttPublisherService to IMqttService/MqttService"
```

---

## Task 2: Create `IPresenceService` + `PresenceService` with tests

**Files:**
- Create: `DCF.Api/Services/IPresenceService.cs`
- Create: `DCF.Api/Services/PresenceService.cs`
- Create: `DCF.Tests/Services/PresenceServiceTests.cs`

- [ ] **Step 1: Write all failing tests**

Create `DCF.Tests/Services/PresenceServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Services;

public class PresenceServiceTests
{
    private sealed class SpyScopeFactory : IServiceScopeFactory
    {
        public readonly SpyDraftService DraftService = new();

        public IServiceScope CreateScope()
        {
            return new Scope(DraftService);
        }

        private sealed class Scope(IDraftService svc) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(svc);

            public void Dispose() { }
        }

        private sealed class Provider(IDraftService svc) : IServiceProvider
        {
            public object? GetService(Type t)
            {
                return t == typeof(IDraftService) ? svc : null;
            }
        }
    }

    private sealed class SpyDraftService : IDraftService
    {
        public List<Guid> PublishedStateFor { get; } = [];

        public Task PublishStateAsync(Guid leagueId)
        {
            PublishedStateFor.Add(leagueId);
            return Task.CompletedTask;
        }

        public Task OpenDraftAsync(Guid leagueId) => throw new NotImplementedException();
        public Task OpenDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
        public Task StartDraftAsync(Guid leagueId) => throw new NotImplementedException();
        public Task StartDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
        public Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, Caption caption) => throw new NotImplementedException();
        public Task SkipCurrentPickAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
    }

    private static PresenceService Create(SpyScopeFactory? factory = null)
    {
        return new PresenceService(factory ?? new SpyScopeFactory(), NullLogger<PresenceService>.Instance);
    }

    [Fact]
    public async Task HandlePresenceAsync_Online_AddsToSet()
    {
        var svc = Create();
        var leagueId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, userId, online: true);

        Assert.Contains(userId, svc.GetOnline(leagueId));
    }

    [Fact]
    public async Task HandlePresenceAsync_Offline_RemovesFromSet()
    {
        var svc = Create();
        var leagueId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, userId, online: true);
        await svc.HandlePresenceAsync(leagueId, userId, online: false);

        Assert.DoesNotContain(userId, svc.GetOnline(leagueId));
    }

    [Fact]
    public async Task HandlePresenceAsync_Offline_UnknownUser_DoesNotThrow()
    {
        var svc = Create();

        var ex = await Record.ExceptionAsync(
            () => svc.HandlePresenceAsync(Guid.NewGuid(), Guid.NewGuid(), online: false));

        Assert.Null(ex);
    }

    [Fact]
    public void GetOnline_UnknownLeague_ReturnsEmpty()
    {
        var svc = Create();

        Assert.Empty(svc.GetOnline(Guid.NewGuid()));
    }

    [Fact]
    public async Task HandlePresenceAsync_TriggersDraftStatePublish()
    {
        var factory = new SpyScopeFactory();
        var svc = Create(factory);
        var leagueId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, Guid.NewGuid(), online: true);

        Assert.Single(factory.DraftService.PublishedStateFor);
        Assert.Equal(leagueId, factory.DraftService.PublishedStateFor[0]);
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PresenceServiceTests"
```

Expected: compile error — `PresenceService`, `IPresenceService` not found.

- [ ] **Step 3: Create `IPresenceService.cs`**

Create `DCF.Api/Services/IPresenceService.cs`:

```csharp
namespace DCF.Api.Services;

public interface IPresenceService
{
    Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online);
    IReadOnlyCollection<Guid> GetOnline(Guid leagueId);
}
```

- [ ] **Step 4: Create `PresenceService.cs`**

Create `DCF.Api/Services/PresenceService.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace DCF.Api.Services;

public class PresenceService(IServiceScopeFactory scopeFactory, ILogger<PresenceService> logger) : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, bool>> _presence = new();

    public async Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online)
    {
        var league = _presence.GetOrAdd(leagueId, _ => new ConcurrentDictionary<Guid, bool>());

        if (online)
        {
            league[userId] = true;
        }
        else
        {
            league.TryRemove(userId, out _);
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<IDraftService>();

            await draftService.PublishStateAsync(leagueId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish draft state after presence change for league {LeagueId}", leagueId);
        }
    }

    public IReadOnlyCollection<Guid> GetOnline(Guid leagueId)
    {
        if (_presence.TryGetValue(leagueId, out var set))
        {
            return set.Keys.ToList();
        }

        return Array.Empty<Guid>();
    }
}
```

- [ ] **Step 5: Run tests — confirm they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PresenceServiceTests"
```

Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/IPresenceService.cs DCF.Api/Services/PresenceService.cs DCF.Tests/Services/PresenceServiceTests.cs
git commit -m "feat: add IPresenceService and PresenceService with in-memory presence tracking"
```

---

## Task 3: Wire `MqttService` to subscribe to presence topic

**Files:**
- Modify: `DCF.Api/Services/MqttService.cs`

- [ ] **Step 1: Add `IPresenceService` injection and subscription to `MqttService`**

Replace `DCF.Api/Services/MqttService.cs` entirely:

```csharp
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace DCF.Api.Services;

public class MqttService : IMqttService, IHostedService
{
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<MqttService> _logger;
    private readonly IPresenceService _presenceService;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MqttService(IConfiguration config, ILogger<MqttService> logger, IPresenceService presenceService)
    {
        _host = config["Mqtt:Host"] ?? "localhost";
        _port = config.GetValue<int>("Mqtt:Port", 1883);
        _logger = logger;
        _presenceService = presenceService;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithCleanStart()
            .Build();

        try
        {
            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            await _client.ConnectAsync(options, ct);

            await _client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f
                        .WithTopic("dcf/leagues/+/draft/presence")
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build(),
                ct);

            _logger.LogInformation("MQTT connected to {Host}:{Port}", _host, _port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT connection failed — publishing will be silently skipped");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(cancellationToken: ct);
        }
    }

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
                .WithRetainFlag(retain)
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

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var parts = e.ApplicationMessage.Topic.Split('/');

            // Topic format: dcf/leagues/{leagueId}/draft/presence
            if (parts.Length != 5 || !Guid.TryParse(parts[2], out var leagueId))
            {
                return;
            }

            var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            var payload = JsonSerializer.Deserialize<PresencePayload>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
            {
                return;
            }

            bool online = payload.Status.Equals("online", StringComparison.OrdinalIgnoreCase);

            await _presenceService.HandlePresenceAsync(leagueId, payload.UserId, online);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process presence message on topic {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private record PresencePayload(Guid UserId, string Status);
}
```

- [ ] **Step 2: Build**

```
dotnet build DCF.slnx
```

Expected: Build succeeded, 0 errors. (Tests unchanged — MqttService is not unit-tested; wiring verified end-to-end when the full stack runs.)

- [ ] **Step 3: Commit**

```
git add DCF.Api/Services/MqttService.cs
git commit -m "feat: add presence topic subscription to MqttService"
```

---

## Task 4: Extend `DraftService` with `PublishStateAsync` and `onlineUserIds`

**Files:**
- Modify: `DCF.Api/Services/IDraftService.cs`
- Modify: `DCF.Api/Services/DraftService.cs`
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`

- [ ] **Step 1: Add `PublishStateAsync` to `IDraftService`**

Replace `DCF.Api/Services/IDraftService.cs`:

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
    Task PublishStateAsync(Guid leagueId);
}
```

- [ ] **Step 2: Write failing tests for the new method**

Add to `DCF.Tests/Services/DraftServiceTests.cs` — first add a `NullPresenceService` at the top (inside namespace, outside any class):

```csharp
internal sealed class NullPresenceService : IPresenceService
{
    public Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online) => Task.CompletedTask;
    public IReadOnlyCollection<Guid> GetOnline(Guid leagueId) => Array.Empty<Guid>();
}
```

Then update every `new DraftService(db, mqtt)` call in `OpenDraftTests.Seed` and `StartDraftTests.Seed` to `new DraftService(db, mqtt, new NullPresenceService())`.

In `OpenDraftTests.Seed` the return line becomes:
```csharp
return (db, new DraftService(db, mqtt, new NullPresenceService()), mqtt, commissioner.Id, member.Id, league.Id);
```

In `StartDraftTests.Seed` the return line becomes:
```csharp
return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), commissioner.Id, league.Id);
```

Then add a new test class at the bottom of the file:

```csharp
public class PublishStateTests
{
    private sealed class CapturingMqtt : IMqttService
    {
        public string? LastPayloadJson { get; private set; }

        public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
        {
            LastPayloadJson = JsonSerializer.Serialize(
                payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return Task.CompletedTask;
        }
    }

    private sealed class FakePresenceService : IPresenceService
    {
        private readonly Guid[] _online;

        public FakePresenceService(params Guid[] online)
        {
            _online = online;
        }

        public Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online) => Task.CompletedTask;
        public IReadOnlyCollection<Guid> GetOnline(Guid leagueId) => _online;
    }

    private static DcfDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task PublishStateAsync_LeagueNotFound_DoesNotThrow()
    {
        var db = CreateDb();
        var svc = new DraftService(db, new NullMqtt(), new NullPresenceService());

        var ex = await Record.ExceptionAsync(() => svc.PublishStateAsync(Guid.NewGuid()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task PublishStateAsync_IncludesOnlineUserIds()
    {
        var db = CreateDb();
        var mqtt = new CapturingMqtt();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var presence = new FakePresenceService(user1, user2);
        var svc = new DraftService(db, mqtt, presence);

        var league = new LeagueEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            CommissionerUserId = user1,
            DraftStatus = DraftStatus.Open,
            DraftOrderJson = "[]",
            InviteCode = "ABCD1234",
            DraftableCaptions = [Caption.Brass],
            CorpsPerCaption = 1
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync();

        await svc.PublishStateAsync(league.Id);

        Assert.NotNull(mqtt.LastPayloadJson);

        using var doc = JsonDocument.Parse(mqtt.LastPayloadJson!);
        var ids = doc.RootElement.GetProperty("onlineUserIds")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet();

        Assert.Contains(user1.ToString(), ids);
        Assert.Contains(user2.ToString(), ids);
    }
}
```

- [ ] **Step 3: Run tests to confirm new tests fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~PublishStateTests"
```

Expected: compile error — `DraftService` constructor arity mismatch, `PublishStateAsync` not implemented.

- [ ] **Step 4: Update `DraftService.cs`**

Replace `DCF.Api/Services/DraftService.cs`:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DCF.Api.Services;

public class DraftService(DcfDbContext db, IMqttService mqtt, IPresenceService presenceService) : IDraftService
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

        if (league.DraftStatus != DraftStatus.NotStarted && league.DraftStatus != DraftStatus.Scheduled)
        {
            throw new InvalidOperationException("Draft can only be opened from NotStarted or Scheduled status");
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

    public async Task PublishStateAsync(Guid leagueId)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league is null)
        {
            return;
        }

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

        var onlineUserIds = presenceService.GetOnline(league.Id)
            .Select(id => id.ToString())
            .ToArray();

        var payload = new
        {
            Status = league.DraftStatus.ToString(),
            league.DraftStartTime,
            league.CurrentPickNumber,
            CurrentDrafterId = currentDrafterId,
            OnlineUserIds = onlineUserIds,
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

- [ ] **Step 5: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests pass (32 original + 5 presence + 2 publish state = 39 tests).

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/IDraftService.cs DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
git commit -m "feat: add PublishStateAsync to DraftService and include onlineUserIds in MQTT payload"
```

---

## Task 5: Register `PresenceService` in `Program.cs`

**Files:**
- Modify: `DCF.Api/Program.cs`

- [ ] **Step 1: Add `IPresenceService` singleton registration**

In `DCF.Api/Program.cs`, add the following line directly before the `IMqttService` registration (currently line 29):

```csharp
builder.Services.AddSingleton<IPresenceService, PresenceService>();
```

The MQTT block should now read:

```csharp
builder.Services.AddSingleton<IPresenceService, PresenceService>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService(sp => (MqttService)sp.GetRequiredService<IMqttService>());
```

- [ ] **Step 2: Build and run all tests**

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: Build succeeded, 39 tests pass.

- [ ] **Step 3: Commit**

```
git add DCF.Api/Program.cs
git commit -m "feat: register IPresenceService singleton in DI container"
```

---

## Task 6: Frontend types

**Files:**
- Modify: `DCF.Web/src/types/api.ts`

- [ ] **Step 1: Add `onlineUserIds` to `DraftState` and add `PickPreview` type**

In `DCF.Web/src/types/api.ts`, update `DraftState` and add `PickPreview`:

```typescript
export interface DraftState {
  status: DraftStatus;
  draftStartTime?: string;
  currentPickNumber: number;
  currentDrafterId?: string;
  onlineUserIds: string[];
  draftOrder: { userId: string; displayName: string }[];
  members: Member[];
  picks: DraftPick[];
}

export interface PickPreview {
  userId: string;
  corpsId: string;
  caption: string;
}
```

- [ ] **Step 2: Build to surface any TypeScript errors**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds. (`onlineUserIds` is now required on `DraftState`; since `useMqtt` returns `T | null` and we always null-check before accessing, no further changes needed.)

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/types/api.ts
git commit -m "feat: add onlineUserIds to DraftState type and add PickPreview type"
```

---

## Task 7: `useDraftPresence` hook

**Files:**
- Create: `DCF.Web/src/mqtt/useDraftPresence.ts`

- [ ] **Step 1: Create the hook**

Create `DCF.Web/src/mqtt/useDraftPresence.ts`:

```typescript
import mqtt from 'mqtt';
import type { MqttClient } from 'mqtt';
import { useCallback, useEffect, useRef } from 'react';

const MQTT_URL = import.meta.env.VITE_MQTT_URL as string;

export function useDraftPresence(leagueId: string, userId: string | undefined) {
  const clientRef = useRef<MqttClient | null>(null);

  useEffect(() => {
    if (!userId) return;

    const presenceTopic = `dcf/leagues/${leagueId}/draft/presence`;
    const onlinePayload = JSON.stringify({ userId, status: 'online' });
    const offlinePayload = JSON.stringify({ userId, status: 'offline' });

    const client = mqtt.connect(MQTT_URL, {
      will: {
        topic: presenceTopic,
        payload: offlinePayload,
        qos: 1,
        retain: false,
      },
    });

    clientRef.current = client;

    client.on('connect', () => {
      client.publish(presenceTopic, onlinePayload, { qos: 1 });
    });

    client.on('error', () => { /* connection errors are non-fatal */ });

    return () => {
      if (client.connected) {
        client.publish(presenceTopic, offlinePayload, { qos: 1 });
      }
      client.end();
      clientRef.current = null;
    };
  }, [leagueId, userId]);

  const publishPickPreview = useCallback(
    (corpsId: string, caption: string) => {
      const client = clientRef.current;
      if (!client?.connected || !userId) return;
      client.publish(
        `dcf/leagues/${leagueId}/draft/pick`,
        JSON.stringify({ userId, corpsId, caption }),
        { qos: 0 },
      );
    },
    [leagueId, userId],
  );

  return { publishPickPreview };
}
```

- [ ] **Step 2: Build**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds with no TypeScript errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/mqtt/useDraftPresence.ts
git commit -m "feat: add useDraftPresence hook with LWT and publishPickPreview"
```

---

## Task 8: `DraftRoom.tsx` full rewrite

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Replace `DraftRoom.tsx` with the full rewrite**

Replace `DCF.Web/src/pages/DraftRoom.tsx` entirely:

```tsx
import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import { useDraftPresence } from '../mqtt/useDraftPresence';
import { useUser } from '../context/UserContext';
import type { Corps, DraftState, League, PickPreview } from '../types/api';

export function DraftRoom() {
  const { id } = useParams<{ id: string }>();
  const { user } = useUser();
  const navigate = useNavigate();

  const [league, setLeague] = useState<League | null>(null);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [selectedCell, setSelectedCell] = useState<{ corpsId: string; caption: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [activeTab, setActiveTab] = useState<'order' | 'picks'>('order');
  const [activePicksPlayer, setActivePicksPlayer] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [error, setError] = useState<string | null>(null);

  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const pickPreview = useMqtt<PickPreview>(`dcf/leagues/${id}/draft/pick`);
  const { publishPickPreview } = useDraftPresence(id!, user?.id);

  useEffect(() => {
    if (!id) return;
    api.getLeague(id).then(setLeague).catch(() => setError('Failed to load league.'));
    api.adminGetCorps().then(setCorps).catch(() => {});
  }, [id]);

  // Redirect guard — only allow Open, InProgress, Completed
  useEffect(() => {
    if (!league) return;
    if (league.draftStatus === 'NotStarted' || league.draftStatus === 'Scheduled') {
      navigate(`/leagues/${id}`);
    }
  }, [league, id, navigate]);

  // Countdown timer — only ticks during Open lobby
  useEffect(() => {
    if (draftState?.status !== 'Open') return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [draftState?.status]);

  // Initialise picks-tab player selection
  useEffect(() => {
    if (draftState?.members?.length && !activePicksPlayer) {
      setActivePicksPlayer(draftState.members[0].userId);
    }
  }, [draftState, activePicksPlayer]);

  if (error) return <div style={{ padding: 16, color: 'var(--text)' }}>{error}</div>;
  if (!league || !draftState) return <div style={{ padding: 16, color: 'var(--text-muted)' }}>Loading…</div>;

  const status = draftState.status;
  const isMyTurn = status === 'InProgress' && draftState.currentDrafterId === user?.id;
  const isCommissioner = user?.id !== undefined && user.id === league.commissionerUserId;

  const takenSet = new Set(draftState.picks.map(p => `${p.corpsId}|${p.caption}`));
  const isTaken = (corpsId: string, caption: string) => takenSet.has(`${corpsId}|${caption}`);
  const isOnline = (userId: string) => draftState.onlineUserIds.includes(userId);

  const currentDrafter = draftState.members.find(m => m.userId === draftState.currentDrafterId);

  // Pick preview is valid only when the cell is still available and it's the current drafter's preview
  const validPreview = (
    pickPreview &&
    pickPreview.userId === draftState.currentDrafterId &&
    !isMyTurn &&
    !isTaken(pickPreview.corpsId, pickPreview.caption)
  ) ? pickPreview : null;

  const handleCellClick = (corpsId: string, caption: string) => {
    if (!isMyTurn || isTaken(corpsId, caption)) return;
    setSelectedCell({ corpsId, caption });
    publishPickPreview(corpsId, caption);
  };

  const submitPick = async () => {
    if (!id || !selectedCell || submitting) return;
    setSubmitting(true);
    try {
      await api.submitPick(id, selectedCell.corpsId, selectedCell.caption);
      setSelectedCell(null);
    } finally {
      setSubmitting(false);
    }
  };

  const skipPick = () => { if (id) api.skipPick(id).catch(() => {}); };
  const startDraft = () => { if (id) api.startDraft(id).catch(() => {}); };

  const getCountdown = () => {
    if (!league.draftStartTime) return '--:--:--';
    const diff = new Date(league.draftStartTime).getTime() - now;
    if (diff <= 0) return '00:00:00';
    const h = Math.floor(diff / 3600000);
    const m = Math.floor((diff % 3600000) / 60000);
    const s = Math.floor((diff % 60000) / 1000);
    return [h, m, s].map(n => String(n).padStart(2, '0')).join(':');
  };

  // ── Top bar ──────────────────────────────────────────────────────────────

  const renderTopBar = () => {
    if (status === 'Open') {
      return (
        <div style={{ background: 'linear-gradient(90deg, #0f1a0f, #101810)', borderBottom: '2px solid var(--green-border)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
          <div>
            <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--green)', fontWeight: 700 }}>Draft Begins In</div>
            <div style={{ fontSize: 26, fontWeight: 900, color: 'var(--text-h)', fontVariantNumeric: 'tabular-nums' }}>{getCountdown()}</div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <div style={{ textAlign: 'right' }}>
              {league.draftStartTime && (
                <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{new Date(league.draftStartTime).toLocaleString()}</div>
              )}
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
            </div>
            {isCommissioner && (
              <button onClick={startDraft} style={{ border: '1px solid var(--green-border)', color: 'var(--green)', background: 'transparent', borderRadius: 5, padding: '4px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600 }}>
                Start Early
              </button>
            )}
          </div>
        </div>
      );
    }

    if (status === 'InProgress') {
      return (
        <div style={{ background: 'linear-gradient(90deg, #2e1065, #1a1535)', borderBottom: '2px solid var(--accent)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
          <div>
            <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--accent)', fontWeight: 700 }}>
              {isMyTurn ? 'On the Clock' : 'Now Picking'}
            </div>
            <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)' }}>
              {isMyTurn ? (user?.displayName ?? '—') : (currentDrafter?.displayName ?? '—')}
            </div>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
              Round {Math.floor(draftState.currentPickNumber / draftState.members.length) + 1} · Pick {(draftState.currentPickNumber % draftState.members.length) + 1}
            </div>
            <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
          </div>
        </div>
      );
    }

    return (
      <div style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-muted)' }}>Draft Complete</div>
        <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
      </div>
    );
  };

  // ── Pick grid ─────────────────────────────────────────────────────────────

  const renderGrid = () => {
    const captions = league.draftableCaptions;
    const gridLocked = status !== 'InProgress' || !isMyTurn;
    const cellWidth = captions.length <= 3 ? Math.min(88, Math.floor(176 / captions.length)) : 44;

    return (
      <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
        {status === 'Open' && (
          <div style={{ fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>
            Pick board locks until the draft begins
          </div>
        )}
        <table style={{ borderCollapse: 'separate', borderSpacing: 2 }}>
          <thead>
            <tr>
              <th style={{ width: 80 }} />
              {captions.map(cap => (
                <th key={cap} style={{ width: cellWidth, fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-muted)', paddingBottom: 6, textAlign: 'center', fontWeight: 600 }}>
                  {cap}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {corps.map(c => (
              <tr key={c.id}>
                <td style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-h)', textAlign: 'right', paddingRight: 8, whiteSpace: 'nowrap' }}>
                  {c.name}
                </td>
                {captions.map(cap => {
                  const taken = isTaken(c.id, cap);
                  const selected = !gridLocked && selectedCell?.corpsId === c.id && selectedCell?.caption === cap;
                  const previewed = !taken && !selected && validPreview?.corpsId === c.id && validPreview?.caption === cap;
                  const isLobby = status === 'Open';

                  let bg = 'var(--green-bg)';
                  let border = '1px solid var(--green-border)';
                  let boxShadow = 'none';
                  let cursor = gridLocked || taken ? 'not-allowed' : 'pointer';
                  let content: React.ReactNode = <span style={{ color: 'var(--green)', fontSize: 10 }}>●</span>;

                  if (taken) {
                    bg = '#12141a';
                    border = '1px solid var(--border-subtle)';
                    content = <span style={{ color: 'var(--border)', fontSize: 12 }}>—</span>;
                  } else if (selected) {
                    bg = 'var(--accent-bg)';
                    border = '2px solid var(--accent)';
                    boxShadow = '0 0 10px var(--accent-bg)';
                    content = <span style={{ color: 'var(--accent)', fontSize: 16 }}>★</span>;
                  } else if (previewed) {
                    const drafter = draftState.members.find(m => m.userId === validPreview!.userId);
                    bg = '#1e1430';
                    border = '1px dashed var(--accent-border)';
                    content = (
                      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1 }}>
                        <span style={{ color: 'var(--green)', fontSize: 10 }}>●</span>
                        <span style={{ color: 'var(--text-muted)', fontSize: 7, lineHeight: 1 }}>
                          {drafter?.displayName.split(' ')[0] ?? ''}
                        </span>
                      </div>
                    );
                  }

                  return (
                    <td key={cap}>
                      <div
                        onClick={() => handleCellClick(c.id, cap)}
                        style={{
                          width: cellWidth,
                          height: 44,
                          background: bg,
                          border,
                          borderRadius: 4,
                          boxShadow,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          cursor,
                          opacity: isLobby ? 0.45 : 1,
                          userSelect: 'none',
                          transition: 'background 0.1s',
                          pointerEvents: gridLocked ? 'none' : 'auto',
                        }}
                      >
                        {content}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };

  // ── Submit bar ────────────────────────────────────────────────────────────

  const renderSubmitBar = () => {
    if (status === 'Completed') return null;

    const selectedCorps = corps.find(c => c.id === selectedCell?.corpsId);
    const selectionLabel = isMyTurn && selectedCell
      ? `${selectedCorps?.name ?? '—'} · ${selectedCell.caption}`
      : '— · —';
    const canSubmit = isMyTurn && !!selectedCell && !submitting;

    return (
      <div style={{ background: 'var(--surface)', border: '1px solid var(--accent-border)', borderRadius: 6, padding: '8px 12px', margin: '0 12px 12px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexShrink: 0 }}>
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Selected</div>
          <div style={{ fontSize: 11, color: canSubmit ? 'var(--text-h)' : 'var(--text-muted)' }}>{selectionLabel}</div>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {isCommissioner && status === 'InProgress' && !isMyTurn && (
            <button
              onClick={skipPick}
              style={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)', borderRadius: 5, padding: '5px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600 }}
            >
              Skip Pick
            </button>
          )}
          <button
            onClick={submitPick}
            disabled={!canSubmit}
            style={{
              background: canSubmit ? 'var(--accent)' : 'var(--border)',
              color: canSubmit ? '#0d0f14' : 'var(--text-faint)',
              border: 'none', borderRadius: 5, padding: '5px 14px',
              fontSize: 10, fontWeight: 800, letterSpacing: '0.5px',
              textTransform: 'uppercase', cursor: canSubmit ? 'pointer' : 'not-allowed',
            }}
          >
            Submit Pick
          </button>
        </div>
      </div>
    );
  };

  // ── Side panel — Draft Order tab ──────────────────────────────────────────

  const renderDraftOrderTab = () => {
    if (status === 'Open') {
      const onlineCount = draftState.draftOrder.filter(m => isOnline(m.userId)).length;
      return (
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>Draft Order</div>
          {draftState.draftOrder.map((m, i) => (
            <div key={m.userId} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '5px 0', borderBottom: '1px solid var(--border-subtle)' }}>
              <div style={{ width: 20, height: 20, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 9, color: 'var(--text-muted)', flexShrink: 0 }}>
                {i + 1}
              </div>
              <span style={{ fontSize: 7, color: isOnline(m.userId) ? 'var(--green)' : 'var(--text-faint)', flexShrink: 0 }}>
                {isOnline(m.userId) ? '●' : '○'}
              </span>
              <span style={{ fontSize: 11, color: 'var(--text-h)' }}>{m.displayName}</span>
            </div>
          ))}
          <div style={{ fontSize: 9, color: 'var(--text-muted)', marginTop: 8 }}>
            {onlineCount} of {draftState.draftOrder.length} members online
          </div>
        </div>
      );
    }

    const n = draftState.members.length;
    const totalPicks = n * league.draftableCaptions.length * league.corpsPerCaption;

    const upcomingOrder: Array<{ userId: string; displayName: string }> = [];
    if (status === 'InProgress') {
      for (let pick = draftState.currentPickNumber + 1; pick < Math.min(draftState.currentPickNumber + 6, totalPicks); pick++) {
        const round = Math.floor(pick / n);
        const pos = pick % n;
        const idx = round % 2 === 0 ? pos : n - 1 - pos;
        upcomingOrder.push(draftState.draftOrder[idx]);
      }
    }

    return (
      <div>
        {draftState.picks.length > 0 && (
          <div style={{ marginBottom: 8 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Completed</div>
            {draftState.picks.map(p => (
              <div key={p.pickNumber} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', opacity: 0.55 }}>
                <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--text-muted)', flexShrink: 0 }}>
                  {p.pickNumber + 1}
                </div>
                <span style={{ fontSize: 10, color: 'var(--text)' }}>{p.displayName} — {p.corpsName} ({p.caption})</span>
              </div>
            ))}
          </div>
        )}
        {status === 'InProgress' && currentDrafter && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 8px', margin: '4px 0', background: 'var(--accent-bg)', border: '1px solid var(--accent-border)', borderRadius: 5 }}>
            <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--accent-bg)', border: '1px solid var(--accent)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--accent)', flexShrink: 0 }}>
              {draftState.currentPickNumber + 1}
            </div>
            <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-h)' }}>{currentDrafter.displayName}</span>
          </div>
        )}
        {upcomingOrder.length > 0 && (
          <div style={{ marginTop: 8 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Up Next</div>
            {upcomingOrder.map((m, i) => (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', opacity: 0.6 }}>
                <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--text-muted)', flexShrink: 0 }}>
                  {draftState.currentPickNumber + i + 2}
                </div>
                <span style={{ fontSize: 10, color: 'var(--text)' }}>{m.displayName}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    );
  };

  // ── Side panel — Picks tab ────────────────────────────────────────────────

  const renderPicksTab = () => {
    const players = draftState.members;
    const currentPlayer = players.find(m => m.userId === activePicksPlayer) ?? players[0];
    if (!currentPlayer) return null;

    const captions = league.draftableCaptions;
    const playerPicks = draftState.picks.filter(p => p.userId === currentPlayer.userId);

    return (
      <div>
        <div style={{ display: 'flex', gap: 4, marginBottom: 12, flexWrap: 'wrap' }}>
          {players.map(m => (
            <button
              key={m.userId}
              onClick={() => setActivePicksPlayer(m.userId)}
              style={{
                padding: '4px 10px', borderRadius: 12, fontSize: 10, fontWeight: 600,
                cursor: 'pointer', border: 'none',
                background: activePicksPlayer === m.userId ? 'var(--accent)' : 'var(--surface)',
                color: activePicksPlayer === m.userId ? '#0d0f14' : 'var(--text-muted)',
              }}
            >
              {m.displayName.split(' ')[0]}
            </button>
          ))}
        </div>
        {captions.map(cap => {
          const capPicks = playerPicks.filter(p => p.caption === cap);
          const filled = capPicks.length;
          const total = league.corpsPerCaption;
          return (
            <div key={cap} style={{ marginBottom: 10 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
                <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>{cap}</div>
                <div style={{
                  fontSize: 8, padding: '1px 6px', borderRadius: 8, fontWeight: 700,
                  background: filled > 0 ? 'var(--accent-bg)' : 'var(--surface)',
                  color: filled > 0 ? 'var(--accent)' : 'var(--text-faint)',
                  border: `1px solid ${filled > 0 ? 'var(--accent-border)' : 'var(--border)'}`,
                }}>
                  {filled} / {total}
                </div>
              </div>
              {Array.from({ length: total }).map((_, i) => {
                const pick = capPicks[i];
                if (pick) {
                  return (
                    <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '5px 8px', background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, marginBottom: 4 }}>
                      <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--accent-bg)', border: '1px solid var(--accent-border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--accent)', flexShrink: 0 }}>
                        #{pick.pickNumber + 1}
                      </div>
                      <div>
                        <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-h)' }}>{pick.corpsName}</div>
                        <div style={{ fontSize: 8, color: 'var(--text-muted)' }}>Pick #{pick.pickNumber + 1} overall</div>
                      </div>
                    </div>
                  );
                }
                return (
                  <div key={i} style={{ padding: '5px 8px', border: '1px dashed var(--border)', borderRadius: 5, marginBottom: 4 }}>
                    <span style={{ fontSize: 10, fontStyle: 'italic', color: 'var(--text-faint)' }}>Empty</span>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    );
  };

  // ── Layout ────────────────────────────────────────────────────────────────

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', background: 'var(--bg)', color: 'var(--text)', overflow: 'hidden' }}>
      {renderTopBar()}
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
        {/* Left — grid + submit bar */}
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          {renderGrid()}
          {renderSubmitBar()}
        </div>
        {/* Right — side panel */}
        <div style={{ width: 280, background: 'var(--surface-2)', borderLeft: '1px solid var(--border)', display: 'flex', flexDirection: 'column', flexShrink: 0 }}>
          <div style={{ display: 'flex', borderBottom: '1px solid var(--border)', background: 'var(--surface)', flexShrink: 0 }}>
            {(['order', 'picks'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setActiveTab(tab)}
                style={{
                  flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600, cursor: 'pointer',
                  background: 'transparent', border: 'none',
                  color: activeTab === tab ? 'var(--accent)' : 'var(--text-muted)',
                  borderBottom: activeTab === tab ? '2px solid var(--accent)' : '2px solid transparent',
                }}
              >
                {tab === 'order' ? 'Draft Order' : 'Picks'}
              </button>
            ))}
          </div>
          <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
            {activeTab === 'order' ? renderDraftOrderTab() : renderPicksTab()}
          </div>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Build**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds, 0 TypeScript errors.

- [ ] **Step 3: Lint**

```
cd DCF.Web && npm run lint
```

Fix any lint errors before committing.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: rewrite DraftRoom with pick grid, presence display, and pick preview"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| Rename IMqttPublisherService → IMqttService | Task 1 |
| MqttService subscribes to `dcf/leagues/+/draft/presence` | Task 3 |
| PresencePayload parsing (topic + JSON) | Task 3 |
| IPresenceService / PresenceService in-memory tracking | Task 2 |
| PresenceService triggers DraftService.PublishStateAsync | Task 2 |
| IDraftService.PublishStateAsync | Task 4 |
| DraftService injects IPresenceService | Task 4 |
| onlineUserIds in MQTT payload | Task 4 |
| Register IPresenceService singleton | Task 5 |
| DraftState.onlineUserIds type | Task 6 |
| PickPreview type | Task 6 |
| useDraftPresence hook with LWT | Task 7 |
| publishPickPreview stable ref | Task 7 |
| DraftRoom: presence subscription + pick preview subscription | Task 8 |
| DraftRoom: pick grid (corps × caption) | Task 8 |
| DraftRoom: cell states (Available, Taken, Selected, Previewed) | Task 8 |
| DraftRoom: top bar (Open / InProgress / Completed) | Task 8 |
| DraftRoom: countdown timer (ticks every second) | Task 8 |
| DraftRoom: submit bar (hidden in Completed, disabled unless my turn) | Task 8 |
| DraftRoom: commissioner Skip Pick button | Task 8 |
| DraftRoom: Start Early button (commissioner, Open state) | Task 8 |
| DraftRoom: side panel Draft Order tab (lobby with presence dots) | Task 8 |
| DraftRoom: side panel Draft Order tab (InProgress: completed/current/upcoming) | Task 8 |
| DraftRoom: side panel Picks tab (player switcher + caption groups + empty slots) | Task 8 |
| DraftRoom: redirect guard unchanged | Task 8 |
| PresenceServiceTests (5 tests) | Task 2 |
| PublishStateTests (2 tests) | Task 4 |

All spec requirements covered. No gaps found.
