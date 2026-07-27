# Backend Logging & Monitoring Hardening — Design Spec

**Date:** 2026-07-25
**Branch:** feat/sentry-monitoring-hardening

## Overview

Commit `4f1a261` wired `Sentry.AspNetCore` 6.7.0 into `DCF.Api` as a proof of concept: `Program.cs` calls `builder.WebHost.UseSentry(...)` with a hardcoded DSN, `Debug = true` unconditionally, `TracesSampleRate = 1` (100%), and three throwaway calls (`SentrySdk.CaptureMessage("Hello Sentry")`, `SentrySdk.Logger.LogInfo("sample info")`, `SentrySdk.Logger.LogError("Sample error Log message")`) that would otherwise fire on every single app boot, in every environment, forever.

This closes out GitHub issue #16 ("IF-1: Third-party logging and error monitoring integration"), which chose Sentry over Datadog/Grafana specifically for "what broke and why," explicitly deferred structured log aggregation to a later Grafana/OpenTelemetry layer if ever needed, and called for DSN-via-config, a low production trace sample rate, and enrichment with user/league context. This design covers the backend half of that issue (the frontend `@sentry/react` half is separate, out of scope here).

Confirmed via Sentry's own docs during design: `Sentry.AspNetCore` auto-captures unhandled exceptions with zero extra code, its `Microsoft.Extensions.Logging` integration automatically turns any `ILogger` `LogError`+ call into a Sentry Issue (with `Information`+ calls attached as breadcrumbs) with no `SentrySdk` calls needed anywhere in application code, `EnableLogs = true` additionally forwards `ILogger` calls to Sentry's separate structured Logs product, `SendDefaultPii = true` auto-populates `SentryUser` from `HttpContext.User`'s `NameIdentifier` claim, and `ILogger.BeginScope` state is attached to breadcrumbs/events/logs the same way exception and claim data is — it is not merely a local/console-only feature.

Four existing `LogError` call sites already exist in the codebase (`ScrapeSchedulerService` ×2, `DraftSchedulerService`, `SeasonStatusService`) — all in background scheduler services, all following the same shape (a `Task.Run` processes one entity, catches broadly, logs the error with that entity's id). These will start generating Sentry Issues automatically as soon as the config below lands; no code changes are needed for that part.

---

## Architecture

### 1. Sentry configuration (`Program.cs`, `appsettings.json`)

Current:

```csharp
builder.WebHost.UseSentry(o =>
{
    o.Dsn = "https://8dfe572a3fc3b524cc9b149e02fea937@o4511798022045696.ingest.us.sentry.io/4511798029189120";
    o.Debug = true; //Once we're working, set based on environment
    o.TracesSampleRate = 1; //May need to adjust for prod
    o.EnableLogs = true;
});
```

New:

```csharp
builder.WebHost.UseSentry(o =>
{
    o.Dsn = builder.Configuration["Sentry:Dsn"];
    o.Debug = builder.Environment.IsDevelopment();
    o.TracesSampleRate = builder.Environment.IsDevelopment() ? 1.0 : 0.1;
    o.EnableLogs = true;
    o.SendDefaultPii = true;
});
```

`appsettings.json` gets a new section, following the same committed-placeholder pattern as `Auth0:Domain`/`Auth0:Audience` — a Sentry DSN is not a secret (frontend Sentry SDKs ship theirs in public JS bundles), so unlike `DB_PASSWORD`/`RESEND_API_KEY` it does not need `.env.prod`/GitHub-secrets treatment:

```json
"Sentry": {
  "Dsn": "https://8dfe572a3fc3b524cc9b149e02fea937@o4511798022045696.ingest.us.sentry.io/4511798029189120"
}
```

The three test calls (`CaptureMessage`, `Logger.LogInfo`, `Logger.LogError`) are deleted from `Program.cs` entirely — confirmed redundant, since any real `LogError` call anywhere in the app already becomes a Sentry Issue automatically once this is wired (see Overview). Environment tagging (`development`/`production` shown in the Sentry UI) is automatic from `IHostEnvironment` — no explicit `o.Environment` assignment needed.

### 2. Global exception handling (new `DCF.Api/ApiExceptionHandler.cs`)

Nothing in `Program.cs` currently handles unhandled exceptions — they fall through to ASP.NET Core's bare default behavior. This adds the .NET 8+ idiomatic `IExceptionHandler` pattern, matching this codebase's existing one-class-per-concern style under `DCF.Api/Services/`:

```csharp
public class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred."
            }
        });
    }
}
```

Registered in `Program.cs`:

```csharp
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
    };
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
// ...
app.UseExceptionHandler();
```

`CustomizeProblemDetails` runs for every ProblemDetails response the framework produces (not just this handler's 500s), so a `traceId` is attached consistently, including on plain framework-generated 404s — a correlation id support/dev can match against a specific Sentry event without ever seeing the raw exception, which never leaves the server. Existing deliberate `try/catch` blocks elsewhere (e.g. `MqttService`'s reconnect loop) are untouched; this only catches what nothing else already handles.

**Approach considered and rejected:** an inline `app.UseExceptionHandler(errorApp => errorApp.Run(...))` lambda was considered — fewer moving parts, but not unit-testable and mixes error-shaping logic into `Program.cs`'s composition root. A third-party library (`Hellang.Middleware.ProblemDetails`) was also considered for its exception-type-to-status-code mapping, but this app has no real exception-type taxonomy to map, making it a dependency for marginal benefit over the built-in `IExceptionHandler` + `AddProblemDetails()`.

### 3. Reliable user identity (new `DCF.Api/Auth0SentryUserFactory.cs`)

Every controller in this codebase (`GetSub()` in `LeaguesController`, `DraftController`, `AdminController`, and inline in `AuthController`/`NotificationsController`) defensively falls back from `ClaimTypes.NameIdentifier` to a raw `"sub"` claim before trusting either. `DevAuthHandler` and `RememberMeAuthHandler` both explicitly set `ClaimTypes.NameIdentifier`, so they're covered by Sentry's default `SendDefaultPii` extraction — but the codebase-wide fallback pattern, repeated in six-plus places, is a strong signal that the production `Auth0Jwt` scheme's claim shape isn't fully trusted to populate `NameIdentifier` alone. A custom `ISentryUserFactory` mirrors the exact same fallback instead of relying on Sentry's default (`NameIdentifier`-only) extraction:

```csharp
public class Auth0SentryUserFactory : ISentryUserFactory
{
    public SentryUser? Create(HttpContext context)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        return sub is null ? null : new SentryUser { Id = sub };
    }
}
```

Registered via `builder.Services.AddSingleton<ISentryUserFactory, Auth0SentryUserFactory>();`. **To confirm during implementation:** whether registering a custom factory replaces `SendDefaultPii`'s IP-address capture too, or whether that's independent — if independent, nothing changes; if not, `IpAddress = context.Connection.RemoteIpAddress?.ToString()` gets added to the returned `SentryUser` to preserve it.

### 4. League-id tagging (new `DCF.Api/SentryLeagueTaggingFilter.cs`)

A league id doesn't semantically belong on `SentryUser` — one user belongs to many leagues, so it's contextual to the request, not an identity attribute. It's added as a Sentry **tag** instead, since tags are what Sentry's issue search/filtering actually indexes (e.g. "show every issue for league X"); data on the user object isn't first-class searchable the same way.

Concrete wrinkle confirmed in the actual routes: `DraftController` scopes its whole route under `[Route("api/leagues/{leagueId}/draft")]`, but `LeaguesController` uses `{id}` (`[HttpGet("{id}")]`, `[HttpPatch("{id}")]`, etc.) for the same concept — so resolution has to check both, not assume one canonical name. This runs as an `IActionFilter` (not raw middleware) specifically so it fires after MVC has fully resolved route/action binding, sidestepping any ambiguity about where a custom middleware would sit relative to the minimal-hosting-model's implicit routing middleware:

```csharp
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

    public void OnActionExecuted(ActionExecutedContext context) { }

    internal static Guid? ResolveLeagueId(RouteValueDictionary routeValues, PathString path)
    {
        var raw = routeValues.TryGetValue("leagueId", out var v1) ? v1
            : path.StartsWithSegments("/api/leagues") && routeValues.TryGetValue("id", out var v2) ? v2
            : null;

        return raw is string s && Guid.TryParse(s, out var id) ? id : null;
    }
}
```

Registered via `builder.Services.AddControllers(options => options.Filters.Add<SentryLeagueTaggingFilter>())`. `ResolveLeagueId` is `internal static` specifically so it's unit-testable directly without spinning up MVC's action-filter pipeline.

### 5. Background-operation context (`ScrapeSchedulerService`, `DraftSchedulerService`, `SeasonStatusService`)

All three schedulers share the same shape: a `Task.Run` processes one entity (a show/league/season) whose id is already known, wrapped in a broad `catch (Exception ex)` that logs it. Each gets its per-entity work wrapped in `ILogger.BeginScope`, confirmed (see Overview) to attach scope data to whatever Sentry Issue/breadcrumb/log the wrapped code produces — not just local console output — and this works identically whether the call originates from the scheduled background path or a manual admin-triggered endpoint, since scope is ambient to the current async flow, not tied to any one call site.

`ScrapeSchedulerService.ExecuteScrapeAsync` is the single point both the scheduler (`ExecuteScrapeWithRetriesAsync`) and the manual `AdminController.TriggerScrape` → `AdminService.TriggerScrapeAsync` path converge on, so wrapping it there covers both:

```csharp
public async Task<(ScrapeOutcome Outcome, string? Error)> ExecuteScrapeAsync(ShowEntity show)
{
    using var _ = logger.BeginScope(new Dictionary<string, object> { ["ShowId"] = show.Id });

    // existing body, unchanged
}
```

`DraftSchedulerService` and `SeasonStatusService` get the equivalent treatment around the body of their own scheduled `Task.Run` work, tagging `["LeagueId"] = leagueId` and `["SeasonId"] = season.Id` respectively.

---

## Error Handling

- **Unhandled exceptions:** now caught by `ApiExceptionHandler`, logged (→ Sentry Issue via the `ILogger` integration), and returned to the client as a generic ProblemDetails 500 with a `traceId` — the exception message/stack trace never leaves the server.
- **Existing deliberate `try/catch` blocks** (MQTT reconnect, email-send failures, scrape-failed alert emails, etc.): unchanged. This design adds visibility on top of them (via `BeginScope` context and the `LogError` calls they already make); it does not change their control flow.
- **Missing/unresolvable claims in `Auth0SentryUserFactory`:** returns `null` rather than throwing — an anonymous request (e.g. the public DCI endpoints, `/api/notifications/unsubscribe`) simply reports no Sentry user, exactly like today.
- **No route match in `SentryLeagueTaggingFilter`:** `ResolveLeagueId` returns `null`, no tag is set, request proceeds exactly as it does today.

## Out of Scope

- **`@sentry/react` frontend integration** — issue #16's other half; separate codebase area, separate follow-up.
- **Sentry alert-rule configuration** (email/Slack on new issues) — dashboard-side setup, not code.
- **Structured log aggregation beyond Sentry's own Logs product** (e.g. Grafana Cloud, OpenTelemetry) — issue #16 already explicitly deferred this.
- **`ShowInfoScraperTask`'s prefill scraper** — different shape (runs before a `ShowEntity` exists, so there's no `ShowId` yet to tag), and lower priority than the recurring recap-scrape path this design covers.
- **Any change to `RecapScraperTask`'s HTML-parsing logic** — unrelated to observability.

## Testing

Following this codebase's existing convention of hand-rolled fakes rather than a mocking framework (no Moq/NSubstitute in `DCF.Tests.csproj`):

- **`ApiExceptionHandler`:** a hand-rolled fake `IProblemDetailsService` recording what it was asked to write, `NullLogger<ApiExceptionHandler>.Instance`, and a `DefaultHttpContext` — assert the response status is 500 and the recorded `ProblemDetails.Title`/`Status` match, without needing EF InMemory (nothing here touches the database).
- **`Auth0SentryUserFactory`:** a `DefaultHttpContext` with a `ClaimsPrincipal` carrying either `ClaimTypes.NameIdentifier`, a raw `"sub"` claim, or neither — assert `Create()` returns the expected id or `null`.
- **`SentryLeagueTaggingFilter.ResolveLeagueId`:** called directly with `RouteValueDictionary`/`PathString` combinations covering `leagueId` present, `id` present under `/api/leagues`, `id` present but *not* under `/api/leagues` (must return `null`), and neither present.
- **`BeginScope` additions to the three scheduler services:** no control-flow change, so `ScrapeSchedulerServiceTests.cs`, `DraftSchedulerServiceTests.cs`, and `SeasonStatusServiceTests.cs` (all three already exist) are expected to keep passing unchanged — confirmed these test files already exist, so this is a regression check, not new coverage to write.
- **Actual Sentry delivery** (an event really lands in the Sentry dashboard) isn't unit-testable — no network calls happen in the test suite. Confirmed manually against the dashboard once implemented, the same way the original proof-of-concept in `4f1a261` was verified.

## Documentation

Add `Sentry__Dsn` to `CLAUDE.md`'s Configuration section, alongside the existing `Auth0__Domain`/`Email__*` entries — that section documents every other value in double-underscore environment-variable form, not the `Sentry:Dsn` colon form the C# code above reads via `builder.Configuration[...]`. Both forms address the same underlying config value (ASP.NET Core treats a double-underscore env var and a colon-nested config path as equivalent); the doc entry should match the env-var convention every other line in that section already uses.
