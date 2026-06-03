# Draft Makeup Picks

**Date:** 2026-06-03

## Problem

When the commissioner skips a player's pick during the snake draft, that player permanently loses the pick. `SkipCurrentPickAsync` increments `CurrentPickNumber` without creating a `DraftPickEntity`, and the draft ends when `CurrentPickNumber >= totalPicks` regardless of how many actual picks were made. Skipped users end up with fewer corps than other league members.

## Design

### Core concept

A "skipped pick pool" tracks how many makeup picks each user is owed. After the main snake draft completes, if the pool is non-empty the draft remains `InProgress`. Users in the pool can each submit their makeup picks at any time, independently of each other (no turn order). Once the pool is empty the draft auto-completes.

Makeup picks are unskippable — the commissioner's Skip button is hidden during the makeup phase.

### Data model

No new columns or entities. The makeup queue is derived at runtime from existing data:

For each slot `i` in `0..min(CurrentPickNumber, mainTotalPicks)-1`, if no `DraftPickEntity` has `PickNumber == i`, then `GetCurrentDrafter(draftOrder, i)` was skipped and has a pending makeup pick. The gap in `DraftPicks` is the record of the skip.

```csharp
int mainTotalPicks = draftOrder.Length * league.DraftableCaptions.Length * league.CorpsPerCaption;
var completedPickNumbers = new HashSet<int>(league.DraftPicks.Select(p => p.PickNumber));
var makeupQueue = Enumerable
    .Range(0, Math.Min(league.CurrentPickNumber, mainTotalPicks))
    .Where(i => !completedPickNumbers.Contains(i))
    .Select(i => GetCurrentDrafter(draftOrder, i))
    .ToList(); // ordered by original skip position; one entry per skip event
```

`DraftPicks` are already loaded via `Include` in `SubmitPickAsync` and loaded separately in `PublishDraftStateAsync`, so this is pure LINQ over already-fetched data. Survives API restarts with no extra persistence.

### Pick numbering

`CurrentPickNumber` increments for normal picks and skips during the main draft. It stays frozen at `mainTotalPicks` once the main draft is done — makeup picks do not increment it.

Each makeup pick gets `PickNumber = gapSlot`, the index of the earliest unfilled slot in `0..mainTotalPicks-1` belonging to that user. This fills the gap in `DraftPicks`, which is what removes the user from the reconstructed makeup queue. The draft history panel thus shows makeup picks in their original slot positions rather than appended at the end.

### `SkipCurrentPickAsync` changes

- Append current drafter's userId to `MakeupPickQueueJson` is **removed** — the gap is the record.
- `CurrentPickNumber++` stays.
- Remove the draft-completion check (`DraftStatus = Completed`). A skip can never end the draft; a skipped-to-the-end draft stays `InProgress` until all makeup picks are made.

### `SubmitPickAsync` changes

Bifurcates on whether the current pick is in the main draft or the makeup phase:

```
inMakeupPhase = CurrentPickNumber >= mainTotalPicks
```

**Main draft phase** (unchanged except completion check):
- Enforce `GetCurrentDrafter(draftOrder, CurrentPickNumber) == user.Id`
- Record pick, `CurrentPickNumber++`
- Complete the draft only if `CurrentPickNumber >= mainTotalPicks && makeupQueue.Count == 0`

**Makeup phase:**
- Enforce `makeupQueue.Contains(user.Id.ToString())` (user has at least one pending makeup pick)
- Find `gapSlot`: the earliest index in `0..mainTotalPicks-1` with no `DraftPickEntity` and whose expected drafter is this user
- Record pick with `PickNumber = gapSlot`, `RoundNumber = gapSlot / draftOrder.Length`
- Do NOT increment `CurrentPickNumber`
- Re-evaluate `makeupQueue` after adding the pick (the filled gap removes one entry for the user); complete the draft when empty

The existing caption quota guard (`picksForCaption >= CorpsPerCaption`) and duplicate-pick guard remain unchanged — a makeup pick is still subject to all normal pick validation.

### MQTT payload additions

`PublishDraftStateAsync` adds two fields:

```json
{
  "MakeupQueue": ["userId-A", "userId-B", "userId-A"],
  "MainTotalPicks": 60
}
```

`MakeupQueue` is the reconstructed list in skip order (one entry per pending makeup slot). `MainTotalPicks` lets the frontend determine phase without recomputing it.

### Frontend (`DraftRoom.tsx`)

**Phase detection:**
```ts
const inMakeupPhase = status === 'InProgress' &&
  draftState.currentPickNumber >= draftState.mainTotalPicks;
```

**`isMyTurn` logic:**
```ts
const isMyTurn = status === 'InProgress' && (
  inMakeupPhase
    ? (draftState.makeupQueue ?? []).includes(user?.id ?? '')
    : draftState.currentDrafterId === user?.id
);
```

**Bar status area — makeup phase state:**
Replace the "On the Clock / Now Picking" display with a "Makeup Picks" area showing the list of users who still need to pick (derived from `makeupQueue`, deduplicated for display with a count per user). If the current user is in the list, show their Submit Pick button as normal.

**Skip button:**
Hidden when `inMakeupPhase`. The existing guard (`!isMyTurn && league.isCommissioner`) already prevents skipping when it's the user's own makeup turn, but explicitly suppressing the button in makeup phase ensures no makeup pick can be skipped regardless of turn state.

### TypeScript types

`DraftState` in `src/types/api.ts` gains two fields:
```ts
makeupQueue: string[];
mainTotalPicks: number;
```

## Out of scope

- Allowing the commissioner to force-complete a draft with outstanding makeup picks.
- Per-caption constraints on makeup picks (a makeup pick is a free pick subject only to normal quota guards).
- UI indication of which specific pick slot a makeup pick is filling.
