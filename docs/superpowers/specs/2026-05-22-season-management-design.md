# Season Management Design

**Date:** 2026-05-22
**Status:** Approved

## Problem

The admin page has no UI for managing seasons. The backend already has partial season support (create, activate via bool flag, assign corps, add shows), but several gaps exist: the `IsActive` bool cannot express the `Upcoming → Active → Completed` lifecycle, there are no season start/end dates, and there is no concept of a season being "published" (fully configured and ready for leagues). The frontend has no season management UI at all.

## Goals

- Admin can create a season with a year, start date, and end date.
- Season status transitions automatically based on dates: `Upcoming` → `Active` (on start date) → `Completed` (day after end date).
- Admin explicitly publishes a season when setup is complete, making it available for league creation. Publishing is permanent.
- Admin can assign corps to a season and add shows to it.
- The admin page uses a tabbed layout: Seasons tab and Corps tab.
- Clicking a season opens a dedicated detail page at `/admin/seasons/:id`.

## Backend

### `SeasonStatus` enum

New enum added to `DCF.Data`:

```csharp
public enum SeasonStatus { Upcoming, Active, Completed }
```

### `SeasonEntity` changes

- Remove `bool IsActive`
- Add `SeasonStatus Status { get; set; }` defaulting to `Upcoming`
- Add `DateOnly StartDate { get; set; }`
- Add `DateOnly EndDate { get; set; }`
- Add `bool IsPublished { get; set; }` defaulting to `false`

An EF Core migration is required.

### `ScrapeSchedulerService` update

The existing query `s.Season.IsActive` (bool) is updated to `s.Season.Status == SeasonStatus.Active`.

### `SeasonStatusService` (new background service)

`BackgroundService` that drives automatic status transitions. Follows the same `ConcurrentDictionary<Guid, CancellationTokenSource>` + `Task.Delay` pattern as `ScrapeSchedulerService`.

On startup (`ExecuteAsync`):
1. Load all seasons with `Status != Completed`.
2. For any `Active` season where `EndDate < today`: immediately set `Status = Completed`.
3. For any `Upcoming` season where `StartDate <= today`: immediately set `Status = Active`. Then schedule its completion (step 4).
4. For remaining `Active` seasons: schedule a `Task.Delay` to fire at midnight UTC on `EndDate.AddDays(1)` and set `Status = Completed`.
5. For remaining `Upcoming` seasons: schedule a `Task.Delay` to fire at midnight UTC on `StartDate`, set `Status = Active`, and then schedule a second `Task.Delay` to fire at midnight UTC on `EndDate.AddDays(1)` to set `Status = Completed`.

`ScheduleSeason(SeasonEntity season)` public method: cancels any existing scheduled work for that season and re-schedules from the current state. Called by `AdminService` after creating a season.

### `IAdminService` / `AdminService` changes

**Modified records:**

```csharp
public record SeasonSummary(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished);
public record SeasonDetail(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished, IEnumerable<Guid> CorpsIds);
```

**Modified methods:**

- `CreateSeasonAsync(int year, DateOnly startDate, DateOnly endDate)` — creates with `Status = Upcoming`, `IsPublished = false`. Calls `SeasonStatusService.ScheduleSeason` after save.
- `ActivateSeasonAsync` → removed entirely.

**New methods:**

- `GetSeasonDetailAsync(Guid id) → SeasonDetail?` — returns single season with its assigned `CorpsIds`. Returns `null` if not found.
- `PublishSeasonAsync(Guid id) → bool` — sets `IsPublished = true`. Returns `false` if not found. Publishing is permanent (no unpublish).

### `AdminController` changes

- `POST /api/admin/seasons` — request body gains `StartDate` and `EndDate`.
- `PUT /api/admin/seasons/{id}/activate` → replaced by `POST /api/admin/seasons/{id}/publish`.
- `GET /api/admin/seasons/{id}` — new endpoint, returns `SeasonDetail` (404 if not found).

### `AdminRequests.cs` changes

```csharp
public record CreateSeasonRequest(int Year, DateOnly StartDate, DateOnly EndDate);
```

`SetSeasonCorpsRequest` and show requests are unchanged.

## Frontend

### `types/api.ts` additions

```ts
export type SeasonStatus = 'Upcoming' | 'Active' | 'Completed';

export interface Season {
  id: string;
  year: number;
  startDate: string;   // ISO date string (DateOnly)
  endDate: string;
  status: SeasonStatus;
  isPublished: boolean;
}

export interface SeasonDetail extends Season {
  corpsIds: string[];
}

export interface Show {
  id: string;
  name: string;
  url: string;
  date: string;
  scoresAnnouncedTime: string;
  corpsIds: string[];
}
```

### `client.ts` additions

```ts
adminGetSeasons: () =>
  request<Season[]>('/api/admin/seasons'),
adminGetSeason: (id: string) =>
  request<SeasonDetail>(`/api/admin/seasons/${id}`),
adminCreateSeason: (year: number, startDate: string, endDate: string) =>
  request<Season>('/api/admin/seasons', { method: 'POST', body: JSON.stringify({ year, startDate, endDate }) }),
adminPublishSeason: (id: string) =>
  request<void>(`/api/admin/seasons/${id}/publish`, { method: 'POST' }),
adminSetSeasonCorps: (id: string, corpsIds: string[]) =>
  request<void>(`/api/admin/seasons/${id}/corps`, { method: 'PUT', body: JSON.stringify({ corpsIds }) }),
adminGetShows: (seasonId: string) =>
  request<Show[]>(`/api/admin/seasons/${seasonId}/shows`),
adminCreateShow: (seasonId: string, name: string, url: string, date: string, scoresAnnouncedTime: string, corpsIds: string[]) =>
  request<{ id: string; name: string }>(`/api/admin/seasons/${seasonId}/shows`, {
    method: 'POST',
    body: JSON.stringify({ name, url, date, scoresAnnouncedTime, corpsIds }),
  }),
```

The existing `adminTriggerScrape` method is kept in `client.ts` for now (it will move to the show management UI in a future change, not in scope here).

### `Admin.tsx` changes

The page becomes a two-tab layout using simple tab state (`'seasons' | 'corps'`).

**Seasons tab (default):**
- On mount: loads seasons via `adminGetSeasons()`.
- Lists seasons sorted newest first. Each row: year, start–end date range, status badge (`Upcoming` / `Active` / `Completed`), `Published` badge if `isPublished`, and a "Manage →" link to `/admin/seasons/:id`.
- "Add Season" form: year (number), start date, end date inputs. On submit: calls `adminCreateSeason`, refreshes list, clears form.

**Corps tab:**
- Existing corps list and "Add Corps" form, moved here unchanged.

The existing Manual Scrape section is removed from this page (deferred to show management UI).

### `SeasonDetail.tsx` (new page at `/admin/seasons/:id`)

On mount: loads season detail via `adminGetSeason(id)`, all corps via `adminGetCorps()`, and shows via `adminGetShows(id)`.

**Header:** Year, start–end dates, status badge, published badge. "Publish" button — disabled if already published or if no corps are assigned (`corpsIds.length === 0`). Clicking Publish calls `adminPublishSeason(id)` and refreshes the season detail.

**Corps section:**
- Checklist of all corps. Checked state reflects the season's current `corpsIds`.
- "Save Corps" button: calls `adminSetSeasonCorps(id, selectedIds)`.
- Entire section is disabled (inputs + button) once `isPublished = true` — the corps roster is locked at publish time.

**Shows section:**
- Table of existing shows: name, date, URL.
- "Add Show" form: name, URL, date (date input), scores-announced datetime (datetime-local input), and a multi-select checklist of the season's assigned corps (the corps participating at this specific show). This checklist is always the season's corps, not the global corps list.
- Always editable regardless of published state.

### Routing (`App.tsx`)

Add one new `AdminRoute`-protected route:

```tsx
<Route path="/admin/seasons/:id" element={<AdminRoute><SeasonDetail /></AdminRoute>} />
```

## Out of Scope

- Show edit/update UI and moving manual scrape trigger to show management (separate feature).
- Unpublishing a season.
- Automatic season selection when a user creates a league.
