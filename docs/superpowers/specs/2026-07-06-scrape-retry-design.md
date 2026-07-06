# Scrape Retry & Failure Alerting — Design Spec

**Date:** 2026-07-06
**Branch:** feat/scrape-retry

## Overview

Today, `ScrapeSchedulerService.ExecuteScrapeAsync` makes exactly one scrape attempt per show. On failure it logs the error, marks `ShowEntity.ScrapeStatus = Failed` with the exception message, and stops — nothing retries it. Worse, the startup reconciliation in `ExecuteAsync` only re-schedules shows whose `ScoresAnnouncedTime` is still in the future, so a failed show is never picked up again automatically, even across an API restart/redeploy. The only recovery path today is a human noticing the `Failed` status in the admin UI and clicking "Trigger Score Scrape" to force a fresh attempt.

In practice, scrape failures are often transient and timing-related rather than structural: DCI sometimes uploads a recap later than `ScoresAnnouncedTime` would suggest, and show schedules can shift due to weather. This design adds a bounded retry sequence to the automatic (scheduled) scrape path, and an email alert to admins if all retries are exhausted, so timing-related failures self-heal without a human needing to notice and intervene — and if they don't self-heal, the right people find out immediately instead of whenever someone happens to check the admin page.

The manual "Trigger Score Scrape" button (`AdminController.TriggerScrape`) is explicitly **not** wrapped in retries — it stays a single immediate attempt, since an admin clicking it is already deliberately forcing a fresh try right now. A separate, deferred effort (bundled with upcoming show-editing changes) will update that button's frontend to show progress and consume the richer response this design adds to its endpoint.

---

## Architecture

`ScrapeSchedulerService.ExecuteScrapeAsync` changes from `Task` to `Task<(ScrapeOutcome Outcome, string? Error)>`, where:

```csharp
public enum ScrapeOutcome { Succeeded, Failed, Skipped }
```

This is a 3-state result rather than a plain bool because of a case already in the code today: `ExecuteScrapeAsync`'s existing early-return guard (`freshShow is null || freshShow.IsExhibition || freshShow.Url is null` — the show was deleted, flipped to exhibition, or lost its URL after being scheduled) currently just logs a warning and returns, touching no `ScrapeStatus`. That's neither a success nor a real failure — it's "this show can no longer be scraped, and retrying won't change that." A plain bool would force treating it as one or the other: as a failure, it would retry 5 times against a guard that will never pass and then send a false "scrape failed" alert for a show nobody expects to be scraped; as a success, it would silently mask that nothing happened. `Skipped` lets the retry loop stop immediately without retrying *or* alerting — the same no-op behavior this guard already has today, just now expressible.

A new private method, `ExecuteScrapeWithRetriesAsync`, wraps the retry sequence and is called from `ScheduleScrape`'s existing `Task.Run` in place of today's single `await ExecuteScrapeAsync(show)` call:

```csharp
private async Task ExecuteScrapeWithRetriesAsync(ShowEntity show, CancellationToken token)
{
    var result = await ExecuteScrapeAsync(show);

    var retry = 0;

    while (result.Outcome == ScrapeOutcome.Failed && retry < _maxRetries)
    {
        await Task.Delay(TimeSpan.FromMinutes(_retryIntervalMinutes), token);

        result = await ExecuteScrapeAsync(show);

        retry++;
    }

    if (result.Outcome == ScrapeOutcome.Failed)
    {
        await SendScrapeFailedAlertAsync(show, result.Error);
    }

    await mqtt.PublishAsync("dcf/scores/updated", new { ShowId = show.Id });
}
```

`Skipped` (like `Succeeded`) exits the loop immediately without retrying or alerting — only `Failed` does either.

Two new config values, read the same way `Scraper:DelayMinutes` already is:

```csharp
private readonly int _maxRetries = config.GetValue<int>("Scraper:MaxRetries", 5);
private readonly int _retryIntervalMinutes = config.GetValue<int>("Scraper:RetryIntervalMinutes", 5);
```

**Retry count confirmed: 5 retries *after* the initial attempt — 6 attempts total.** `_maxRetries` counts retries only; the initial attempt (already scheduled via `Scraper:DelayMinutes`) happens once before the loop even starts. With the defaults (`DelayMinutes = 5`, `MaxRetries = 5`, `RetryIntervalMinutes = 5`), the schedule from `ScoresAnnouncedTime` (T) is:

| Attempt | Time |
|---|---|
| 1 (initial) | T+5 |
| 2 (retry 1) | T+10 |
| 3 (retry 2) | T+15 |
| 4 (retry 3) | T+20 |
| 5 (retry 4) | T+25 |
| 6 (retry 5) | T+30 |

The final attempt lands ~30 minutes after scores are announced — confirmed against the numbers directly.

This reuses the `CancellationTokenSource` already created per-show in `ScheduleScrape`'s `_scheduled` dictionary — the same token that guards the initial pre-scrape delay now also guards the inter-retry delays, so rescheduling or cancelling a show (e.g. an admin edits it, or the API shuts down) interrupts a pending retry exactly like it already interrupts a pending first attempt today. No new cancellation plumbing.

**Approaches considered:** Building the retry loop into `ExecuteScrapeAsync` itself was rejected — that method is also called directly by the manual-trigger path, so retry logic there would silently make manual clicks take up to ~20 minutes, contradicting the single-attempt decision above. A separate periodic sweeper that rescans for `Failed` shows on a timer (surviving an API restart mid-retry) was also considered, but it requires a persisted attempt counter and a new background-service concern to cover a rare edge case (a redeploy landing in the exact 20-minute window a show is retrying) that the existing manual-trigger button already covers well enough. Retry state is therefore in-memory only, scoped to the lifetime of the one scheduled `Task.Run` — lost on API restart, falling back to the manual button. This matches this codebase's existing bias toward minimal scope over durability infra that wasn't explicitly requested (see the persistent-login spec's "Out of Scope" section).

**`ScrapeStatus` gets no new value.** Between retries the show will show `Failed` with that attempt's error — accurate at that moment — and `LastScrapeAttemptAt` advancing each try is enough signal that it's actively being retried. An explicit "Retrying" status or in-progress UI indicator is the same territory as the button-progress work already deferred to the show-editing effort, so it's out of scope here.

**MQTT publish timing is preserved as-is, behaviorally:** today it fires once, unconditionally, after the single scrape attempt; this design fires it once, unconditionally, after the whole retry sequence resolves. Suppressing it on failure would be a separate, unrequested behavior change.

---

## Manual Trigger Endpoint

Since `ExecuteScrapeAsync` now reports a real outcome, `AdminService.TriggerScrapeAsync` and `AdminController.TriggerScrape` are updated to surface it, rather than discarding it:

```csharp
// IAdminService.cs / AdminService.cs
Task<(bool Found, ScrapeOutcome Outcome, string? Error)> TriggerScrapeAsync(Guid showId);

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

```csharp
// AdminController.cs
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

The response shape changes from always-`204` to `200 { outcome, error }` (or `404` if the show doesn't exist). `outcome` serializes as a string (`"Succeeded"`/`"Failed"`/`"Skipped"`) for free — `Program.cs` already registers a global `JsonStringEnumConverter`, the same one every other enum in this API (`Caption`, `DraftStatus`, etc.) already goes through, so no extra serialization work is needed here. This is backend-only and non-breaking: the frontend's `adminTriggerScrape` is declared `request<void>`, and `request<T>`'s implementation (`client.ts`) only special-cases status `204`; any other `2xx` gets `res.json()`'d, and the result is presently discarded by `SeasonDetail.tsx`'s `.then(() => {...})` handler regardless of shape. Consuming this new response (e.g. correcting the currently-inaccurate "✓ Scrape triggered successfully" message, which today fires on every request regardless of actual scrape outcome) is left to the deferred show-editing/button-progress effort.

---

## Admin Failure Alert

If all retry attempts are exhausted, `ExecuteScrapeWithRetriesAsync` calls a new method that emails every admin:

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

This mirrors the existing `SendScoresUpdatedNotificationsAsync` structure exactly: its own DI scope (rather than holding one open across the whole retry sequence), and a catch-and-log-warning wrapper so an email failure can never surface as a scrape failure or interrupt already-persisted state.

`EmailTemplate.ScrapeFailed(showName, errorMessage, seasonId, frontendUrl, unsubscribeToken)` is added following the exact shape of the existing `ScoresAvailable` template — HTML-encoded show name and error message in the body, CTA linking to `/admin/seasons/{seasonId}` (the real admin route for managing that show, confirmed in `main.tsx`), and the same unsubscribe footer every other template has.

**Gating:** the alert respects each admin's own `EmailNotificationsEnabled` preference — the same flag used for score-update emails — rather than sending unconditionally. This was a deliberate choice to keep exactly one on/off switch for all email notifications rather than carving out an operational-alert exception, and it's also what makes reusing the standard `Layout`'s unsubscribe footer make sense (an alert that ignored the preference but still said "unsubscribe from email notifications" would be self-contradictory).

---

## Error Handling

- **Exception during a single scrape attempt:** unchanged — caught inside `ExecuteScrapeAsync`, recorded as `ScrapeStatus.Failed` + `ScrapeError`, reported as `ScrapeOutcome.Failed` to the retry loop.
- **Show can no longer be scraped** (deleted, flipped to exhibition, or lost its URL between scheduling and execution): unchanged from today — logged as a warning, no `ScrapeStatus` write — now reported as `ScrapeOutcome.Skipped`, which stops the sequence immediately with no retries and no admin alert.
- **All retries exhausted:** show remains in `ScrapeStatus.Failed` (from the last attempt), admins are emailed, MQTT is published once.
- **Alert email fails to send** (SMTP down, etc.): caught and logged as a warning; does not affect the already-persisted `ScrapeStatus.Failed`, does not throw back into the scheduler's outer catch.
- **API restarts mid-retry-sequence:** the in-memory loop is gone. The show sits at whatever `ScrapeStatus` its most recent attempt left it in, recoverable via the manual trigger button (which now reports its real outcome). Accepted trade-off, matching this codebase's existing minimal-durability precedent.

## Out of Scope

- **Frontend consumption of the new endpoint response**, and the "Trigger Scrape" button's loading/disabled/label-text UX — deferred to the upcoming show-editing effort.
- **An explicit "Retrying" `ScrapeStatus` or other in-progress UI signal** — same reasoning as above.
- **Retry-state durability across an API restart** (e.g. a persisted attempt counter, or a periodic sweeper for stuck `Failed` shows) — the manual-trigger button is the accepted fallback for this rare edge case; revisit only if it becomes a real recurring problem.
- **Any change to `RecapScraperTask`'s parsing logic.** Retries only help transient failures (timing, network); a structural HTML-parsing mismatch will fail identically on every attempt, and isn't addressed by this design.

## Testing

Following this codebase's existing convention of hand-rolled fakes rather than a mocking framework (confirmed: no Moq/NSubstitute in `DCF.Tests.csproj`; see `LeagueServiceTests.cs`'s `NullEmailService : IEmailService`):

- A fake `IRecapScraperTask` that throws for a configurable number of calls before succeeding (or always throws), to drive `ScrapeSchedulerService` through both the "recovers after N failures" and "exhausts all retries" paths.
- A show entity constructed as exhibition (or with a null `Url`) to cover the `Skipped` path, asserting zero retries and zero emails sent.
- `Scraper:RetryIntervalMinutes` configured to `0` in tests so the retry loop doesn't actually wait between attempts.
- A recording fake `IEmailService` to assert the alert is sent exactly once when retries are exhausted, and never when an attempt eventually succeeds or is skipped.
- Assertions cover: number of scrape attempts made, final `ScrapeStatus`/`ScrapeError`, and `AdminService.TriggerScrapeAsync`'s returned tuple across all three `ScrapeOutcome` values plus the not-found case.
