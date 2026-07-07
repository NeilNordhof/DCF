# Scrape Retry & Failure Alerting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `ScrapeSchedulerService`'s automatic scrape path retry up to 5 times (5-minute intervals) before giving up, and email admins when it still fails, without changing the manual "Trigger Score Scrape" button's single-attempt behavior.

**Architecture:** `ExecuteScrapeAsync` starts reporting a 3-state `ScrapeOutcome` (`Succeeded`/`Failed`/`Skipped`) instead of returning `void`. A new `ExecuteScrapeWithRetriesAsync` wraps it in a retry loop that reuses the existing per-show `CancellationTokenSource`. On final exhaustion it emails every `IsAdmin && EmailNotificationsEnabled` user via a new `EmailTemplate.ScrapeFailed` template. The manual-trigger endpoint (`AdminController.TriggerScrape`) is updated to surface the real outcome instead of a fixed `204`, but stays outside the retry loop entirely.

**Tech Stack:** ASP.NET Core background service (`BackgroundService`), EF Core InMemory for tests, xUnit, hand-rolled test fakes (no mocking framework in this repo).

## Global Constraints

- Retry count is 5 retries *after* the initial attempt (6 attempts total) — confirmed against the worked timing example in the spec (`docs/superpowers/specs/2026-07-06-scrape-retry-design.md`): with default `Scraper:DelayMinutes=5` and `Scraper:RetryIntervalMinutes=5`, the 6th and final attempt lands at `ScoresAnnouncedTime + 30 minutes`.
- The manual "Trigger Score Scrape" button/endpoint (`AdminController.TriggerScrape` → `AdminService.TriggerScrapeAsync` → `ExecuteScrapeAsync`) must NOT be wrapped in retries — it stays exactly one attempt, called directly, never through `ExecuteScrapeWithRetriesAsync`.
- No new `ScrapeStatus` value, no frontend changes, no retry-state persistence across API restarts — all explicitly out of scope per the spec.
- This repo has no mocking framework (no Moq/NSubstitute in `DCF.Tests.csproj`) — all test doubles are hand-rolled classes implementing the real interface, following `LeagueServiceTests.cs`'s `NullEmailService`/`NoOpStandings` pattern.
- Test infrastructure shared across more than one test file lives in a dedicated `DCF.Tests/Services/ScrapeTestHelpers.cs` (created in Task 1) rather than being duplicated per file — this was a deliberate pre-flight decision (favoring DRY over this repo's more common per-file `CreateDb`-style duplication) since both `ScrapeSchedulerServiceTests.cs` and `AdminServiceTests.cs` need an identical `ScrapeSchedulerService` construction helper. Simple fakes (`NullMqttService`, `FakeRecapScraperTask`, `RecordingEmailService`) are declared `internal sealed class` at namespace scope inside that same file, matching how `NullEmailService` already works in `LeagueServiceTests.cs`.
- C# style (user's global CLAUDE.md): curly braces always on their own line, all blocks braced even one-liners, one blank line before `return`, one blank line before/after `await` expressions and before/after blocks, never more than one blank line in a row.
- `DCF.Tests` gets `Microsoft.Extensions.DependencyInjection`'s `ServiceCollection`/`BuildServiceProvider` for free — `DCF.Api.csproj` uses `Sdk="Microsoft.NET.Sdk.Web"`, whose implicit `FrameworkReference` to `Microsoft.AspNetCore.App` flows transitively through `DCF.Tests`'s `<ProjectReference Include="..\DCF.Api\DCF.Api.csproj" />`. No `DCF.Tests.csproj` changes needed (confirmed: `NullLogger<T>`/`Options.Create` already work today in `LeagueServiceTests.cs` via this exact mechanism, with no explicit package reference).

---

### Task 1: `ScrapeOutcome` enum + `ExecuteScrapeAsync` reports a real outcome (TDD)

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs` (add enum above the class; change `ExecuteScrapeAsync`'s signature and its 3 return points)
- Create: `DCF.Tests/Services/ScrapeTestHelpers.cs` (shared fakes + `ScrapeSchedulerService` construction helper, reused by Task 2, Task 3, and Task 4)
- Modify: `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs` (add outcome tests)

**Interfaces:**
- Produces: `public enum ScrapeOutcome { Succeeded, Failed, Skipped }` in `DCF.Api.Services`; `ScrapeSchedulerService.ExecuteScrapeAsync(ShowEntity show) : Task<(ScrapeOutcome Outcome, string? Error)>` (was `Task`); `ScrapeTestHelpers.CreateSvc(DcfDbContext db, IRecapScraperTask scraperTask, Dictionary<string, string?>? configValues = null, IEmailService? emailService = null) : ScrapeSchedulerService` in `DCF.Tests.Services`

This is a 3-state result rather than a plain bool because of a case already in the code today: the early-return guard (`freshShow is null || freshShow.IsExhibition || freshShow.Url is null`) means "this show can no longer be scraped," which is neither success nor a real failure — retrying it would loop against a guard that can never pass, and alerting on it would be a false "scrape failed" report for a show nobody expects to be scraped.

- [ ] **Step 1: Write the failing tests**

Create `DCF.Tests/Services/ScrapeTestHelpers.cs`:

```csharp
using DCF.Api.Scraping;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DCF.Tests.Services;

internal sealed class NullMqttService : IMqttService
{
    public Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class FakeRecapScraperTask(int failuresBeforeSuccess = int.MaxValue) : IRecapScraperTask
{
    public int CallCount { get; private set; }

    public Task<List<Result>> ScrapeAsync(Show show)
    {
        CallCount++;

        if (CallCount <= failuresBeforeSuccess)
        {
            throw new InvalidOperationException("Simulated scrape failure");
        }

        return Task.FromResult(new List<Result>());
    }
}

internal static class ScrapeTestHelpers
{
    public static ScrapeSchedulerService CreateSvc(
        DcfDbContext db,
        IRecapScraperTask scraperTask,
        Dictionary<string, string?>? configValues = null,
        IEmailService? emailService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(scraperTask);
        services.AddSingleton(emailService ?? new NullEmailService());

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? [])
            .Build();

        var emailOpts = Options.Create(new EmailOptions { UnsubscribeSecret = "test-secret", FrontendUrl = "http://test.local" });
        var tokenSvc = new EmailTokenService(emailOpts);

        return new ScrapeSchedulerService(
            scopeFactory,
            new NullMqttService(),
            config,
            emailOpts,
            tokenSvc,
            NullLogger<ScrapeSchedulerService>.Instance);
    }
}
```

(`NullEmailService` is already visible here without a `using` — it's declared at namespace scope in `LeagueServiceTests.cs`, and this file shares the same `namespace DCF.Tests.Services`.)

Replace the full contents of `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DCF.Tests.Services.ScrapeTestHelpers;

namespace DCF.Tests.Services;

public class ScrapeSchedulerServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static ShowEntity CreateShow(bool isExhibition = false, string? url = "https://example.test/recap")
    {
        return new ShowEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Show",
            Url = url,
            Date = new DateOnly(2026, 7, 4),
            ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            IsExhibition = isExhibition,
            SeasonId = Guid.NewGuid()
        };
    }

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

    [Fact]
    public async Task ExecuteScrapeAsync_SuccessfulScrape_ReturnsSucceededAndSetsStatus()
    {
        using var db = CreateDb("execute_scrape_success");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask(failuresBeforeSuccess: 0));
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(ScrapeStatus.Succeeded, db.Shows.Single(s => s.Id == show.Id).ScrapeStatus);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ScraperThrows_ReturnsFailedWithErrorAndSetsStatus()
    {
        using var db = CreateDb("execute_scrape_failure");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Failed, result.Outcome);
        Assert.Equal("Simulated scrape failure", result.Error);
        var updated = db.Shows.Single(s => s.Id == show.Id);
        Assert.Equal(ScrapeStatus.Failed, updated.ScrapeStatus);
        Assert.Equal("Simulated scrape failure", updated.ScrapeError);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowIsExhibition_ReturnsSkippedWithoutTouchingStatus()
    {
        using var db = CreateDb("execute_scrape_skipped_exhibition");
        var show = CreateShow(isExhibition: true);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
        Assert.Null(result.Error);
        Assert.Equal(ScrapeStatus.NotStarted, db.Shows.Single(s => s.Id == show.Id).ScrapeStatus);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowHasNoUrl_ReturnsSkipped()
    {
        using var db = CreateDb("execute_scrape_skipped_no_url");
        var show = CreateShow(url: null);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task ExecuteScrapeAsync_ShowDeleted_ReturnsSkipped()
    {
        using var db = CreateDb("execute_scrape_skipped_deleted");
        var show = CreateShow();

        var svc = CreateSvc(db, new FakeRecapScraperTask());
        var result = await svc.ExecuteScrapeAsync(show);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
    }
}
```

Note: `show` is never added to `db` in `ExecuteScrapeAsync_ShowDeleted_ReturnsSkipped` — `ExecuteScrapeAsync` re-fetches by ID internally, so an unsaved show is indistinguishable from a deleted one, which is exactly the case this test covers.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"
```

Expected: compile errors — `ExecuteScrapeAsync` returns `Task`, not something with an `.Outcome`/`.Error` member, and `ScrapeOutcome` doesn't exist yet. `ScrapeTestHelpers.cs` itself compiles cleanly on its own (it doesn't reference anything not-yet-existing); the failure is isolated to the new outcome-based assertions in the test file.

- [ ] **Step 3: Add the `ScrapeOutcome` enum and change `ExecuteScrapeAsync`'s outcome reporting**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, add the enum directly above the class declaration (mirrors how `AdminService.cs` declares its `SeasonSummary`/`ShowSummary` record types directly above its own class, in the same file):

```csharp
using System.Collections.Concurrent;
using DCF.Api.Scraping;
using DCF.Data;
using DCF.Data.Entities;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DCF.Api.Services;

public enum ScrapeOutcome { Succeeded, Failed, Skipped }

public class ScrapeSchedulerService(
```

Change the method signature (currently `public async Task ExecuteScrapeAsync(ShowEntity show)`):

```csharp
    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeAsync(ShowEntity show)
```

Change the early-return guard from:

```csharp
        if (freshShow is null || freshShow.IsExhibition || freshShow.Url is null)
        {
            logger.LogWarning("Show {ShowId} cannot be scraped", show.Id);

            return;
        }
```

to:

```csharp
        if (freshShow is null || freshShow.IsExhibition || freshShow.Url is null)
        {
            logger.LogWarning("Show {ShowId} cannot be scraped", show.Id);

            return (ScrapeOutcome.Skipped, null);
        }
```

Change the catch block from:

```csharp
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", freshShow.Id);

            freshShow.ScrapeStatus = ScrapeStatus.Failed;
            freshShow.ScrapeError = ex.Message;

            await db.SaveChangesAsync();

            return;
        }
```

to:

```csharp
        catch (Exception ex)
        {
            logger.LogError(ex, "Scrape failed for show {ShowId}", freshShow.Id);

            freshShow.ScrapeStatus = ScrapeStatus.Failed;
            freshShow.ScrapeError = ex.Message;

            await db.SaveChangesAsync();

            return (ScrapeOutcome.Failed, ex.Message);
        }
```

Change the end of the method from:

```csharp
        await SendScoresUpdatedNotificationsAsync(db, emailService, freshShow.SeasonId, freshShow.Name);
    }
```

to:

```csharp
        await SendScoresUpdatedNotificationsAsync(db, emailService, freshShow.SeasonId, freshShow.Name);

        return (ScrapeOutcome.Succeeded, null);
    }
```

Nothing else in `ExecuteScrapeAsync` changes — the DB writes, the scraper call, and the score-processing logic are untouched.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"
```

Expected: all 9 tests pass (4 existing `GetScrapeDelay` tests + 5 new `ExecuteScrapeAsync` tests).

- [ ] **Step 5: Build the whole solution to confirm nothing else broke**

```bash
dotnet build DCF.slnx
```

Expected: build succeeds. `ScheduleScrape`'s existing `await ExecuteScrapeAsync(show);` call (a bare await-statement) still compiles fine against the new tuple return type — C# allows discarding a return value in a statement expression. `AdminService.TriggerScrapeAsync`'s `await scrapeScheduler.ExecuteScrapeAsync(show);` call is unaffected for the same reason (it's changed for real in Task 4, not because this step requires it).

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeTestHelpers.cs DCF.Tests/Services/ScrapeSchedulerServiceTests.cs
git commit -m "feat: report a real ScrapeOutcome from ExecuteScrapeAsync"
```

---

### Task 2: Retry loop, new config values, and wiring (TDD)

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs` (new config fields; new `ExecuteScrapeWithRetriesAsync` method; rewire `ScheduleScrape`)
- Modify: `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs` (add retry tests)
- Modify: `CLAUDE.md` (document the two new config vars)

**Interfaces:**
- Consumes: `ScrapeOutcome`, `ExecuteScrapeAsync` (Task 1), `ScrapeTestHelpers.CreateSvc` (Task 1)
- Produces: `ScrapeSchedulerService.ExecuteScrapeWithRetriesAsync(ShowEntity show, CancellationToken token) : Task<(ScrapeOutcome Outcome, string? Error)>` — `public`, for the same testability reason `ExecuteScrapeAsync` itself is already `public` rather than being called only from inside a fire-and-forget `Task.Run`.

- [ ] **Step 1: Write the failing tests**

Add these three `[Fact]` methods to `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`, just before the final closing `}` of the `ScrapeSchedulerServiceTests` class:

```csharp
    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_FailsTwiceThenSucceeds_MakesThreeAttemptsAndSucceeds()
    {
        using var db = CreateDb("retry_recovers");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask(failuresBeforeSuccess: 2);
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "5"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, scraperTask.CallCount);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_AlwaysFails_MakesInitialAttemptPlusMaxRetriesAttempts()
    {
        using var db = CreateDb("retry_exhausts");
        var show = CreateShow();
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "3"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Failed, result.Outcome);
        Assert.Equal(4, scraperTask.CallCount);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_ShowSkipped_MakesOnlyOneAttempt()
    {
        using var db = CreateDb("retry_skipped");
        var show = CreateShow(isExhibition: true);
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scraperTask = new FakeRecapScraperTask();
        var svc = CreateSvc(db, scraperTask, new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "5"
        });

        var result = await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(ScrapeOutcome.Skipped, result.Outcome);
        Assert.Equal(0, scraperTask.CallCount);
    }
```

(`retry: 3` in the "always fails" test means 1 initial attempt + 3 retries = 4 total, verifying the "5 retries after the initial attempt" semantics with a smaller number so the test itself stays exercising real logic rather than hardcoding the default.)

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ExecuteScrapeWithRetriesAsync"
```

Expected: compile error — `ExecuteScrapeWithRetriesAsync` doesn't exist yet.

- [ ] **Step 3: Add the two config fields**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, change:

```csharp
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private readonly int _delayMinutes = config.GetValue<int>("Scraper:DelayMinutes", 5);
```

to:

```csharp
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _scheduled = new();
    private readonly int _delayMinutes = config.GetValue<int>("Scraper:DelayMinutes", 5);
    private readonly int _maxRetries = config.GetValue<int>("Scraper:MaxRetries", 5);
    private readonly int _retryIntervalMinutes = config.GetValue<int>("Scraper:RetryIntervalMinutes", 5);
```

- [ ] **Step 4: Add `ExecuteScrapeWithRetriesAsync` and rewire `ScheduleScrape`**

Change `ScheduleScrape`'s `Task.Run` body from:

```csharp
                await ExecuteScrapeAsync(show);

                await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
            }
            catch (OperationCanceledException)
```

to:

```csharp
                await ExecuteScrapeWithRetriesAsync(show, cts.Token);
            }
            catch (OperationCanceledException)
```

Then add the new method immediately after `ScheduleScrape`'s closing brace (right before `public static TimeSpan GetScrapeDelay(...)`):

```csharp
    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeWithRetriesAsync(ShowEntity show, CancellationToken token)
    {
        var result = await ExecuteScrapeAsync(show);

        var retry = 0;

        while (result.Outcome == ScrapeOutcome.Failed && retry < _maxRetries)
        {
            await Task.Delay(TimeSpan.FromMinutes(_retryIntervalMinutes), token);

            result = await ExecuteScrapeAsync(show);

            retry++;
        }

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });

        return result;
    }
```

(The admin-alert email call is intentionally not here yet — Task 3 adds it. This keeps this task's deliverable independently testable: retry counting and outcome propagation, without forward-referencing a method that doesn't exist yet.)

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"
```

Expected: all 12 tests pass (9 from Task 1 + 3 new retry tests).

- [ ] **Step 6: Document the new config values**

In `CLAUDE.md`, under `## Configuration` → `**API**`, change:

```markdown
- `Scraper__DelayMinutes` — buffer after `ScoresAnnouncedTime` before scraping
```

to:

```markdown
- `Scraper__DelayMinutes` — buffer after `ScoresAnnouncedTime` before scraping
- `Scraper__MaxRetries` — number of retries after a scrape failure before giving up and alerting admins (default 5)
- `Scraper__RetryIntervalMinutes` — delay between retries (default 5)
```

- [ ] **Step 7: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeSchedulerServiceTests.cs CLAUDE.md
git commit -m "feat: retry failed scrapes with a bounded backoff before giving up"
```

---

### Task 3: Admin failure alert email (TDD)

**Files:**
- Modify: `DCF.Api/Services/EmailTemplate.cs` (add `ScrapeFailed`)
- Modify: `DCF.Tests/Services/EmailTemplateTests.cs` (add test)
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs` (add `SendScrapeFailedAlertAsync`; wire into `ExecuteScrapeWithRetriesAsync`)
- Modify: `DCF.Tests/Services/ScrapeTestHelpers.cs` (add `RecordingEmailService` fake)
- Modify: `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs` (add alert tests)

**Interfaces:**
- Consumes: `ExecuteScrapeWithRetriesAsync` (Task 2), `EmailTemplate.Layout`, `IEmailService`, `EmailTokenService.GenerateToken`, `ScrapeTestHelpers.CreateSvc` (Task 1)
- Produces: `EmailTemplate.ScrapeFailed(string showName, string errorMessage, Guid seasonId, string frontendUrl, string unsubscribeToken) : (string subject, string html)`; `RecordingEmailService : IEmailService` in `DCF.Tests.Services` (reusable by any future test needing to assert on sent emails)

- [ ] **Step 1: Write the failing template test**

Add to `DCF.Tests/Services/EmailTemplateTests.cs`, add this field alongside the existing `TestLeagueId`/`FrontendUrl`/`Token` constants:

```csharp
    private static readonly Guid TestSeasonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
```

Then add this test, alongside the other `[Fact]` methods:

```csharp
    [Fact]
    public void ScrapeFailed_SubjectAndHtmlContainShowNameAndError()
    {
        var (subject, html) = EmailTemplate.ScrapeFailed(
            "Drum Corps West", "HTTP request failed", TestSeasonId, FrontendUrl, Token);

        Assert.Equal("Scrape failed — Drum Corps West", subject);
        Assert.Contains("Drum Corps West", html);
        Assert.Contains("HTTP request failed", html);
        Assert.Contains($"/admin/seasons/{TestSeasonId}", html);
        Assert.Contains($"/unsubscribe?token={Token}", html);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeFailed_SubjectAndHtmlContainShowNameAndError"
```

Expected: compile error — `EmailTemplate.ScrapeFailed` doesn't exist yet.

- [ ] **Step 3: Implement `EmailTemplate.ScrapeFailed`**

In `DCF.Api/Services/EmailTemplate.cs`, add this method immediately after `ScoresAvailable` and before the private `Layout` method:

```csharp
    public static (string subject, string html) ScrapeFailed(
        string showName,
        string errorMessage,
        Guid seasonId,
        string frontendUrl,
        string unsubscribeToken)
    {
        var safeName = WebUtility.HtmlEncode(showName);
        var safeError = WebUtility.HtmlEncode(errorMessage);

        return (
            $"Scrape failed — {showName}",
            Layout(
                heading: "Scrape failed",
                body: $"Scraping scores for <strong style=\"color: #f3f4f6;\">{safeName}</strong> failed after multiple attempts: {safeError}. A manual re-trigger may be needed.",
                ctaText: "View Show",
                ctaUrl: $"{frontendUrl}/admin/seasons/{seasonId}",
                unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTemplateTests"
```

Expected: all 8 tests pass (7 existing + 1 new).

- [ ] **Step 5: Write the failing alert-sending tests**

Add this fake to `DCF.Tests/Services/ScrapeTestHelpers.cs`, alongside `NullMqttService`/`FakeRecapScraperTask`:

```csharp
internal sealed class RecordingEmailService : IEmailService
{
    public List<string> SentToEmails { get; } = [];

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        SentToEmails.Add(toEmail);

        return Task.CompletedTask;
    }
}
```

Add these four `[Fact]` methods to the `ScrapeSchedulerServiceTests` class in `DCF.Tests/Services/ScrapeSchedulerServiceTests.cs`, just before the final closing `}`:

```csharp
    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_ExhaustsRetries_EmailsAdminsWithNotificationsEnabled()
    {
        using var db = CreateDb("alert_sent_to_admin");
        var show = CreateShow();
        db.Shows.Add(show);
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "admin1", Email = "admin@example.com",
            DisplayName = "Admin", IsAdmin = true, EmailNotificationsEnabled = true
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var svc = CreateSvc(db, new FakeRecapScraperTask(), new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "1"
        }, emailService);

        await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Equal(["admin@example.com"], emailService.SentToEmails);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_ExhaustsRetries_DoesNotEmailAdminsWithNotificationsDisabled()
    {
        using var db = CreateDb("alert_skips_opted_out_admin");
        var show = CreateShow();
        db.Shows.Add(show);
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "admin1", Email = "admin@example.com",
            DisplayName = "Admin", IsAdmin = true, EmailNotificationsEnabled = false
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var svc = CreateSvc(db, new FakeRecapScraperTask(), new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "1"
        }, emailService);

        await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Empty(emailService.SentToEmails);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_ExhaustsRetries_DoesNotEmailNonAdmins()
    {
        using var db = CreateDb("alert_skips_non_admin");
        var show = CreateShow();
        db.Shows.Add(show);
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "user1", Email = "user@example.com",
            DisplayName = "User", IsAdmin = false, EmailNotificationsEnabled = true
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var svc = CreateSvc(db, new FakeRecapScraperTask(), new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "1"
        }, emailService);

        await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Empty(emailService.SentToEmails);
    }

    [Fact]
    public async Task ExecuteScrapeWithRetriesAsync_EventuallySucceeds_DoesNotSendAlertEmail()
    {
        using var db = CreateDb("alert_not_sent_on_recovery");
        var show = CreateShow();
        db.Shows.Add(show);
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(), Auth0Sub = "admin1", Email = "admin@example.com",
            DisplayName = "Admin", IsAdmin = true, EmailNotificationsEnabled = true
        });
        await db.SaveChangesAsync();

        var emailService = new RecordingEmailService();
        var svc = CreateSvc(db, new FakeRecapScraperTask(failuresBeforeSuccess: 1), new Dictionary<string, string?>
        {
            ["Scraper:RetryIntervalMinutes"] = "0",
            ["Scraper:MaxRetries"] = "5"
        }, emailService);

        await svc.ExecuteScrapeWithRetriesAsync(show, CancellationToken.None);

        Assert.Empty(emailService.SentToEmails);
    }
```

- [ ] **Step 6: Run the tests to verify the meaningful one fails**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"
```

Expected: this compiles fine (Task 2 already added `ExecuteScrapeWithRetriesAsync`), so all tests run rather than fail to build. `ExecuteScrapeWithRetriesAsync_ExhaustsRetries_EmailsAdminsWithNotificationsEnabled` FAILS — `Assert.Equal(["admin@example.com"], emailService.SentToEmails)` mismatches because nothing sends an email yet. The other three new tests (`DoesNotEmailAdminsWithNotificationsDisabled`, `DoesNotEmailNonAdmins`, `EventuallySucceeds_DoesNotSendAlertEmail`) all assert an *absence* of emails, which is trivially true before this feature exists — they pass now and will keep passing after Step 7, at which point they start actually protecting against a regression instead of passing vacuously.

- [ ] **Step 7: Implement `SendScrapeFailedAlertAsync` and wire it in**

In `DCF.Api/Services/ScrapeSchedulerService.cs`, add this method immediately after `SendScoresUpdatedNotificationsAsync`'s closing brace:

```csharp
    private async Task SendScrapeFailedAlertAsync(ShowEntity show, string? error)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var admins = await db.Users
                .Where(u => u.IsAdmin && u.EmailNotificationsEnabled)
                .ToListAsync();

            foreach (var admin in admins)
            {
                var token = emailTokenService.GenerateToken(admin.Id);
                var (subject, html) = EmailTemplate.ScrapeFailed(show.Name, error ?? "Unknown error", show.SeasonId, emailOptions.Value.FrontendUrl, token);

                await emailService.SendAsync(admin.Email, admin.DisplayName, subject, html);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send scrape-failed alert for show {ShowId}", show.Id);
        }
    }
```

Then change `ExecuteScrapeWithRetriesAsync` (added in Task 2) from:

```csharp
        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });

        return result;
    }
```

to:

```csharp
        if (result.Outcome == ScrapeOutcome.Failed)
        {
            await SendScrapeFailedAlertAsync(show, result.Error);
        }

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });

        return result;
    }
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~ScrapeSchedulerServiceTests"
```

Expected: all 16 tests pass (12 from Tasks 1–2 + 4 new alert tests).

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Services/EmailTemplate.cs DCF.Tests/Services/EmailTemplateTests.cs DCF.Api/Services/ScrapeSchedulerService.cs DCF.Tests/Services/ScrapeTestHelpers.cs DCF.Tests/Services/ScrapeSchedulerServiceTests.cs
git commit -m "feat: email admins when a show exhausts all scrape retries"
```

---

### Task 4: Manual trigger endpoint surfaces the real outcome

**Files:**
- Modify: `DCF.Api/Services/IAdminService.cs:27`
- Modify: `DCF.Api/Services/AdminService.cs:274-288` (`TriggerScrapeAsync`)
- Modify: `DCF.Api/Controllers/AdminController.cs:329-338` (`TriggerScrape`)
- Modify: `DCF.Tests/Services/AdminServiceTests.cs` (add tests, reusing `ScrapeTestHelpers.CreateSvc` from Task 1)

**Interfaces:**
- Consumes: `ScrapeOutcome`, `ExecuteScrapeAsync` (Task 1), `ScrapeTestHelpers.CreateSvc` and its fakes (Task 1)
- Produces: `IAdminService.TriggerScrapeAsync(Guid showId) : Task<(bool Found, ScrapeOutcome Outcome, string? Error)>` (was `Task<bool>`); `POST /api/admin/shows/{id}/scrape` now responds `200 { outcome, error }` instead of always `204`

This task has no dedicated HTTP-level test — `WebApplicationFactory`-style controller integration tests are already an established out-of-scope decision for this codebase (see `docs/superpowers/specs/2026-06-24-testing-and-ci-design.md`). `AdminService.TriggerScrapeAsync` itself (not the HTTP layer) is a plain method callable directly from a unit test, so that's what gets covered.

- [ ] **Step 1: Write the failing tests**

Add this new `using` statement to the top of `DCF.Tests/Services/AdminServiceTests.cs` (alongside the existing ones):

```csharp
using DCF.Api.Scraping;
using static DCF.Tests.Services.ScrapeTestHelpers;
```

(No other new usings are needed — `CreateSvc` from `ScrapeTestHelpers` already builds a fully-wired `ScrapeSchedulerService` internally, so `AdminServiceTests.cs` doesn't need `Microsoft.Extensions.DependencyInjection`/`Configuration`/`Options`/`Logging.Abstractions` itself. `NullMqttService`, `FakeRecapScraperTask`, and `NullEmailService` are already visible without a `using` — they're declared at namespace scope in `ScrapeTestHelpers.cs`/`LeagueServiceTests.cs`, and this file shares the same `namespace DCF.Tests.Services`.)

Add these three `[Fact]` methods to the `AdminServiceTests` class, just before the final closing `}`:

```csharp
    [Fact]
    public async Task TriggerScrapeAsync_MissingShow_ReturnsFoundFalse()
    {
        using var db = CreateDb("trigger_scrape_missing");
        var scrapeScheduler = CreateSvc(db, new FakeRecapScraperTask());
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(Guid.NewGuid());

        Assert.False(found);
        Assert.Equal(ScrapeOutcome.Skipped, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task TriggerScrapeAsync_SuccessfulScrape_ReturnsSucceededOutcome()
    {
        using var db = CreateDb("trigger_scrape_success");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scrapeScheduler = CreateSvc(db, new FakeRecapScraperTask(failuresBeforeSuccess: 0));
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(show.Id);

        Assert.True(found);
        Assert.Equal(ScrapeOutcome.Succeeded, outcome);
        Assert.Null(error);
    }

    [Fact]
    public async Task TriggerScrapeAsync_FailedScrape_ReturnsFailedOutcomeWithError()
    {
        using var db = CreateDb("trigger_scrape_failure");
        var show = new ShowEntity
        {
            Id = Guid.NewGuid(), Name = "Test Show", Url = "https://example.test/recap",
            Date = new DateOnly(2026, 7, 4), ScoresAnnouncedTime = DateTimeOffset.UtcNow,
            SeasonId = Guid.NewGuid()
        };
        db.Shows.Add(show);
        await db.SaveChangesAsync();

        var scrapeScheduler = CreateSvc(db, new FakeRecapScraperTask());
        var svc = new AdminService(db, scrapeScheduler, new NullMqttService(), new NoOpSeasonStatus(), null!);

        var (found, outcome, error) = await svc.TriggerScrapeAsync(show.Id);

        Assert.True(found);
        Assert.Equal(ScrapeOutcome.Failed, outcome);
        Assert.Equal("Simulated scrape failure", error);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~AdminServiceTests"
```

Expected: compile error — `TriggerScrapeAsync` still returns `Task<bool>`, not a 3-tuple.

- [ ] **Step 3: Update `IAdminService`**

In `DCF.Api/Services/IAdminService.cs`, change line 27 from:

```csharp
    Task<bool> TriggerScrapeAsync(Guid showId);
```

to:

```csharp
    Task<(bool Found, ScrapeOutcome Outcome, string? Error)> TriggerScrapeAsync(Guid showId);
```

- [ ] **Step 4: Update `AdminService.TriggerScrapeAsync`**

In `DCF.Api/Services/AdminService.cs`, change:

```csharp
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
```

to:

```csharp
    public async Task<(bool Found, ScrapeOutcome Outcome, string? Error)> TriggerScrapeAsync(Guid showId)
    {
        var show = await db.Shows.Include(s => s.ShowCorps).FirstOrDefaultAsync(s => s.Id == showId);

        if (show is null)
        {
            return (false, ScrapeOutcome.Skipped, null);
        }

        var result = await scrapeScheduler.ExecuteScrapeAsync(show);

        await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = showId });

        return (true, result.Outcome, result.Error);
    }
```

- [ ] **Step 5: Update `AdminController.TriggerScrape`**

In `DCF.Api/Controllers/AdminController.cs`, change:

```csharp
    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        return await adminService.TriggerScrapeAsync(id) ? NoContent() : NotFound();
    }
```

to:

```csharp
    [HttpPost("shows/{id}/scrape")]
    public async Task<IActionResult> TriggerScrape(Guid id)
    {
        if (!await adminService.IsAdminAsync(GetSub()))
        {
            return Forbid();
        }

        var (found, outcome, error) = await adminService.TriggerScrapeAsync(id);

        return found ? Ok(new { outcome, error }) : NotFound();
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~AdminServiceTests"
```

Expected: all existing `AdminServiceTests` tests still pass, plus the 3 new `TriggerScrapeAsync` tests (all pass).

- [ ] **Step 7: Run the full test suite and build**

```bash
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: build succeeds; every test in the solution passes (this is the final task, so this is the full regression check for the whole feature).

- [ ] **Step 8: Commit**

```bash
git add DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: surface real scrape outcome from the manual trigger endpoint"
```
