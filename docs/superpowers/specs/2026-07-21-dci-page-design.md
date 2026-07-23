# DCI page (schedule / standings / scores / recaps)

## Context

A new public-facing section of the site showing the *real-world* DCI tour's current season — schedule, standings, scores, and recaps. Distinct from the existing fantasy-league pages (Leagues, Draft Room, Standings tab within a league), which are about fantasy scoring built on top of this same underlying data.

## Problem

Today none of this is exposed outside the admin tool. `ShowEntity` (schedule) and `ScoreEntity` (the full judge-by-judge, caption-by-caption score breakdown scraped by `RecapScraperTask`/`ShowInfoScraperTask`) already hold everything needed — there's no missing data. But every existing API endpoint is `[Authorize]`-gated with no anonymous fallback policy (`Program.cs`), and there's no frontend route for any of it.

One domain correction made during brainstorming: a DCI "recap" is **not** prose/commentary — there is no recap text anywhere in this domain. A recap *is* the judge-by-judge caption score breakdown table, in the same structure DCI itself publishes (verified directly against a live `dci.org` recap page during design). That's exactly what `ScoreEntity` already stores, so "recap" needs no new scraping or schema work, just a UI for data that's already captured.

## Approach

**No new data model or scraping.** Schedule reads from `ShowEntity` (+ `ShowScheduleEntryEntity` for lineup/set times). Standings/Scores/Recap all read from `ScoreEntity`. A key finding from investigating the schema: `ScoreEntity` already stores a standalone `Caption.Total` row per `(CorpsId, ShowId)` — DCI's own panel grand total, scraped from the recap's dedicated "Total" cell rather than summed client-side, with a unique `(CorpsId, ShowId, Caption, Judge)` index guaranteeing exactly one such row. So "this corps' real score at this show" is `db.Scores.Where(s => s.ShowId == showId && s.Caption == Caption.Total)` — a lookup, not an aggregation. Recap is `db.Scores.Where(s => s.ShowId == showId)` grouped by corps — every caption/judge row plus the section totals (`GeneralEffect`/`Visual`/`Music`, `Judge == null`) and `SubTotal`/`Penalty`/`Total` together are the complete table.

**This is unrelated to `ComputedScoreEntity`/`StandingsService`.** That pipeline computes *fantasy* standings — a weighted average of a league's drafted corps across `ComputedScoreEntity`'s derived caption fields. It must not be reused or referenced for this feature; the DCI page needs corps' real scores, not fantasy point totals.

**Public / anonymous access**, approved over keeping it behind the existing login wall. Rationale: this is real-world DCI data, not fantasy-specific, so making it public can drive non-member signups. This is the *first* anonymous-accessible surface in the app with real scope — precedent exists (`AuthController.Logout`, `NotificationsController.Unsubscribe` are both `[AllowAnonymous]`) but nothing this size has been public before.

**New `PublicDciController`** (route prefix `api/dci`), with no `[Authorize]` anywhere in the file — not a relaxed policy on an existing controller. Backed by a new `DciPublicService` whose `record` DTOs are declared at the top of the file, matching the established convention (`AdminService.cs`'s `ShowSummary`/`SeasonSummary` style) rather than a separate DTOs-folder file. Two alternatives were considered and rejected:
- Adding `[AllowAnonymous]` actions directly to `SeasonsController` — rejected because it bolts 4-5 new public endpoints onto a controller that's currently 2 small authenticated actions serving a different purpose (fantasy-app season context), making it harder to verify at a glance that nothing sensitive is exposed, and forces reusing `/api/seasons/active`'s publish-based "current season" logic where a calendar-based one is more correct here (see below).
- Reusing `AdminService`'s existing show-query logic — rejected because those DTOs are shaped for the admin use case (scrape status/error internals) and would need re-mapping to be public-safe anyway, at which point it's the new-controller approach with extra indirection.

**Routes are season-scoped by ID from the start** (`GET /api/dci/seasons/{seasonId}/standings`, not an implicit "current season" baked into the URL). This costs nothing extra now and means a future season picker — raised during design, explicitly deferred out of this spec, see below — is just a dropdown calling the same routes with a different ID, no breaking API change later.

**"Current season" resolution** (used by the one endpoint that doesn't take a `seasonId`, to tell the frontend what to default to) is calendar-based, not publish-order-based — deliberately different from `/api/seasons/active`'s "latest published season by year," which is a fantasy-app publishing-workflow concept that can disagree with what's actually touring right now. Resolution order, all tiers filtered to `IsPublished` (the public page shouldn't show a season an admin hasn't finished setting up):
1. The season with `Status == SeasonStatus.Active` (auto-maintained by the existing `SeasonStatusService`).
2. If none, the most recently `Completed` season (by `Year`, then `EndDate`) — so the off-season shows last season's results instead of nothing, the way real sports sites do.
3. If still none (e.g. a fresh deployment with only an `Upcoming` season), the most recent `Upcoming` season.

**No caching.** Standings/Last-3-Avg is computed on every request. This matches the existing precedent — `StandingsService` already computes fantasy standings on read with no materialized table — and the underlying query is an indexed lookup, not an expensive aggregation. Revisit only if real traffic data says otherwise.

**Ranks are computed from the actual score values at read time**, not read from `ScoreEntity`'s scraped `TotalRank`/`RepertoireRank`/`PerformanceRank` fields. DCI's own recap sheets can have ties or gaps in those columns; deriving rank by sorting the real numbers is more reliable and is what every approved mockup's interaction already assumes.

### API surface

| Route | Returns |
|---|---|
| `GET /api/dci/seasons/current` | `{ id, year }` — resolved per the algorithm above |
| `GET /api/dci/seasons/{seasonId}/standings` | Per corps: id, name, icon, latest `Total` score + show name/date, last 3 `Total` scores each with show name/date |
| `GET /api/dci/seasons/{seasonId}/schedule` | Upcoming shows (`Date >= today`, ascending) with name, date/time/timezone, `Location`, exhibition flag, schedule entries. Grouping into weeks happens client-side. |
| `GET /api/dci/seasons/{seasonId}/scores` | Completed shows (`Date < today`, descending) with, per corps, rank + name + `Total` score — or the show's `NoScoreReason` when scoring didn't happen (cancelled/rained out), reusing that field exactly as the existing admin UI does rather than inventing new state. Grouping into weeks happens client-side. |
| `GET /api/dci/shows/{showId}/recap` | Show name/date/location + every `ScoreEntity` row for that show, grouped by corps |

## Design

### Shared chrome

Full-width `Nav` (`components/Nav.tsx`) gets a new "DCI" top-level link. Standings/Schedule/Scores share a tab strip below it; the Recap page is a standalone route (its own URL, reached from a Scores card) with a "← Back to Scores" link instead of the tab strip. Content is constrained the same way every other page does it: `App.tsx`'s `.page-content` wrapper, `maxWidth: 1200`, centered, `padding: '24px 20px'` — confirmed by reading `App.tsx` directly. Visual style matches the existing dark theme exactly: `index.css` custom properties (`--bg: #0d0f14`, `--surface: #161822`, `--accent: #c084fc`, `--text-heading: #f3f4f6`, `--text-muted: #6b7280`, `--text-faint: #4b5563`), same pill-badge conventions used in `SeasonDetail.tsx`/`Nav.tsx`.

Four interactive mockups were built and approved via the brainstorming skill's visual companion, iterated live against real feedback rather than approved as static images. They're saved under `.superpowers/brainstorm/1511-1784756539/content/` (gitignored, local to the machine this session ran on — not committed) and should be treated as the reference implementation for markup/CSS/JS, not rebuilt from scratch:
- Standings: `.superpowers/brainstorm/1374-1784658597/content/standings-tab-v2.html` (built in an earlier session)
- Schedule: `schedule-tab-v3.html`
- Scores: `scores-tab-v2.html`
- Recap: `recap-page-v3.html`

### Standings tab (default landing tab)

Ranks the current season's corps. Columns: `#` (rank), `Corps`, **Latest Score** (most recent show's `Total`; default sort, descending), **Last 3 Avg** (average of the last 3 shows' `Total`, mirroring a dci.org convention — independently sortable from Latest Score, and the two can disagree by design), `Last Event` (name + date the Latest Score came from).

Both score columns are click-to-sort (clicking the active column flips asc/desc; a small ▲/▼ arrow shows current column + direction — new columns default to descending, since "best performance first" is the natural reading). `Last 3 Avg`'s header carries a small "i" hint icon; hovering it explains that hovering a score below shows its breakdown. Hovering an individual Last 3 Avg cell shows a tooltip listing the 3 contributing shows as **Score – Show Title – Date**.

### Schedule tab

Upcoming shows only (not a mixed past/future list — Scores tab owns everything completed). Grouped into weeks ("Week of Jul 27"), each week a horizontally-scrollable row of cards — arrow buttons on both edges plus click-and-drag — rather than a flat list or a vertical stack, per explicit request.

Each card: show name (wraps to 2 lines for long titles rather than truncating), exhibition badge directly under the title when applicable, date/time/timezone, city/state location (`ShowEntity.Location` is already stored in exactly that format — confirmed against the scraper's test fixtures, e.g. `"Camarillo, CA"` — so it's used as-is, no parsing needed), and the full lineup (`ShowScheduleEntryEntity`, the same schedule data already shown in the admin "Fetch from DCI" preview), including its TBD-time-aware rendering when DCI hasn't published set times yet. Cards are 230px wide; lineup rows show time on a fixed-width column with the corps name left-aligned immediately after (not right-aligned against the card edge — right-justifying was tried first and made the list hard to scan).

### Scores tab

Completed shows, most recent week first (same weekly-carousel pattern as Schedule, chosen deliberately for interaction consistency even though each card's content is taller here). Each card: show name, date, exhibition badge where applicable, then the **full results list** for every corps that performed — rank, corps name (left-aligned, same fix as Schedule), `Total` score (right-aligned) — and a "View Recap →" link at the bottom leading to that show's Recap page.

A completed-date show has three possible states, not two — caught during spec self-review:
1. Has `Total` rows → show the full results list as above.
2. Has `NoScoreReason` set (cancelled, rained out) → show that reason instead of a results list, reusing the existing field rather than inventing new state.
3. Has neither → the show happened but scraping hasn't completed yet (still within the retry/delay window, or a retry is pending). Card shows a "Scores pending" state, distinct from `NoScoreReason` — this is an expected transient state given the existing scrape delay/retry timing, not an error.

### Recap page (standalone, own URL)

One wide matrix — corps as rows ordered by rank, captions as columns — matching the real structure DCI itself publishes (verified against a live `dci.org` recap page, not assumed): General Effect 1 and 2 (Rep/Perf/Tot each), a GE Total column, Visual Proficiency/Visual Analysis/Color Guard (Cont/Achv/Tot each) and a Visual Total column, Brass/Music Analysis/Percussion (Cont/Achv/Tot each) and a Music Total column, then Sub Total, Penalties, and Total Score. The GE/Visual/Music/Sub Total and Total Score columns wrap their headers to 2 lines and share a fixed 64px width, narrower than the full phrase would otherwise need.

Every score cell shows its value with that column's rank directly underneath (not a superscript). Both the corps column and the two header rows are sticky — corps stays pinned while scrolling horizontally through ~30 columns, headers stay pinned while scrolling vertically. The scroll area is sized to show 12 rows before scrolling (chosen specifically so a Finals recap — always exactly 12 corps — fits with no scroll at all). Clicking anywhere in a corps' row cell (not just the name) highlights that entire row. Every score column defaults to descending on first click (dci.org itself defaults to ascending, which was called out explicitly as wrong for this use case). Sample data in the mockup deliberately includes rank disagreements between overall placement and individual captions (e.g. a corps leading Color Guard despite placing 5th overall) to prove the per-column ranks are doing real work, not just decoration.

Unlike dci.org, where the whole page scrolls horizontally, only the table itself scrolls inside a fixed-height box — the page header and breadcrumb stay in place. The sticky leaf-row header's vertical offset is measured from the actual rendered height of the group-row header at load time (`getBoundingClientRect()`) rather than a hardcoded pixel value, since a guessed constant drifted out of sync with real text rendering during design and caused visible clipping. Sort-direction arrows are positioned absolutely, pinned to each header cell's right edge, rather than sitting inline after the label text — an inline empty arrow span was found to skew `text-align: center` asymmetrically, which is what caused headers to look inconsistently left/right-justified during design.

### Edge cases (found during spec self-review)

- **Standings**: a corps with zero `Total` rows yet this season (hasn't performed) is excluded from the list entirely — Latest Score and Last 3 Avg are undefined with no data points, so there's nothing to rank. It appears automatically once it has at least one score.
- **Schedule**: a week with zero upcoming shows isn't rendered as an empty section — weeks are skipped, not shown blank.
- **Scores**: see the three-state handling above.

## Testing

Backend: `DciPublicService` gets unit tests for the current-season resolution algorithm (Active present; Active absent, falls back to most recent Completed; both absent, falls back to most recent Upcoming; unpublished seasons excluded at every tier), the Last 3 Avg calculation (fewer than 3 shows played yet), and rank computation (ties, single-corps edge case). Standard integration-test coverage for the new `PublicDciController` routes returning 200s with no `Authorization` header, matching the existing pattern for other controllers' happy-path tests.

Frontend: no full component-test effort for the four page components, consistent with the existing boundary already established for other large, mostly-presentational pages (e.g. `SeasonDetail.tsx`) — verified manually instead. Any extracted pure helper functions (week-grouping, rank computation if duplicated client-side, `NoScoreReason` display logic) get unit tests the same way `SeasonDetail.helpers.test.ts` covers similar helpers today.

## Out of scope

- **Season picker UI.** Raised during design as a wanted future capability. The backend is already season-scoped by ID to make this cheap to add later, but the picker control itself (a dropdown to browse past seasons) is not built in this pass — the page just defaults to and only shows the current season.
- **Caching/materialization** of standings or any other computed value. Revisit only if real traffic data shows the on-read computation is a problem.
- **Live MQTT updates.** The app already has `dcf/scores/updated` as a topic, but wiring the public DCI page to auto-update in real time when a scrape completes is a nice-to-have, not something the original request asked for. v1 is fetch-on-load/on-navigation, like any other page in the app.
- **Any recap prose/commentary.** Confirmed early in design that this doesn't exist anywhere in the domain — a recap is the score table, full stop.
