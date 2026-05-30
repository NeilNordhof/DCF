# Leagues Page Redesign

**Date:** 2026-05-27
**Status:** Ready for implementation

## Overview

Four areas of change: tabbed Leagues page, redesigned league cards, Create page updates (max players + caption dropdowns), and non-member view in League Details. No backend favorite/featured league concept — that idea was dropped.

---

## 1. Leagues Page — Tabbed Layout

The Leagues page splits into two tabs: **My Leagues** and **Join**.

Tab state is driven by a URL query param (`/leagues?tab=join`). This enables deep-linking from other parts of the app (e.g., the empty-state CTA navigates to `?tab=join`, the `← Browse` button on League Details navigates to `/leagues?tab=join`). Default tab is `My Leagues`.

### My Leagues Tab

All leagues the user is a member of are displayed as uniform cards (no featured/promoted card). Cards are displayed in the order returned by the API (order joined).

Each card shows:

| Draft Status | Card Content |
|---|---|
| `InProgress` | Name · LIVE DRAFT badge · "Rank X/Y · score pts" (or "—" if no scores yet) · **Join Draft Room →** button (green) |
| `Open` | Name · LOBBY OPEN badge · "[N] members" · **Join Draft Room →** button (green) |
| `Scheduled` | Name · SCHEDULED badge · "Draft: [date] · ⏱ in X days Y hrs" |
| `NotStarted` | Name · NOT STARTED badge · "[N] members · waiting for commissioner" |
| `Completed` | Name · COMPLETED badge · "Rank X/Y · score pts (final)" in muted colour |

Rank and score are returned as `userRank` and `userScore` fields on each league in the `GET /api/leagues` response, avoiding N+1 standings calls. The countdown for scheduled leagues is computed client-side from `draftStartTime`.

**Empty state** (user is in no leagues):
> "You are not currently in any leagues. [Join a league] or [Create your own]!"

Both links navigate to their respective destinations (`?tab=join` and `/leagues/create`).

### Join Tab

Header text: "Browse and join a public league, or join by code:"

Below that, an invite-code input with a **Look up** button. Submitting navigates to `/leagues/{id}?code=XXX` for the found league. If the code is invalid or not found, an inline error is shown below the input ("No league found with that code.") and no navigation occurs.

Below the code row, a list of all public leagues (fetched from a new public endpoint). Each row shows name, member count as current/max (e.g., "3 / 8 members"), and status badge. Clicking navigates to `/leagues/{id}` (no code needed for public leagues).

**Empty state** (no public leagues):
> "There are no public leagues to join. [Create one now!]"

---

## 2. Create League Page Updates

### Layout Changes

- `← Back to Leagues` link sits **above** the `Create League` page title (not beside it)
- Buttons at the bottom: `[Create League]  [Cancel]` — Create League is fixed-width (wider than Cancel, not full-width). Cancel is on the right.

### Caption Selection — Dropdowns

Replace the individual caption chip picker with three dropdowns, one per scoring group. Each dropdown is mutually exclusive within its group.

**General Effect**
| Option | Captions |
|---|---|
| Combined | `GeneralEffectCombined` |
| Split | `GeneralEffect1`, `GeneralEffect2` |

**Visual**
| Option | Captions |
|---|---|
| Combined | `VisualCombined` |
| Partial Split | `Visual`, `Colorguard` |
| Full Split | `VisualAnalysis`, `VisualProficiency`, `Colorguard` |

*Partial Split: VA+VP scored as one combined score, plus CG separately. Full Split: VA, VP, and CG all separate.*

**Music**
| Option | Captions |
|---|---|
| Combined | `MusicCombined` |
| Partial Split | `Brass`, `Percussion` |
| Full Split | `Brass`, `MusicAnalysis`, `Percussion` |

On submit, the selected options are expanded to the corresponding `ComputedCaption[]` array as before.

### Max Players Stepper

A new **Max Players** stepper field, placed after Corps per Caption.

Constraints:
- Minimum: 4
- Maximum: `floor(activeSeason.corpsCount / corpsPerCaption)`

As Corps per Caption changes, the Max Players maximum recalculates and the stepper clamps to the new range if needed.

Corps per Caption is also now bounded:
- Maximum: `floor(activeSeason.corpsCount / 4)`

Both steppers use opacity-faded buttons (not hidden) when at their limits. No inline min/max labels. Each stepper has an ⓘ tooltip icon on hover explaining the rule in plain language.

Active season corps count is fetched on page load from a new `GET /api/seasons/active` endpoint.

### Unsaved Changes Warning

A confirmation dialog ("Discard changes?") appears when:
- The user clicks **Cancel**
- The user attempts to navigate away from the page (React Router `useBlocker`)

Dialog actions: **Keep editing** (dismiss) · **Discard** (red, proceed).

---

## 3. League Details — Non-Member View

When a user views a league they are not a member of:

- The **Join Draft Room** / **Open Draft** button is hidden
- A **← Browse** button always appears, navigating to `/leagues?tab=join`
- A **Join League** button appears only when all three conditions are met:
  1. User is not a member
  2. League has not reached `maxPlayers`
  3. Draft status is `NotStarted` or `Scheduled` (not Open, InProgress, or Completed)

Clicking **Join League** calls `POST /api/leagues/{id}/join` with the invite code from the URL query param (if present). On success, redirect to the league's home tab as a member.

All tabs (Home, Scores, Members, Picks, Info) remain visible and readable to non-members.

When `memberCount >= maxPlayers`, a "Full" badge appears next to the member count in the header.

Private leagues can be previewed by non-members when the request includes a valid invite code (`GET /api/leagues/{id}?code=XXX`).

---

## 4. Backend Changes Required

### New Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/leagues/browse` | All public leagues (requires auth) |
| `GET` | `/api/leagues/lookup?code=XXX` | Find a league by invite code, returns the league ID |
| `GET` | `/api/seasons/active` | Active published season with corps count |

### Modified Endpoints

| Method | Path | Change |
|---|---|---|
| `GET` | `/api/leagues/{id}` | Accept optional `?code=` query param; use it to authorize non-member preview of private leagues |

### Schema Changes

**`LeagueEntity` / `League` DTO:**
- Add `MaxPlayers` (int, non-nullable, minimum 4)
- `GET /api/leagues` response includes `UserRank` (int?) and `UserScore` (double?) computed from standings for the requesting user

**`CreateLeagueRequest`:**
- Add `maxPlayers` (int)

**`League` frontend type:**
- Add `maxPlayers: number`
- Add `memberCount` as non-optional (already partially present)
- Add `userRank?: number` and `userScore?: number` (populated on `GET /api/leagues`, null when no scores exist yet)

### Validation

On league creation, the server validates:
- `maxPlayers >= 4`
- `corpsPerCaption <= floor(activeSeason.corpsCount / 4)`
- `maxPlayers <= floor(activeSeason.corpsCount / corpsPerCaption)`

Frontend enforces the same constraints via the stepper bounds to prevent invalid submissions.
