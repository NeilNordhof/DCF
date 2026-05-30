# Corps Sort Order — Design Spec

**Date:** 2026-05-30

## Overview

Admins can assign a placement number to each corps within a season (reflecting prior DCI season results). This ordering controls the row sequence in the draft board pick grid and is available everywhere corps appear. Corps with no placement float to the end alphabetically.

---

## Data Model

`SeasonCorpsEntity` gains one nullable field:

```csharp
public int? SortOrder { get; set; }
```

`null` means unranked — sorts after all ranked corps, then alphabetically by name.

---

## Backend

### Updated SeasonDetail response

`SeasonDetail` record gains:

```csharp
IReadOnlyDictionary<Guid, int?> CorpsSortOrders
```

Maps `CorpsId → SortOrder` for every corps in the season. Frontend uses this to pre-populate the number inputs. `GetSeasonDetailAsync` populates it from the `SeasonCorps` join.

### New admin endpoint

```
PUT /api/admin/seasons/{id}/corps/order
```

Body: `[{ corpsId: Guid, sortOrder: int? }]`

Admin-only. Bulk-updates `SortOrder` for all corps in the season in a single save. Does nothing if the season is published (returns 409 Conflict).

### New public endpoint

```
GET /api/seasons/{id}/corps
```

Returns `[{ id, name, iconUrl, sortOrder }]` ordered by `SortOrder ASC NULLS LAST, Name ASC`. Authenticated but not admin-only — all users can call this. Used by the draft room to get the season's corps in the correct order.

This also fixes the existing issue where non-admin users in the draft room couldn't load corps (which previously used the admin-only `GET /api/admin/corps`).

### League response

`League` API response gains `seasonId: string?` so the draft room knows which season to query.

---

## Frontend

### SeasonDetail — Draft Order section

Location: left column, below the corps chip section and Save Corps button, separated by a divider. Only shown when the season has at least one corps selected and is not published. Read-only (inputs disabled) after publish.

**State:** `corpsSortInputs: Record<string, string>` — maps corpsId → current input string. Initialised from `season.corpsSortOrders` on load. Separate from the persisted values until Save Order is clicked.

**Display:** The section renders only the currently-selected corps (matching the chip selection). The list is derived by sorting `selectedCorpsIds` by their current `corpsSortInputs` value — parsed as integer, blanks/invalid values sort to the end alphabetically. Sorting is recomputed on every input change so the list re-orders live as the admin types.

Each row:
- Small number input (width 36px, type `number`, min 1)
- Corps icon (22px, same `CorpsIcon` component)
- Corps name
- Blank/unranked corps have a dashed input border and muted name style

**Save Order button:** fires `PUT /api/admin/seasons/{id}/corps/order` with the current input values (blank inputs send `null`). On success, refreshes the season to update `corpsSortOrders`.

### DraftRoom — ordered corps

`DraftRoom` replaces `api.adminGetCorps()` with `api.getSeasonCorps(league.seasonId)`. The corps rows in the pick grid render in the order returned by the endpoint (SortOrder ASC NULLS LAST, then name). `league.seasonId` is the new field added to the `League` response.

---

## Sorting Behaviour Summary

| SortOrder | Outcome |
|---|---|
| 1 | Top of list |
| 2, 3 … n | Ascending |
| null (unranked) | After all ranked, alphabetical |

---

## Out of Scope

- Reordering corps in the scores tab or admin corps list (only draft board row order is affected now)
- Drag-and-drop reorder UI
- Validating that placement numbers are unique (duplicates allowed — admin's responsibility)
- Exposing sort order to non-admin views outside the draft board
