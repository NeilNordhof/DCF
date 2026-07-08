# Show schedule data completeness (Spec 1 of 3)

## Context

This is the first of three related specs covering admin show-management improvements:

1. **Show schedule data completeness** (this spec) — stop the scraper from silently losing data: unscheduled ("TBD") corps, and exhibition shows' completion time.
2. **Show status & lifecycle controls** (next) — admin-driven Cancelled / Exhibition-with-reason status, and a "Completed" badge for exhibition shows once they conclude. Depends on this spec.
3. **Admin show-list UX** (last) — trigger-scrape button loading/result state, an edit-show "Fetch from DCI" lookup button, and search/filter for the shows list. Depends on specs 1 and 2.

## Problem

Two related gaps in `ShowInfoScraperTask`, both discovered by fetching a real DCI page (`2026-dci-southwestern-championship`) rather than assuming:

1. **TBD rows are silently dropped, corps and all.** DCI marks a corps with no assigned set time as `<tr><td>TBD</td><td><strong>Corps Name</strong> - City, ST</td></tr>` — same table, same row shape as timed rows, just an unparseable time cell. `ConvertTo24h` returns `null` for it, and the caller `continue`s past the row. Since `PrefillShowAsync` builds its participating-corps list by walking these same rows, a show with 16 TBD corps (as in the real example) currently returns only the corps that already have times — the other 16 vanish entirely, forcing the admin to add them by hand.
2. **Exhibition shows have no scraped "show is over" signal.** Competitive shows get `ScoresAnnouncedTime` from a "Scores Announced" row. Exhibition shows instead end with an "Event Concludes" row, which today just falls through the generic schedule-entry list with no field capturing it.

## Approach

For representing "no assigned time," three options were considered:

1. **Nullable `Time` on the existing `ShowScheduleEntryEntity` row (chosen).** A TBD corps is a schedule entry whose time isn't known yet — that's a null case of the field that already exists, not a new concept.
2. Keep `Time` required, add an `IsUnscheduled` flag with a sentinel time. Rejected: a sentinel invites bugs where the flag isn't checked and the sentinel is treated as real.
3. Track "unscheduled" on `ShowCorpsEntity` instead. Rejected: splits "when does this corps perform" across two tables and loses the entry's position/label context.

For the exhibition "show concluded" signal, rather than adding a new `EventConcludesTime` column, **`ShowEntity.ScoresAnnouncedTime` is reused for both purposes**. Conceptually "Event Concludes" plays the same role for an exhibition show that "Scores Announced" plays for a competitive one — the moment the show is done. The admin form already has exactly one always-rendered time field for this (`scoresAnnouncedTime`/"Scores"), and it's already populated unconditionally regardless of `isExhibition` — so extending the scraper's label matching is sufficient; no new field, no new migration, no new API surface for this half of the problem.

## Design

### Data model

`ShowScheduleEntryEntity.Time`: `DateTimeOffset` → `DateTimeOffset?`. One EF Core migration (`dotnet ef migrations add MakeShowScheduleTimeNullable`); no Fluent API changes needed since nullability here is convention-based (no existing `.IsRequired()` override in `DcfDbContext`).

### Scraper (`DCF.Api/Scraping/ShowInfoScraperTask.cs`)

- `ParseScheduleEntries`: when `ConvertTo24h(rawTime)` returns `null` (unparseable — TBD or otherwise), **keep the row** with `Time24h = null` instead of `continue`-ing past it. This is a generic "unparseable → unscheduled" rule, not a literal `"TBD"` string match, so it stays correct if DCI phrases it differently in the future (e.g. "TBA").
- `ShowPrefillScheduleEntry` record: `Time24h` becomes `string?`.
- Extend the `scoresAnnouncedTime` extraction to also match "Event Concludes":
  ```csharp
  var scoresAnnouncedTime = filteredEntries
      .FirstOrDefault(e =>
          e.Label.Contains("score", StringComparison.OrdinalIgnoreCase) ||
          e.Label.Contains("recap", StringComparison.OrdinalIgnoreCase) ||
          e.Label.Contains("conclude", StringComparison.OrdinalIgnoreCase))
      ?.Time24h;
  ```
  Competitive and exhibition pages never carry both labels, so there's no ambiguity about which wins.

### `PrefillShowAsync` (`DCF.Api/Services/AdminService.cs`)

No code change required. It already loops over every entry in `prefillData.ScheduleEntries` to build the participating-corps list; it only misses TBD corps today because the scraper removes their rows before this method runs. Once the scraper stops dropping them, corps auto-inclusion works as-is.

### Contract threading

Nullable `Time`/`Time24h` flows through:
- `ShowPrefillScheduleEntry.Time24h` (`string?`)
- `ShowPrefillScheduleEntryResponse.Time` (`string?`)
- `ShowScheduleEntryRequest.Time` (`DateTimeOffset?`)
- `ShowScheduleEntryResponse.Time` (`DateTimeOffset?`)

`SortOrder` assignment is unchanged (`AdminService.CreateShowAsync`/`UpdateShowAsync` already assign it by row index via `schedule.Select((entry, i) => ...)`). Since DCI lists TBD rows after all timed rows on the page itself, they naturally sort last with no special-casing needed.

### Frontend (`DCF.Web/src/pages/SeasonDetail.tsx`, `DCF.Web/src/types/api.ts`)

`ShowScheduleEntry.time` and `ShowPrefillScheduleEntry.time` become `string | null`.

**Two existing bugs this change surfaces, both fixed as part of this work:**

1. `addShow`'s `schedulePayload` mapping calls `buildDateTime(rolloverDate, entry.time, showTz)` for every entry. With `entry.time === null` this builds `"2026-07-07Tnull:00Z"` — an invalid `Date` — and `.toISOString()` throws. Fix: when `entry.time` is null, send `time: null` and skip both `buildDateTime` and the rollover-detection comparison (`entry.time < prevTime`) for that entry, leaving `prevTime` unchanged (not overwritten to null) so a later timed entry — hypothetically, if DCI ever interleaves them — still compares against the last real time seen rather than against `null`.
2. `saveShowEdit`'s schedule round-trip does `new Date(e.time).toISOString()` when echoing a show's existing schedule back on save. `new Date(null)` does **not** throw — it silently evaluates to the Unix epoch (1970-01-01), corrupting an unscheduled entry's time to a real, wrong timestamp on *any* edit to the show, including unrelated ones like a rename. This is the more dangerous of the two since it fails silently. Fix: `time: e.time ? new Date(e.time).toISOString() : null`.

**Other frontend changes:**
- Add Show schedule preview: render "TBD" (muted styling, `--text-faint`) in place of the time when `entry.time` is null.
- The "Scores" `TimePicker` field (used for both `showScoresTime` and `editShow.scoresTime`) gets a dynamic label: "Scores" for competitive shows, "Concludes" for exhibition shows. Same field, same state variable — just contextual label text.
- `addShow`'s validation currently requires this field only when `!isExhibition`. Drop that guard so it's required for both show types, with a type-neutral error message (e.g. "Scores/concludes time is required."). `saveShowEdit` does not currently validate this field for either show type — a pre-existing asymmetry, left as-is.

## Testing

**Backend:**
- `ShowInfoScraperTaskTests.cs`: new fixture modeled on the real Southwestern Championship page structure (timed rows followed by trailing `TBD` rows, one table) asserting TBD rows are kept with `Time24h == null` rather than dropped. Added as a *new* fixture rather than modifying `ExhibitionHtml`/`CompetitiveHtml`, since existing tests (e.g. `ScrapeAsync_ScheduleRetainsAllNonGateEntries`) assert exact counts against those. New test confirming `ScoresAnnouncedTime` is populated from an "Event Concludes" label.
- `AdminServiceTests.cs`: `PrefillShowAsync` currently has zero test coverage. Add focused tests for the behavior being fixed (a TBD corps appears in the returned `CorpsIds`), not a full backfill of historical coverage for that method.

**Frontend:**
- `SeasonDetail.tsx` has no existing test file (996 lines, currently untested). Rather than a full component-test effort, export `buildDateTime` and the new null-safe schedule-mapping helper, and add a focused `SeasonDetail.test.tsx` covering exactly the two bugs above: normal time-string input (unchanged behavior) and `null`/TBD input (no crash, passes through as `null`).

## Out of scope (deferred to spec 2 or 3)

- No new `EventConcludesTime` column — `ScoresAnnouncedTime` is reused instead.
- "Completed" badge and Cancelled/Exhibition-with-reason admin actions → spec 2.
- Edit-form "Fetch from DCI" button, search bar, filter dropdown, trigger-scrape button loading state → spec 3. Until spec 3 ships, TBD corps are only picked up at *show creation* time — an admin cannot yet re-fetch an existing show to backfill times once DCI publishes them.
- No automatic re-polling of the DCI info page to detect newly-published times — manual re-fetch (and the button for it) is spec 3's job.
