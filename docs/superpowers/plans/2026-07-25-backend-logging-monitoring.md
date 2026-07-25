# Backend Logging & Monitoring Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **This plan is being executed via live pair programming in the current session**, not dispatched to a fresh subagent or run through executing-plans — the user is writing the code, and each task is reviewed together as it's finished. The checkboxes are a shared progress tracker for that session, not a dispatch queue.

**Goal:** Finish hardening the Sentry proof-of-concept in `DCF.Api` into production-ready error monitoring: config-driven setup, global exception handling, reliable user identity, league-id tagging, and background-operation context.

**Architecture:** Five additions to `DCF.Api`, each registered from `Program.cs`: environment-aware Sentry options, a global `IExceptionHandler`, a custom `ISentryUserFactory`, an `IActionFilter` that tags league id, and `ILogger.BeginScope` wrapping in the three background scheduler services.

**Tech Stack:** ASP.NET Core 10 (net10.0), Sentry.AspNetCore 6.7.0, xUnit (hand-rolled fakes, no mocking framework).

## Global Constraints

- Target framework: net10.0 (existing `DCF.Api.csproj`) — no new NuGet packages needed anywhere in this plan.
- Verified by loading the installed 6.7.0 assemblies directly (not docs, which vary by SDK version): `ISentryUserFactory.Create()` takes **no parameters** — get the current request via injected `IHttpContextAccessor`, never a method parameter.
- `IExceptionHandler` lives in `Microsoft.AspNetCore.Diagnostics` (confirmed by assembly search — not `.Abstractions`, where it isn't found).
- C# style (from CLAUDE.md, applies to every snippet below): curly braces always start on a new line; no expression-bodied/lambda method or property bodies; every `if`/`foreach`/`using`/`try` block is braced even for one line; one blank line before `return`; one blank line before and after braced blocks and `await` statements; never more than one blank line in a row.
- File-scoped namespaces (`namespace X;`) — confirmed exclusive convention across every existing file in this codebase.
- Primary constructors for DI — confirmed exclusive convention (`ScrapeSchedulerService`, `DraftSchedulerService`, `DevAuthHandler`, every controller).
- New classes go in `DCF.Api/Services/` with `namespace DCF.Api.Services;` — matching where `ApiExceptionHandler.cs` was placed this session.
- Tests go in `DCF.Tests/Services/` regardless of source file location (confirmed convention — e.g. `NotificationsControllerTests.cs` tests a controller but lives there): xUnit `[Fact]`/`Assert.*`, `NullLogger<T>.Instance` for logger fakes, naming `MethodName_Scenario_ExpectedBehavior`, hand-rolled fakes implementing real interfaces rather than a mocking framework.

---

## Already Done This Session

- **Sentry config** (`Program.cs`, `appsettings.json`): DSN from `Sentry:Dsn` config, `Debug`/`TracesSampleRate` environment-aware, `SendDefaultPii = true`, the three throwaway test calls removed. Matches the spec.
- **`DCF.Api/Services/ApiExceptionHandler.cs`**: created and registered via `AddProblemDetails(...)` + `AddExceptionHandler<ApiExceptionHandler>()`. Three fixes identified in review and being applied directly (not tracked as plan tasks since they're in-flight): add the missing `app.UseExceptionHandler()` call (registering the handler in DI isn't enough — without this the middleware never actually runs), switch the file to file-scoped namespace syntax to match the rest of the codebase, and fix "occured" → "occurred" in the ProblemDetails title.

Task 1 below assumes those three fixes are in place and adds the tests this class doesn't have yet.

---

### Task 1: Tests for `ApiExceptionHandler`

**Files:**
- Test: `DCF.Tests/Services/ApiExceptionHandlerTests.cs` (new)

**Interfaces:**
- Consumes: `ApiExceptionHandler(ILogger<ApiExceptionHandler>, IProblemDetailsService)`, `TryHandleAsync(HttpContext, Exception, CancellationToken) : ValueTask<bool>` (already implemented)
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Write the tests**

```csharp
using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Services;

public class ApiExceptionHandlerTests
{
    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;

            return ValueTask.FromResult(true);
        }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;

            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task TryHandleAsync_SetsInternalServerErrorStatusAndGenericTitle()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance, problemDetailsService);
        var httpContext = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(httpContext, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, problemDetailsService.Written?.ProblemDetails.Status);
        Assert.Equal("An unexpected error occurred.", problemDetailsService.Written?.ProblemDetails.Title);
    }

    [Fact]
    public async Task TryHandleAsync_DoesNotLeakExceptionMessageIntoProblemDetails()
    {
        var problemDetailsService = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance, problemDetailsService);
        var httpContext = new DefaultHttpContext();

        await handler.TryHandleAsync(httpContext, new InvalidOperationException("sensitive internal detail"), CancellationToken.None);

        var title = problemDetailsService.Written?.ProblemDetails.Title ?? string.Empty;
        var detail = problemDetailsService.Written?.ProblemDetails.Detail ?? string.Empty;

        Assert.DoesNotContain("sensitive internal detail", title);
        Assert.DoesNotContain("sensitive internal detail", detail);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter "FullyQualifiedName~ApiExceptionHandlerTests"`
Expected: PASS (the implementation already exists — this backfills coverage rather than driving new code, since the class was written before this plan existed to TDD it)

- [ ] **Step 3: Commit**

```bash
git add DCF.Tests/Services/ApiExceptionHandlerTests.cs
git commit -m "test: add coverage for ApiExceptionHandler"
```

---

### Task 2: Reliable user identity — `Auth0SentryUserFactory`

**Files:**
- Create: `DCF.Api/Services/Auth0SentryUserFactory.cs`
- Modify: `DCF.Api/Program.cs` (register `IHttpContextAccessor` + the factory)
- Test: `DCF.Tests/Services/Auth0SentryUserFactoryTests.cs` (new)

**Interfaces:**
- Consumes: `IHttpContextAccessor.HttpContext` (nullable)
- Produces: `Auth0SentryUserFactory : ISentryUserFactory` with `SentryUser? Create()` — registered as the DI-resolved `ISentryUserFactory`, consumed internally by the Sentry SDK (nothing in this codebase calls it directly)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Claims;
using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DCF.Tests.Services;

public class Auth0SentryUserFactoryTests
{
    private sealed class FakeHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static HttpContext ContextWithClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");

        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void Create_NameIdentifierClaimPresent_ReturnsUserWithThatId()
    {
        var context = ContextWithClaims(new Claim(ClaimTypes.NameIdentifier, "auth0|123"));
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Equal("auth0|123", user?.Id);
    }

    [Fact]
    public void Create_OnlyRawSubClaimPresent_FallsBackToSubClaim()
    {
        var context = ContextWithClaims(new Claim("sub", "auth0|456"));
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Equal("auth0|456", user?.Id);
    }

    [Fact]
    public void Create_NoIdentifyingClaim_ReturnsNull()
    {
        var context = ContextWithClaims();
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Null(user);
    }

    [Fact]
    public void Create_NoHttpContext_ReturnsNull()
    {
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(null));

        var user = factory.Create();

        Assert.Null(user);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~Auth0SentryUserFactoryTests"`
Expected: FAIL to compile — `Auth0SentryUserFactory` doesn't exist yet

- [ ] **Step 3: Write the implementation**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sentry;

namespace DCF.Api.Services;

public class Auth0SentryUserFactory(IHttpContextAccessor httpContextAccessor) : ISentryUserFactory
{
    public SentryUser? Create()
    {
        var user = httpContextAccessor.HttpContext?.User;
        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");

        if (sub is null)
        {
            return null;
        }

        return new SentryUser { Id = sub };
    }
}
```

- [ ] **Step 4: Register it in `Program.cs`**

Add immediately after the `UseSentry(...)` block:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISentryUserFactory, Auth0SentryUserFactory>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~Auth0SentryUserFactoryTests"`
Expected: PASS

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build DCF.slnx`
Expected: 0 errors (confirms the `Program.cs` registration compiles against the real DI container)

- [ ] **Step 7: Commit**

```bash
git add DCF.Api/Services/Auth0SentryUserFactory.cs DCF.Api/Program.cs DCF.Tests/Services/Auth0SentryUserFactoryTests.cs
git commit -m "feat: add custom ISentryUserFactory matching the app's NameIdentifier/sub claim fallback"
```

---

### Task 3: League-id tagging — `SentryLeagueTaggingFilter`

**Files:**
- Create: `DCF.Api/Services/SentryLeagueTaggingFilter.cs`
- Modify: `DCF.Api/Program.cs` (register the filter on `AddControllers`)
- Test: `DCF.Tests/Services/SentryLeagueTaggingFilterTests.cs` (new)

**Interfaces:**
- Consumes: `SentrySdk.ConfigureScope(Action<Scope>)`, `Scope.SetTag(string, string)` (both confirmed via assembly reflection)
- Produces: `SentryLeagueTaggingFilter : IActionFilter`; `internal static Guid? ResolveLeagueId(RouteValueDictionary routeValues, PathString path)` — the testable pure function other tasks don't depend on

- [ ] **Step 1: Write the failing tests**

```csharp
using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace DCF.Tests.Services;

public class SentryLeagueTaggingFilterTests
{
    [Fact]
    public void ResolveLeagueId_LeagueIdRouteValue_ReturnsIt()
    {
        var leagueId = Guid.NewGuid();
        var routeValues = new RouteValueDictionary { ["leagueId"] = leagueId.ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/leagues/{leagueId}/draft/pick");

        Assert.Equal(leagueId, result);
    }

    [Fact]
    public void ResolveLeagueId_IdRouteValueUnderApiLeagues_ReturnsIt()
    {
        var leagueId = Guid.NewGuid();
        var routeValues = new RouteValueDictionary { ["id"] = leagueId.ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/leagues/{leagueId}");

        Assert.Equal(leagueId, result);
    }

    [Fact]
    public void ResolveLeagueId_IdRouteValueOutsideApiLeagues_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { ["id"] = Guid.NewGuid().ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/admin/shows/{Guid.NewGuid()}");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveLeagueId_NoMatchingRouteValue_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary();

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, "/api/leagues/public");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveLeagueId_MalformedGuid_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { ["leagueId"] = "not-a-guid" };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, "/api/leagues/not-a-guid/draft/pick");

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SentryLeagueTaggingFilterTests"`
Expected: FAIL to compile — `SentryLeagueTaggingFilter` doesn't exist yet

- [ ] **Step 3: Write the implementation**

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Sentry;

namespace DCF.Api.Services;

public class SentryLeagueTaggingFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var leagueId = ResolveLeagueId(context.RouteData.Values, context.HttpContext.Request.Path);

        if (leagueId is not null)
        {
            SentrySdk.ConfigureScope(scope => scope.SetTag("league_id", leagueId.Value.ToString()));
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    internal static Guid? ResolveLeagueId(RouteValueDictionary routeValues, PathString path)
    {
        object? raw = null;

        if (routeValues.TryGetValue("leagueId", out var leagueIdValue))
        {
            raw = leagueIdValue;
        }
        else if (path.StartsWithSegments("/api/leagues") && routeValues.TryGetValue("id", out var idValue))
        {
            raw = idValue;
        }

        if (raw is string s && Guid.TryParse(s, out var id))
        {
            return id;
        }

        return null;
    }
}
```

- [ ] **Step 4: Register it in `Program.cs`**

Change the existing `AddControllers()` call:

```csharp
builder.Services.AddControllers(options => options.Filters.Add<SentryLeagueTaggingFilter>())
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SentryLeagueTaggingFilterTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/SentryLeagueTaggingFilter.cs DCF.Api/Program.cs DCF.Tests/Services/SentryLeagueTaggingFilterTests.cs
git commit -m "feat: tag Sentry events with league_id for league-scoped requests"
```

---

### Task 4: Background-operation context — `BeginScope` in the three scheduler services

**Files:**
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs:131-134`
- Modify: `DCF.Api/Services/DraftSchedulerService.cs:47-50`
- Modify: `DCF.Api/Services/SeasonStatusService.cs:75-78`

**Interfaces:**
- Consumes: `ILogger.BeginScope(object)` (standard `Microsoft.Extensions.Logging` API, already in use elsewhere in these files) — confirmed via Sentry's own docs that scope state attaches to breadcrumbs/events/logs, not just local console output
- Produces: nothing new consumed elsewhere — purely additive context, no behavior change

- [ ] **Step 1: `ScrapeSchedulerService.ExecuteScrapeAsync`**

Current (lines 131-134):

```csharp
    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeAsync(ShowEntity show)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
```

New:

```csharp
    public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeAsync(ShowEntity show)
    {
        using var _ = logger.BeginScope(new Dictionary<string, object> { ["ShowId"] = show.Id });
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
```

- [ ] **Step 2: `DraftSchedulerService.ScheduleNext`**

Current (lines 47-50):

```csharp
        _ = Task.Run(async () =>
        {
            try
            {
```

New:

```csharp
        _ = Task.Run(async () =>
        {
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["LeagueId"] = leagueId });

            try
            {
```

- [ ] **Step 3: `SeasonStatusService.ScheduleSeason`**

Current (lines 75-78):

```csharp
        _ = Task.Run(async () =>
        {
            try
            {
```

New:

```csharp
        _ = Task.Run(async () =>
        {
            using var _ = logger.BeginScope(new Dictionary<string, object> { ["SeasonId"] = season.Id });

            try
            {
```

- [ ] **Step 4: Run the existing tests for all three services to confirm no regression**

Run: `dotnet test --filter "FullyQualifiedName~ScrapeSchedulerServiceTests|FullyQualifiedName~DraftSchedulerServiceTests|FullyQualifiedName~SeasonStatusServiceTests"`
Expected: PASS — these tests already exist and cover control flow; `BeginScope` doesn't change return values or exceptions, so this is a pure regression check, not new coverage.

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Services/ScrapeSchedulerService.cs DCF.Api/Services/DraftSchedulerService.cs DCF.Api/Services/SeasonStatusService.cs
git commit -m "feat: tag background scheduler work with its entity id via BeginScope"
```

---

### Task 5: Documentation

**Files:**
- Modify: `CLAUDE.md` (Configuration section)

- [ ] **Step 1: Add the new config entry**

In the `**API** (\`appsettings.json\` / environment variables):` list, add:

```markdown
- `Sentry__Dsn` — Sentry ingest DSN; not a secret (same trust model as a public frontend key), so no `.env.prod` wiring needed
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: document Sentry__Dsn in the Configuration section"
```

---

## Final Verification

- [ ] `dotnet build DCF.slnx` — 0 errors
- [ ] `dotnet test DCF.Tests/DCF.Tests.csproj` — all tests pass, including the 3 new test files and the 3 pre-existing scheduler test files
- [ ] Manually trigger a real exception locally (e.g. temporarily throw inside a controller action) and confirm in Sentry's dashboard: the issue has a `league_id` tag (if hit through a league route), a user id attached, and the API response is a generic ProblemDetails 500 with a `traceId` — not the raw exception
