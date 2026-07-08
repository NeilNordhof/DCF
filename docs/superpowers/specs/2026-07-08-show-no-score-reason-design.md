# Show no-score reason + exhibition completion (Spec 2 of 3)

## Context

This is the second of three related specs covering admin show-management improvements:

1. **Show schedule data completeness** — TBD/unscheduled corps handling + Event Concludes time reuse. Shipped in PR #63.
2. **Show no-score reason + exhibition completion** (this spec) — admin-driven way to mark a competitive show as not receiving scores, and a "Completed" badge for exhibition shows once they conclude. Depends on #1.
3. **Admin show-list UX** — Trigger-scrape button loading/result state, an edit-show "Fetch from DCI" lookup button, and search/filter for the shows list. Depends on #1 and #2.

## Problem

Some shows that are scheduled as competitive don't end up receiving scores — a corps show gets converted last-minute to a standstill exhibition due to weather, or (less often) gets cancelled outright. Today there's no way to record this: the show sits forever as a competitive show whose scrape will keep being scheduled/retried against a URL that will never produce scores, and admins have no way to signal "this one's not getting scored, here's why."

Separately, exhibition shows (planned as such from creation) have no completion signal at all once they start. Competitive shows get "SCORES ANNOUNCED"/"SCRAPE COMPLETED" indicators; exhibition shows get nothing, even after spec 1 gave them a "Concludes" time.

Both gaps were scoped together in spec 1's decomposition because they're both about a show's terminal state, but they turned out to be independent of each other in the design below — no shared mechanism, just shared theme.

## Approach

For the no-score case, `IsExhibition` is deliberately left untouched — it stays "planned as non-scored at creation, immutable after." A storm-converted show is not folded into that flag; it stays a competitive show with a new nullable `NoScoreReason` explaining the deviation. Keeping "planned show type" and "outcome deviation" as separate concepts was an explicit choice over reusing `IsExhibition`'s semantics: an exhibition-show count shouldn't silently include storm-cancelled competitive shows.

Setting/clearing the reason is a new dedicated endpoint, not a field on the existing show edit form. The existing edit form (name/date/times/corps/etc.) locks itself once a show has started or announced scores, but the real use case here is retroactively marking shows that already happened — so this needs to be always available, independent of that lock.

For the exhibition "Completed" badge, the state is computed on read (`IsExhibition && ScoresAnnouncedTime <= now`) rather than stored and scheduled. `SeasonEntity.Status` + `SeasonStatusService` is the existing precedent for a persisted, background-scheduled lifecycle status, but that machinery earns its keep there because other code branches on season status. Nothing outside the admin show list reads show status today, so a computed value — the same pattern `hasStarted`/`hasScoresAnnounced` already use client-side — is sufficient. This also reuses `hasScoresAnnounced` unchanged for the completion check, since spec 1 already unified `ScoresAnnouncedTime` to mean "concludes" for exhibition shows.

## Design

### Data model

`ShowEntity.NoScoreReason`: new `string?`. One EF Core migration (`dotnet ef migrations add AddShowNoScoreReason`). No change to `IsExhibition` or `ScrapeStatus`.

### Backend

**New endpoint:** `PATCH /api/admin/shows/{id}/no-score-reason`, body `{ reason: string | null }` → `AdminService.SetNoScoreReasonAsync(id, reason)`. No `ScrapeStatus`/lock check — always available regardless of show state. Whitespace-only input normalizes to `null` (clearing the reason). Returns `NoContent()`/`NotFound()`, following the existing endpoint conventions in `AdminController`.

`CreateShowRequest`/`UpdateShowRequest` are unchanged — `NoScoreReason` is only reachable through the new endpoint, so there's exactly one path to it and that path is never subject to the edit lock.

**Scraper interaction** (`DCF.Api/Services/ScrapeSchedulerService.cs`):
- The cancel logic currently inlined in `ScheduleScrape` (remove from `_scheduled`, `Cancel()`, `Dispose()`) is extracted into a public `CancelScheduledScrape(Guid showId)` method so `AdminService` can call it directly.
- `SetNoScoreReasonAsync`, when setting a non-null reason, calls `scrapeScheduler?.CancelScheduledScrape(show.Id)` — null-conditional, matching the existing convention (`scrapeScheduler?.ScheduleScrape(show)` in `CreateShowAsync`/`UpdateShowAsync`) so tests that don't exercise scraping can keep passing `null!` for that dependency.
- `SetNoScoreReasonAsync`, when clearing the reason, calls `scrapeScheduler?.ScheduleScrape(show)` — the same call `CreateShowAsync`/`UpdateShowAsync` already make. If `ScoresAnnouncedTime` is already in the past, `ScheduleScrape`'s existing delay math (`GetScrapeDelay`) naturally triggers an immediate scrape attempt rather than waiting — correct behavior if an admin clears a reason because scores turned out to be available after all.
- `ScheduleScrape`'s guard clause (`IsExhibition || Url is null || ScoresAnnouncedTime is null`) gains `|| NoScoreReason != null`.
- The startup reconciliation query in `ExecuteAsync` (which re-schedules unscraped shows on boot) gains the same `NoScoreReason == null` condition.
- `ExecuteScrapeAsync` (the manual-trigger path used by `TriggerScrapeAsync`) is **not** blocked by `NoScoreReason`. Automatic scheduling and an admin's explicit manual trigger are different intents; the frontend hides the trigger button by default (see below) but the API stays capable of an explicit override.
- `ScrapeStatus` (`NotStarted`/`Succeeded`/`Failed`) is untouched — no new value added. It continues to mean strictly "what happened on the last scrape attempt." `NoScoreReason` answers a different question ("why aren't we expecting scores") and the two are read independently by the frontend.

**Read side:** `ShowSummary` record and the `GetShowsAsync` projection gain `NoScoreReason`.

### Frontend

`DCF.Web/src/types/api.ts`: `Show.noScoreReason: string | null` (matching the `time: string | null` convention from spec 1, not an optional `?:` field). `DCF.Web/src/api/client.ts`: new `adminSetNoScoreReason(showId, reason)` calling the PATCH endpoint.

`DCF.Web/src/pages/SeasonDetail.tsx`:

- A new always-rendered control inside the expanded show card, gated only on `!s.isExhibition`, seeded (like `editShow`) when `expandShow` runs — **not** nested inside the existing `started`/locked conditional branches, since availability regardless of lock state is the point:
  - No reason set: a text input (placeholder e.g. "Reason, e.g. rained out") + "Mark No Scores" button.
  - Reason set: the reason text displayed + a "Clear" button.
- Collapsed card header badge, precedence order (first match wins):
  1. `s.noScoreReason` set → **"NO SCORES"** badge, `--red`, same tier as today's "SCRAPE FAILED"; reason text as a `title` tooltip.
  2. `s.isExhibition && hasScoresAnnounced(s)` → **"COMPLETED"** badge, `--green`. Reuses `hasScoresAnnounced` unchanged.
  3. Otherwise, today's existing STARTED / SCORES ANNOUNCED / SCRAPE COMPLETED / SCRAPE FAILED logic, unchanged.
- "Trigger Score Scrape" button gains a second gate — hidden when `s.noScoreReason` is set, on top of the existing `!s.isExhibition` gate — so the UI doesn't show a "try to scrape" action next to an explicit "no scores" marker.

**Refactor while in there:** the badge-precedence logic above is extracted as a pure function (e.g. `getShowStatusBadge(show): { label: string; color: string } | null`) into `SeasonDetail.helpers.ts`, unit tested in `SeasonDetail.helpers.test.ts` — the same kind of extraction spec 1 did post-review for the schedule-time helpers, done upfront this time instead.

## Testing

**Backend:**
- `AdminServiceTests.cs`: `SetNoScoreReasonAsync` — sets reason, clears reason, not-found, whitespace-only reason normalizes to `null`; using the existing real-`scrapeScheduler` test pattern (already used for scrape-trigger tests) to verify `CancelScheduledScrape`/`ScheduleScrape` are called appropriately.
- `ScrapeSchedulerServiceTests.cs`: `ScheduleScrape` skips shows with `NoScoreReason` set; `CancelScheduledScrape` cancels a pending scheduled task.

**Frontend:**
- `SeasonDetail.helpers.test.ts`: new `getShowStatusBadge` covering the full precedence order (no-score reason, exhibition-completed, started, scores-announced, scrape-succeeded, scrape-failed, none).

## Out of scope (deferred to spec 3, or not planned)

- Search bar, status filter dropdown, trigger-scrape loading UI, edit-form "Fetch from DCI" button — all spec 3.
- No preset/enum reason list — free text only, per the user's framing ("some reason or another").
- No public-facing (non-admin) surface — nothing outside `SeasonDetail.tsx` reads Show data today.
- No change to `IsExhibition` mutability, no new `ScrapeStatus` value.
- No stored/scheduled show-status field — the "Completed" badge is computed on read; revisit if a future feature needs to query show status server-side rather than just display it.
