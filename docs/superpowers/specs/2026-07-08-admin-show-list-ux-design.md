# Admin show-list UX (Spec 3 of 3)

## Context

This is the third and last of three related specs covering admin show-management improvements:

1. **Show schedule data completeness** — TBD/unscheduled corps handling + Event Concludes time reuse. Shipped in PR #63.
2. **Show no-score reason + exhibition completion** — admin-driven way to mark a show as not receiving scores, and a "Completed" badge for exhibition shows. Shipped in PR #64.
3. **Admin show-list UX** (this spec) — three independent gaps in the admin show list, grouped together because each is individually small and they share the same file (`SeasonDetail.tsx`). Depends on specs 1 and 2, both now merged.

## Problem

Three separate gaps, found by re-reading the current admin show list against what specs 1 and 2 left deliberately out of scope:

1. **Trigger-scrape lies about its own outcome.** `TriggerScrapeAsync` already awaits the real scrape and returns `{ outcome, error }`, but the frontend's `.then()` discards the response body and always shows "✓ Scrape triggered successfully" — even when `outcome` is `Failed`. It also never refetches `shows` afterward, so the card's SCRAPE COMPLETED/FAILED badge doesn't update without a page reload.
2. **No way to re-fetch DCI data for a show that already exists.** The Add Show form has "Fetch from DCI"; the edit form doesn't. This was spec 1's original motivation for TBD-corps handling — DCI often doesn't finalize times until close to the show — but there's still no way to pull updated times into an existing show once they're published.
3. **No search or filter on the shows list.** Just a flat, date-ordered list, regardless of how many shows a season has.

## Approach

All three are frontend-only — no backend or API changes anywhere in this spec. Everything needed already exists: `TriggerScrapeAsync` already returns outcome/error, `PrefillShowAsync` already works for any show name, and `UpdateShowRequest`/`UpdateShowAsync` already accept and persist `Location`/`Latitude`/`Longitude`/`Schedule` — `SeasonDetail.tsx`'s edit form just never used that part of the contract.

Investigating gap 2 surfaced that it's bigger than "add a button": the edit form has no schedule or location fields at all today (`saveShowEdit` silently echoes the original show's `location`/`schedule` back unchanged). Delivering the actual motivating use case — backfilling TBD schedule times — means adding those as real editable fields, not just wiring a fetch call to state nobody can see or save. Confirmed this scope increase explicitly before designing it.

## Design

### 1. Trigger-scrape result UX

New per-show `triggeringScrapeId` state (mirrors the existing `deletingShowId` pattern) disables the button and shows "Scraping…" while the request is in flight.

On response, branch on `{ outcome, error }` instead of ignoring it:
- `Succeeded` → green "✓ Scrape succeeded", auto-decays after 3s (same as today's generic message).
- `Failed` → red "✗ Scrape failed: `<error>`", does **not** auto-decay — a failure is exactly the thing an admin shouldn't lose by glancing away.
- `Skipped` → amber "Scrape skipped" (edge case: e.g. a competitive show somehow has no URL set).

After any outcome, refetch `shows` via the existing `api.adminGetShows` call (same pattern every other mutation in this file already uses) so the card's SCRAPE COMPLETED/FAILED badge reflects the real result immediately.

### 2. Edit-form "Fetch from DCI" + editable schedule/location

**`editShow` state** gains three fields: `location: string`, `latitude: number | null`, `longitude: number | null`, `schedule: { time: string | null; label: string; corpsId: string | null }[]` — the same shape `ShowPrefillScheduleEntry` (and `ShowScheduleEntry`) already use.

**`expandShow` seeds them** from the show being expanded: `location`/`latitude`/`longitude` directly; `schedule` entries converted from their stored ISO timestamps to "HH:MM" local time via the same `toHHMM` conversion `expandShow` already applies to `startTime`/`scoresTime`.

**New UI**, inside the existing locked edit-form branch (`!started && !hasScoresAnnounced(s)`) — this is deliberately **not** given the always-available treatment spec 2's no-score-reason control got, since it's part of the same core-identity editing surface as name/date/corps, not a retroactive annotation:
- A "Fetch from DCI" button next to the Name field (same row layout as the Add form).
- Location + Lat + Lng inputs (same 3-input row as the Add form).
- A schedule preview list (same TBD-aware rendering as the Add form's) — shows the current schedule by default (seeded on expand), updates in place if the admin fetches.

**Fetch handler** (new `editFetchFromDci`, with its own `editPrefetching`/`editPrefetched`/`editPrefetchError` state, kept separate from the Add form's equivalents since both could theoretically be open at once) calls the same `api.adminPrefillShow(seasonId, editShow.name)` the Add form already uses, and mirrors its exact merge semantics: `date`/`tz`/`startTime`/`scoresTime`/`corpsIds` only update if the fetch actually returned a value (so an incomplete DCI response can't blank out something already correct); `schedule`/`location`/`latitude`/`longitude` always overwrite. `editPrefetched` resets whenever `expandShow` opens a (possibly different) show, so re-fetching is blocked only within one expand session, not permanently — matching the Add form's own single-fetch-per-session behavior.

**`saveShowEdit`** currently sends `location`/`latitude`/`longitude`/`schedule` by echoing the *original* `show` object; it now sends `editShow`'s values instead. Converting `editShow.schedule`'s HH:MM entries back to ISO datetimes needs the same day-rollover-aware logic `addShow` already has inline (a schedule can cross midnight). Rather than duplicate that loop, it's extracted into a shared `buildSchedulePayload(entries, baseDate, tz)` helper in `SeasonDetail.helpers.ts`, used by both `addShow` and `saveShowEdit`.

### 3. Search + filter

A text search input matches against `show.name` (case-insensitive substring), placed in a new toolbar row between the "Add Show" section and the shows list.

A filter dropdown offers curated triage buckets via a new pure `getShowFilterBucket(show): 'upcoming' | 'needsAttention' | 'done'` helper (same extraction pattern as spec 2's `getShowStatusBadge`, unit tested the same way):

```typescript
function getShowFilterBucket(show: Show): 'upcoming' | 'needsAttention' | 'done' {
  if (!show.isExhibition && show.scrapeStatus === 'Failed' && !show.noScoreReason) {
    return 'needsAttention';
  }
  if (show.noScoreReason || show.scrapeStatus === 'Succeeded' || (show.isExhibition && hasScoresAnnounced(show))) {
    return 'done';
  }
  return 'upcoming';
}
```

- **Needs Attention** — competitive, scrape failed, no reason set yet. A show with a no-score reason is explicitly excluded: setting the reason *is* the resolution, not a pending task.
- **Done** — a reason is set, or the scrape succeeded, or it's a concluded exhibition.
- **Upcoming** — everything else. Broader than strictly "hasn't started": it also covers a show that's started but hasn't announced scores yet, or announced scores but hasn't been scraped yet. There's no clean fourth "in progress" bucket in a 3-option dropdown, and none of those states need admin action or represent a resolved show, so they land here as "still pending." The label reads a little loosely for an already-started show, but keeping the three buckets as scoped rather than introducing a fourth.
- **All** — no filter.

Search and the status filter combine with AND. Filtering happens client-side over the already-loaded `shows` array — a season has at most a few dozen shows, not enough to justify a server-side query.

## Testing

Entirely frontend; no backend test changes.

- `SeasonDetail.helpers.test.ts`: new `buildSchedulePayload` tests (rollover across midnight, non-rollover case, null/TBD entries passed through) and new `getShowFilterBucket` tests (all three buckets, plus the boundary case of a scrape-failed show that also has a reason set — must resolve to `done`, not `needsAttention`).
- `SeasonDetail.tsx` changes (trigger-scrape result branching, edit-form fetch/seed/save wiring, search/filter rendering) have no dedicated test — consistent with the existing "no full component-test effort for `SeasonDetail`" boundary from spec 1's approved spec. Verified manually.

## Out of scope

- No backend or API-client changes — every endpoint this spec needs already exists and already accepts the fields involved.
- No manual, by-hand schedule editing (adding/removing/reordering individual entries) — schedule stays populate-via-fetch-then-review-then-save, same model the Add form already uses.
- No server-side search/filter (query params, pagination) — revisit only if a season's show count grows enough for client-side filtering to matter.
- No renaming the "Upcoming" bucket or adding a fourth bucket — flagged as slightly imprecise, kept as scoped.
- No change to the existing edit-form lock semantics (`!started && !hasScoresAnnounced`) — the new Fetch-from-DCI button and schedule/location fields live inside that same lock, unlike spec 2's always-available no-score-reason control.
