# DCI Show Auto-Populate Design

**Date:** 2026-06-29
**Branch:** feat/dci-show-autopopulate

## Overview

When an admin creates a show, they currently fill in all fields manually (times, competing corps, etc.). This feature adds a "Fetch from DCI" button to the add-show form that scrapes the DCI events page for the show, geocodes the location, and pre-populates the form. It also introduces two new data concepts: show location (with lat/lng for a future tour map) and a full show schedule (performance order, retreat, awards).

A new `IsExhibition` flag handles shows where corps perform but are not scored — these have no recap URL and are excluded from score scraping.

---

## Data Model

### `ShowEntity` changes

- `string? Url` — was non-nullable; made nullable so exhibition shows (no recap page) are valid
- `bool IsExhibition` — default `false`; gates the score scraper from running on this show
- `string? Location` — raw location string from the DCI events page (e.g. `"Lucas Oil Stadium, Indianapolis, IN"`)
- `double? Latitude` / `double? Longitude` — geocoded at create time; stored for the future map feature; null if geocoding fails

### New `ShowScheduleEntryEntity`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `ShowId` | `Guid` | FK → `ShowEntity` |
| `SortOrder` | `int` | Preserves DCI page order |
| `Time` | `DateTimeOffset` | Anchored timestamp, consistent with `StartTime` on the show |
| `Label` | `string` | Display name — corps name, "Retreat", "Awards", etc. |
| `CorpsId` | `Guid?` | Matched to `CorpsEntity`; null for non-world-class corps or non-corps events |

Corps matching uses case-insensitive string comparison against the season's corps list. Unmatched entries retain `CorpsId = null` and keep the raw scraped label.

---

## Backend Components

### `ShowInfoScraperTask` / `IShowInfoScraperTask`

Scrapes `https://www.dci.org/events/{year}-{slug}` via the existing `IHtmlFetcher` and `HtmlAgilityPack`. Returns a `ShowPrefillData` record containing:
- Location string
- Start time and scores-announced time
- Competing corps names
- Schedule entries (time + label) — everything after gates open

Corps name-to-entity matching is performed by the calling service, keeping the scraper stateless. The scraper omits "Gates Open" schedule entries. `IsExhibition` is detected best-effort from the page (e.g. absence of a scores-announced time, or an "Exhibition" / "Non-Competitive" label); the admin can always override via the toggle before submitting.

### `IGeocodingService` / `NominatimGeocodingService`

Calls OpenStreetMap Nominatim (`nominatim.openstreetmap.org/search?q=...&format=json`). Free, no API key required. Registered as a singleton with a named `HttpClient` that sets the required `User-Agent` header (Nominatim policy). Returns `(double Lat, double Lng)?`; null on failure.

### Prefill endpoint

```
GET /api/admin/shows/prefill?name={name}&year={year}
```

Constructs the events URL internally: `https://www.dci.org/events/{year}-{slug}` where `slug` is produced by a C# slugify helper (lowercase, replace non-alphanumeric runs with hyphens — mirrors the existing TypeScript `slugify` in the frontend). Runs `ShowInfoScraperTask`, geocodes the location, matches corps names against the season's corps (queried by `year`), and returns a `ShowPrefillResponse`.

Returns `404` with a descriptive message if the events page cannot be fetched or parsed.

### Scraper guard in `ScrapeSchedulerService`

`ScheduleScrape` skips shows where `IsExhibition == true` or `Url == null`. No score scrape is ever attempted for exhibition shows regardless of how they were created.

---

## API

### Request DTOs

`CreateShowRequest` and `UpdateShowRequest` gain:

```csharp
string? Url,                             // was required; nullable for exhibition shows
bool IsExhibition,
string? Location,
double? Latitude,
double? Longitude,
List<ShowScheduleEntryRequest> Schedule  // empty list = no schedule entries
```

```csharp
public record ShowScheduleEntryRequest(
    DateTimeOffset Time,
    string Label,
    Guid? CorpsId
);
```

### `ShowPrefillResponse`

```csharp
public record ShowPrefillResponse(
    string? Location,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScoresAnnouncedTime,
    string? Timezone,
    bool IsExhibition,
    List<Guid> CorpsIds,
    List<ShowScheduleEntryResponse> Schedule
);

public record ShowScheduleEntryResponse(
    DateTimeOffset Time,
    string Label,
    Guid? CorpsId
);
```

### `Show` response type

The existing `Show` response type gains `location`, `latitude`, `longitude`, `isExhibition`, and `schedule: ShowScheduleEntry[]` so the admin sees and can act on all fields post-creation.

### `AdminService.CreateShowAsync` / `UpdateShowAsync`

Saves `ShowScheduleEntryEntity` rows in the same transaction as the show insert — no partial state if the insert fails. On update, the existing schedule entries for the show are deleted and replaced with the submitted list (full replace, not merge).

---

## Frontend

All changes are in `SeasonDetail.tsx` with type updates in `api.ts`.

### New form fields

- **`IsExhibition` toggle** — near the top of the form. When checked, the URL field hides (no recap page); all other fields (corps, times, schedule) remain.
- **`Location`** — plain text input; pre-populated by the fetch, editable.
- **Schedule display** — read-only list rendered after a successful fetch showing each entry's time and label. Stored as-is on submit; not editable in the form.

### "Fetch from DCI" button

- Sits next to the Name field; enabled once Name is non-empty.
- On click: `GET /api/admin/shows/prefill?name={name}&year={seasonYear}`
- On success: populates Location, StartTime, ScoresAnnouncedTime, Timezone, CorpsIds, IsExhibition, and Schedule in form state. The recap URL field continues to auto-generate from the name as today (same slug, different path prefix).
- On failure: shows an inline error (`"Could not fetch from DCI — fill in manually"`) and leaves the form untouched.

### `api.ts` type additions

```ts
export interface ShowScheduleEntry {
  time: string;
  label: string;
  corpsId: string | null;
}

export interface Show {
  // ... existing fields ...
  isExhibition: boolean;
  location?: string;
  latitude?: number;
  longitude?: number;
  schedule: ShowScheduleEntry[];
}
```

---

## Error Handling

| Scenario | Behaviour |
|---|---|
| DCI events page not found / not yet published | Prefill returns `404`; frontend shows inline warning; admin fills manually |
| DCI page structure changed (parse failure) | Prefill returns `404` with parse error message; same as above |
| Geocoding fails (Nominatim unavailable / unrecognised string) | Logged server-side; `latitude`/`longitude` stored as null; show still created |
| Corps name not matched (non-world-class / name variation) | Schedule entry included with `corpsId: null`, raw label preserved |
| Partial scrape (times not yet on DCI page) | Each `ShowPrefillResponse` field is nullable; frontend populates what was returned |
| Exhibition show reaches scrape scheduler | `ScrapeSchedulerService` guards on `IsExhibition` and `Url == null`; scrape skipped |

---

## Out of Scope

- Editing schedule entries in the form (admin re-fetches or accepts what was scraped)
- Map rendering (consumes `latitude`/`longitude` stored here; built as a separate feature)
- Geocoding for shows created before this feature (can be backfilled separately)
