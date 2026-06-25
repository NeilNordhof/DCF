# Admin Mobile — Design Spec
**Date:** 2026-06-24
**Status:** Approved for implementation

## Problem

The admin UI is desktop-only. The specific mobile use case driving this work is remote management while away from home: triggering score scrapes after a show ends, and fixing a show's recap URL if the auto-generated one is wrong. Both actions live inside the show cards on `SeasonDetail.tsx`.

## Scope

- **In scope:** `SeasonDetail.tsx` — responsive layout, collapsible corps panel, show cards.
- **Out of scope:** `Admin.tsx` (top-level seasons/corps list) — already single-column and usable on mobile without changes.

## Layout: SeasonDetail

### Two-column → stacked

The current layout is a side-by-side flex row:

```
[ Corps sidebar 280px ] [ Shows — flex: 1 ]
```

On mobile (`max-width: 640px`) this becomes a single column, shows first:

```
[ Shows — full width ]
[ Corps panel — collapsible ]
```

The outer `display: flex` row gets `flex-direction: column` on mobile. The corps sidebar loses its `flex: 0 0 280px` fixed width and becomes full-width.

### Corps panel — collapsible on mobile

Wrapped in a native `<details>` / `<summary>` element on mobile (controlled via a CSS class toggle or a small `useState`). Closed by default. The summary label is "Corps & Draft Order". No new JS state needed beyond the existing layout — a boolean `corpsOpen` works fine and is local to the component.

The `<details>` approach avoids JS state but requires overriding browser-default summary styling. A `useState` boolean with a toggle button is simpler to style consistently and preferred here.

### Header row

The `justify-content: space-between` row containing the season title/dates and the Publish button already wraps acceptably. The Publish button gets `width: 100%` on mobile so it's a full-width tap target.

The inline date-editing form (the `editingDates` state) is left as-is — it's a rare management task and not a mobile use case.

## Show Cards

Show cards are already single-column internally and collapse/expand cleanly. Two changes needed:

### Date/TZ and Start/Scores rows

These two rows in the add-show and edit-show forms are currently `display: flex` with multiple inline labels and inputs side-by-side. On mobile they stack:

```
DATE  [ 2026-07-12 ]
TZ    [ ET         ]

START  [ ——   ]
SCORES [ 22:30]
```

Each row gets a class (`admin-show-form-row`). On mobile, `flex-wrap: wrap` is added and the label/input pair inside each row gets `flex: 1 1 100%` so they occupy a full line each. No structural JSX changes — just CSS.

### Scrape button and URL field

No changes needed. The scrape trigger button is already `width: 100%`. The URL input is already `flex: 1`. Both are full-width and tappable.

## CSS strategy

All mobile overrides go in `index.css` under the existing `@media (max-width: 640px)` block, using new class names added to `SeasonDetail.tsx`. Pattern follows what was already done for the mobile redesign (PR #46).

New classes needed:
- `admin-season-layout` — the outer two-column flex row
- `admin-corps-panel` — the 280px sidebar
- `admin-shows-panel` — the flex: 1 shows column
- `admin-show-form-row` — date/TZ and start/scores rows inside show forms
- `admin-publish-btn` — the Publish button

## What doesn't change

- Show card collapsed/expanded behaviour
- Scrape button, URL input, corps chips inside expanded cards
- `Admin.tsx`
- Any backend code
