# Corps Icons — Design Spec

**Date:** 2026-05-30

## Overview

Add per-corps icon support. Admins upload image files; icons replace text corps names in the draft board and league scores table. A grey initials block serves as the fallback when no icon has been uploaded.

---

## Data Model

`CorpsEntity` gains one nullable field:

```csharp
public string? IconPath { get; set; }
```

`IconPath` stores a relative path (e.g. `corps-icons/{id}.png`). It is `null` when no icon has been uploaded.

The existing `CorpsSummary` response record gains:

```csharp
public record CorpsSummary(Guid Id, string Name, string? IconUrl);
```

`IconUrl` is a root-relative path (e.g. `/uploads/corps-icons/{id}.png`) that the frontend resolves against `VITE_API_URL` — `<img src={${import.meta.env.VITE_API_URL}${corps.iconUrl}} />`. It is `null` when `IconPath` is null.

The frontend `Corps` type in `api.ts` gains `iconUrl?: string`.

---

## Storage & Serving

- Files are written to `uploads/corps-icons/` on disk relative to the API working directory.
- The API serves this folder as static files via `app.UseStaticFiles()` mapped to `/uploads`.
- Filename pattern: `{corpsId}.{ext}` — uploading a new icon overwrites the previous file for that corps (regardless of extension change; old file is deleted before writing the new one).
- Accepted formats: PNG, JPG/JPEG, WebP, SVG.
- Max file size: 2 MB. Requests exceeding this are rejected with `400 Bad Request`.

---

## Upload Endpoint

```
POST /api/admin/corps/{id}/icon
Content-Type: multipart/form-data
```

- Admin-only (same `IsAdminAsync` guard as all other admin endpoints).
- Validates content type and file size; returns `400` with an error message on failure.
- Writes file, updates `CorpsEntity.IconPath`, saves to DB.
- Returns `200` with `{ iconUrl: string }` — root-relative path (e.g. `/uploads/corps-icons/{id}.png`).

No delete/clear endpoint — icons are replaced, not removed.

---

## Initials Fallback

Computed on the frontend from the corps name: strip a leading "The " (case-insensitive), then take the first letter of each remaining word, uppercase, capped at 3 characters.

Examples: `"Blue Devils"` → `"BD"`, `"Santa Clara Vanguard"` → `"SCV"`, `"The Cadets"` → `"CAD"`.

Rendered as a square `<div>` with background `#3a3a4a`, white text, the same border-radius as an icon image.

---

## Admin UI

Corps rows in the Admin tab gain:

- **Left:** 28×28 icon preview — uploaded image or initials fallback.
- **Right of name:** a button labelled **"Upload Icon"** (no icon exists) or **"Replace Icon"** (icon exists).
- Clicking the button triggers a hidden `<input type="file" accept="image/png,image/jpeg,image/webp,image/svg+xml">`.
- On file selection, the upload fires immediately (no separate Save step).
- Button shows a brief loading state during the request; on success the preview updates in place.
- Rename and Delete buttons are unchanged.

---

## Draft Board

- The left `<td>` column of corps names is removed.
- Each grid cell renders a 36×36 icon (`<img>` or initials fallback `<div>`) inside the existing 44×44 cell.
- Cell states:
  - **Available:** current green border/background, icon at full opacity.
  - **Taken:** dark background, icon at 25% opacity.
  - **Selected:** accent ring (unchanged), icon at full opacity.
- Every cell gets `title={corps.name}` for the hover tooltip.
- No other grid behaviour changes.

---

## League Scores Tab

- The "Corps" sub-column cells render a 22×22 icon (`<img>` or initials fallback) instead of the `pick.corpsName` text string.
- `title={pick.corpsName}` provides the hover tooltip.
- Column width is unchanged.

---

## Icon Sizing Reference

| Surface | Size |
|---|---|
| Draft grid cell | 36×36 px |
| Scores tab Corps column | 22×22 px |
| Admin row preview | 28×28 px |

Each usage site specifies its own dimensions independently — changing any one is a single-line edit.

---

## Out of Scope

- Deleting / clearing an existing icon (replace only)
- Corps icon on any surface not listed above (standings page, pick history, etc.)
- Emoji icons
