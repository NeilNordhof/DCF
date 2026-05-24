# Draft Open/Start Lifecycle Design

**Date:** 2026-05-24

## Overview

The draft lifecycle currently merges order initialization and pick activation into a single `StartDraft` action. This design splits them into two distinct phases — **open** (order determined, lobby ready, members can enter) and **start** (picks go live) — and ensures both the scheduled and manual paths converge on the same codepath.

The pick timer system is explicitly out of scope for this spec and will be designed separately.

---

## Lifecycle

```
NotStarted / Scheduled
       │
       ▼ open (order shuffled, status → Open, MQTT published)
     Open
       │
       ▼ start (status → InProgress, timers begin)
   InProgress
       │
       ▼ all picks made
   Completed
```

### `DraftStatus` enum

```csharp
public enum DraftStatus { NotStarted, Scheduled, Open, InProgress, Completed }
```

`Open` is inserted between `Scheduled` and `InProgress`. It is the authoritative signal that the draft has been initialized — no `DraftOrderJson` string checks are needed anywhere.

### Scheduled path

- At `DraftStartTime - 10 minutes`: auto-open fires (draft order shuffled, status → `Open`, MQTT state published)
- At `DraftStartTime`: auto-start fires (status → `InProgress`)

### Manual path

- Commissioner clicks **Open Draft**: draft order shuffled, status → `Open`, MQTT state published
- Commissioner clicks **Start Draft**: status → `InProgress`

---

## Service Layer — `DraftService`

### Open/start split

The current `StartDraftCoreAsync` private method is split:

**`OpenDraftCoreAsync(LeagueEntity league)`**
- Shuffles `league.Members` → writes `league.DraftOrderJson`
- Sets `league.DraftStatus = DraftStatus.Open`
- Saves to DB
- Calls `PublishDraftStateAsync` (retained)

**`StartDraftCoreAsync(LeagueEntity league)`**
- Sets `league.DraftStatus = DraftStatus.InProgress`
- Saves to DB
- Calls `PublishDraftStateAsync` (retained)

### Public method signatures

**`OpenDraftAsync(Guid leagueId)`** — scheduler path, no auth check
- Loads league + members
- Idempotent guard: returns early if `DraftStatus == Open` (handles double-fire on restart)
- Calls `OpenDraftCoreAsync`

**`OpenDraftAsync(Guid leagueId, string userSub)`** — commissioner path
- Validates: user exists, user is commissioner, `DraftStatus == NotStarted`
- Calls `OpenDraftCoreAsync`

**`StartDraftAsync(Guid leagueId)`** — scheduler path, no auth check
- Loads league + members
- Validates `DraftStatus == Open` — throws if not yet opened
- Calls `StartDraftCoreAsync`

**`StartDraftAsync(Guid leagueId, string userSub)`** — commissioner path (updated)
- Validates: user exists, user is commissioner, `DraftStatus == Open`
- Returns `InvalidOperationException("Draft must be opened before starting")` if status is not `Open`
- Calls `StartDraftCoreAsync`

---

## Scheduler — `DraftSchedulerService`

### Method rename

`ScheduleDraftStart(Guid leagueId, DateTimeOffset startTime)` → `ScheduleNext(Guid leagueId, DateTimeOffset startTime, bool isAlreadyOpened)`

The `_scheduled` dictionary retains its existing shape (`ConcurrentDictionary<Guid, CancellationTokenSource>`). One CTS per league cancels whichever phase is currently sleeping.

### Task structure

```
Task.Run:
  Phase 1 — open (skipped if isAlreadyOpened)
    delay = (startTime - 10min) - now
    if delay > 0 → await Task.Delay(delay, cts.Token)
    → call OpenDraftAsync(leagueId)

  Phase 2 — start
    delay = startTime - now
    if delay > 0 → await Task.Delay(delay, cts.Token)
    → call StartDraftAsync(leagueId)
```

If either delay is ≤ 0, that phase runs immediately with no wait. Cancellation at any point via `cts.Token` stops the task cleanly.

### Startup recovery (`ExecuteAsync`)

Queries all leagues where `(DraftStatus == Scheduled || DraftStatus == Open) && DraftStartTime != null`. For each:

```
isAlreadyOpened = league.DraftStatus == Open
ScheduleNext(league.Id, league.DraftStartTime, isAlreadyOpened)
```

This handles all restart scenarios:
- Server restarted before open window → Phase 1 waits, Phase 2 follows
- Server restarted during 10-minute window, open not yet run → Phase 1 fires immediately, Phase 2 waits
- Server restarted during 10-minute window, open already ran (`Open` status) → Phase 1 skipped, Phase 2 waits
- Server restarted after start time → both phases run immediately (zero-delay)

### Callers updated

`LeagueService.CreateAsync` calls `ScheduleNext(league.Id, draftStartTime.Value, isAlreadyOpened: false)` (was `ScheduleDraftStart`).

---

## API — `DraftController`

### New endpoint

```
POST /api/leagues/{leagueId}/draft/open
```

- Auth: requires authenticated user
- Calls `draftService.OpenDraftAsync(leagueId, GetSub())`
- Returns `204 No Content` on success
- Error mapping: `ArgumentException` → 404, `UnauthorizedAccessException` → 403, `InvalidOperationException` → 400

### Updated endpoint

```
POST /api/leagues/{leagueId}/draft/start
```

- Now returns `400 "Draft must be opened before starting"` if `DraftStatus != Open` (enforced in the service)

---

## MQTT

### Retained messages

`IMqttPublisherService.PublishAsync` gains a `bool retain = false` parameter:

```csharp
Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default);
```

`MqttPublisherService` passes `retain` to `.WithRetain(retain)` on the `MqttApplicationMessageBuilder`.

All calls via `PublishDraftStateAsync` pass `retain: true`. The existing scores topic publish (`dcf/scores/updated`) remains non-retained.

### Payload — `DraftOrder` field added

`PublishDraftStateAsync` adds a `DraftOrder` field to the MQTT payload:

```json
{
  "status": "Open",
  "draftStartTime": "...",
  "currentPickNumber": 0,
  "currentDrafterId": "...",
  "draftOrder": [
    { "userId": "...", "displayName": "..." }
  ],
  "members": [...],
  "picks": [...]
}
```

`DraftOrder` is computed from `DraftOrderJson` by joining against the members list. It is an empty array when status is `NotStarted` or `Scheduled` (i.e. before open).

---

## Frontend

### `LeagueDetail.tsx` — Join Draft Room button

The existing `<Link to="/leagues/{id}/draft">Draft Room</Link>` becomes a button that is enabled only when the draft is open. `LeagueDetail` already subscribes to the draft MQTT topic (`useMqtt`) but discards the result — it now consumes `draftState.status` to reactively gate the button without requiring a REST re-fetch.

- `draftState.status === 'NotStarted' || 'Scheduled'` (or `draftState` is null): button is disabled / not shown
- `draftState.status === 'Open'`, `'InProgress'`, or `'Completed'`: button is enabled and navigates to `/leagues/{id}/draft`

Using the retained MQTT message means the button state is correct immediately on page load and updates in real time when the commissioner opens the draft.

### `DraftRoom.tsx` — entry guard

As a safety net against direct URL access, `DraftRoom` checks `draftState.status` once the MQTT connection settles. If status is `NotStarted` or `Scheduled`, it redirects to the league detail page.

### `DraftRoom.tsx` — lobby view

When `draftState.status === 'Open'`, the lobby shows:
- The full pick order list (position + display name) from `draftState.draftOrder`
- Countdown to `draftStartTime` if set

### `DraftRoom.tsx` — commissioner buttons

| `draftStatus` | Button shown |
|---|---|
| `NotStarted` | **Open Draft** |
| `Scheduled` | Neither (auto-handled) |
| `Open` | **Start Draft** |

### API client

`src/api/client.ts` gains `openDraft(leagueId: string): Promise<void>` calling `POST /api/leagues/{id}/draft/open`.

---

## Type changes

**`src/types/api.ts`:**
- `DraftState.status` union gains `'Open'`
- `DraftState` gains `draftOrder: { userId: string; displayName: string }[]`

**`DCF.Data/Models/DraftStatus.cs`:**
- `Open` added between `Scheduled` and `InProgress`
