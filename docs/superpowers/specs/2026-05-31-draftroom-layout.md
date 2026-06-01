# DraftRoom Layout Overhaul

**Date:** 2026-05-31
**Status:** Approved

## Overview

Seven visual and structural changes to the DraftRoom page. No backend changes required.

---

## 1. Page layout — match standard page width

**Current:** DraftRoom is a standalone full-screen route with no Nav and no max-width container.

**Change:** Wrap the `/leagues/:id/draft` route in `AuthenticatedLayout` in `main.tsx`, matching every other authenticated page. The DraftRoom component's outer div changes from `height: 100vh` to `height: calc(100vh - 44px - 48px)` (viewport minus 44px nav and 48px top+bottom padding from `AuthenticatedLayout`).

---

## 2. Combined bar (replaces `renderTopBar` + `renderSubmitBar`)

The two separate bars are replaced by a single `renderBar()` function. The bar spans the **grid column only** — it does not stretch across the sidebar panel.

**Structure (all states):**
```
[← League btn] | [League name] | [Status block]
```

The `← League` button and league name are a fixed left anchor. The status block fills the remaining width and contains state-specific content.

### Bar states

| State | Condition | Status block content |
|---|---|---|
| Scheduled lobby — commissioner | `status === 'Open'`, `league.draftStartTime` set, `league.isCommissioner` | `Draft Begins In` label + countdown timer + date/time, then **Start Early** button immediately to the right of the countdown (tight, no flex stretch) |
| Scheduled lobby — non-commissioner | `status === 'Open'`, `league.draftStartTime` set | `Draft Begins In` label + countdown + date/time only. Nothing on the right. |
| Manual lobby — commissioner | `status === 'Open'`, no `draftStartTime`, `league.isCommissioner` | **Start Draft** button |
| Manual lobby — non-commissioner | `status === 'Open'`, no `draftStartTime` | "Waiting for [commissioner display name] to start the draft" (italic, muted) |
| InProgress — my turn | `status === 'InProgress'`, `isMyTurn` | `On the Clock` label + `[Name] · Round X · Pick Y` + vertical divider + Selected corps label + **Submit Pick** button |
| InProgress — commissioner waiting | `status === 'InProgress'`, `!isMyTurn`, `league.isCommissioner` | `Now Picking` label + `[Name] · Round X · Pick Y` + **Skip Pick** button |
| InProgress — regular user waiting | `status === 'InProgress'`, `!isMyTurn`, `!league.isCommissioner` | `Now Picking` label + `[Name] · Round X · Pick Y` |
| Completed | `status === 'Completed'` | `Draft Complete` label (muted) |

**Bar background:** green gradient for lobby states, purple gradient for InProgress, flat surface for Completed.  
**Bottom border accent:** green for lobby, purple for InProgress, `var(--border)` for Completed.

The commissioner display name for the "Waiting for..." text is sourced from `draftState.members` (find member whose `userId === league.commissionerUserId`). Fall back to `"the commissioner"` if the member is not found or `commissionerUserId` is undefined.

---

## 3. Grey cell backgrounds

Available (unpicked) grid cells change from green to neutral grey:

| Property | Before | After |
|---|---|---|
| `background` | `var(--green-bg)` (#052e16) | `#1c1f2c` |
| `border` | `1px solid var(--green-border)` | `1px solid var(--border)` |

Taken, selected, and previewed cell styles are unchanged.

---

## 4. Caption shorthands

A lookup map applied to all caption header labels in the grid. Font size increases from 8px → 10px.

```ts
const CAPTION_SHORT: Record<string, string> = {
  GeneralEffectCombined: 'GE',
  GeneralEffect1:        'GE-Vis',
  GeneralEffect2:        'GE-Mus',
  VisualCombined:        'Vis',
  Visual:                'Vis',
  Colorguard:            'CG',
  VisualProficiency:     'VP',
  VisualAnalysis:        'VA',
  MusicCombined:         'Mus',
  Brass:                 'Br',
  Percussion:            'Perc',
  MusicAnalysis:         'MA',
};
```

Usage: `CAPTION_SHORT[cap] ?? cap` (fallback to raw value for any unmapped caption).

---

## 5. Centred grid

The grid table is horizontally centred within the grid panel using `display: flex; justifyContent: center` on the scrollable container. The table itself is not full-width; it only takes up the space its cells need.

---

## 6. Column spacing — generous formula

`borderSpacing` on the grid table uses a per-caption-count horizontal gap. Vertical gap stays at 3px throughout.

```ts
const H_GAP: Record<number, number> = { 3: 18, 4: 12, 5: 7 };
const hGap = H_GAP[captions.length] ?? 2;
// table style: borderSpacing: `${hGap}px 3px`
```

---

## Files Changed

| File | Change |
|---|---|
| `DCF.Web/src/main.tsx` | Add `<AuthenticatedLayout>` wrapper to the `/leagues/:id/draft` route |
| `DCF.Web/src/pages/DraftRoom.tsx` | Combined bar, grey cells, caption shorthands, centred grid, column spacing, remove standalone `renderTopBar` and `renderSubmitBar` |
