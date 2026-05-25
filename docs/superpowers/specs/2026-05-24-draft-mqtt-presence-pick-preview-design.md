# Draft Room — MQTT Presence & Pick Preview Design

## Overview

Two real-time features are added to the draft experience using MQTT as the live event bus:

1. **Presence tracking** — the server tracks which league members are connected to the Draft Room and includes the list in the retained draft state. The lobby shows who is online; disconnect events are immediately visible to all participants.
2. **Pick preview** — when the current drafter selects a cell in the pick grid, all other participants see the tentative selection in real time before it is submitted. This is a client-to-client MQTT signal; the server does not process it.

Both features are paired with a full **Draft Room UI redesign** that replaces the existing dropdown-based pick form with a corps × caption grid, following the visual design spec in `docs/superpowers/specs/2026-05-24-site-design.md`.

---

## Goals

- Members in the Draft Room lobby can see who else has joined.
- During the draft, participants can see if a member disconnects unexpectedly.
- When the current drafter selects a cell, other participants see the tentative choice animate in real time.
- The pick grid replaces the two dropdowns — one click selects, a Submit bar confirms.

---

## MQTT Topics

| Topic | Direction | QoS | Retained | Purpose |
|---|---|---|---|---|
| `dcf/leagues/{id}/draft` | server → clients | 1 | yes | Authoritative draft state. Gains `onlineUserIds` field. |
| `dcf/leagues/{id}/draft/presence` | client → server | 1 | no | Join / leave events. LWT fires on unexpected disconnect. |
| `dcf/leagues/{id}/draft/pick` | client → clients | 0 | no | Live cell selection preview. Server does not subscribe. |

---

## Backend Design

### 1. `MqttService` (rename from `MqttPublisherService`)

The class and interface are renamed to reflect that the service now both publishes and subscribes:

- `MqttPublisherService` → `MqttService`
- `IMqttPublisherService` → `IMqttService`

`PublishAsync` signature is unchanged; all existing callers update the injected type name only.

On `StartAsync`, after the MQTT connection is established, the service subscribes to `dcf/leagues/+/draft/presence` (QoS 1) and registers `ApplicationMessageReceivedAsync`. The handler:

1. Extracts `leagueId` from the topic segment between `leagues/` and `/draft/presence`.
2. Deserialises the payload to `{userId: Guid, status: "online" | "offline"}`.
3. Parses `userId` as a `Guid`.
4. Calls `IPresenceService.HandlePresenceAsync(leagueId, userId, online: status == "online")`.

Malformed topics or payloads are logged at `Warning` level and discarded.

### 2. `IPresenceService` / `PresenceService`

Registered as singleton. Maintains an in-memory presence map.

```
Interface: IPresenceService
  Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online)
  IReadOnlyCollection<Guid> GetOnline(Guid leagueId)
```

Internal state: `ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, bool>>` keyed by `leagueId → userId`.

`HandlePresenceAsync`:
- Adds or removes the userId from the inner dictionary.
- Creates a scoped `IDraftService` via `IServiceScopeFactory`.
- Calls `draftService.PublishStateAsync(leagueId)` to re-publish the retained draft state with updated presence.

`GetOnline(leagueId)` returns the current key set of the inner dictionary for that league, or an empty collection if the league has no entry.

### 3. `IDraftService` / `DraftService` changes

**New interface method:**
```
Task PublishStateAsync(Guid leagueId)
```

Fetches the league entity from the database and calls the existing internal `PublishDraftStateAsync(LeagueEntity)`. Members and picks are queried inside the internal method, so no includes are needed here. Used by `PresenceService` to re-publish state when presence changes without an active operation in flight.

**`PublishDraftStateAsync` payload addition:**

`onlineUserIds` is added to the anonymous payload object:

```
OnlineUserIds = presenceService.GetOnline(league.Id).Select(id => id.ToString()).ToArray()
```

`IPresenceService` is injected into `DraftService`.

### 4. Draft state payload schema

The `dcf/leagues/{id}/draft` retained message adds one field:

```json
{
  "status": "Open",
  "draftStartTime": "...",
  "currentPickNumber": 0,
  "currentDrafterId": null,
  "onlineUserIds": ["guid1", "guid2"],
  "draftOrder": [...],
  "members": [...],
  "picks": [...]
}
```

`onlineUserIds` is always present (empty array when no one is connected).

### 5. Registration (`Program.cs`)

```
builder.Services.AddSingleton<IPresenceService, PresenceService>();
builder.Services.AddSingleton<IMqttService, MqttService>();
builder.Services.AddHostedService(sp => (MqttService)sp.GetRequiredService<IMqttService>());
```

---

## Frontend Design

### 1. `types/api.ts`

`DraftState` gains:
```typescript
onlineUserIds: string[];
```

New type for pick preview messages:
```typescript
export interface PickPreview {
  userId: string;
  corpsId: string;
  caption: string;
}
```

### 2. `useDraftPresence` hook

Location: `src/mqtt/useDraftPresence.ts`

Creates a **dedicated** MQTT connection (separate from the `useMqtt` subscription connections) with LWT configured. The LWT ensures Mosquitto publishes an `offline` presence event if the WebSocket connection drops without a clean disconnect.

```
useDraftPresence(leagueId: string, userId: string | undefined)
  → { publishPickPreview: (corpsId: string, caption: string) => void }
```

Behaviour:
- On mount: connects to `VITE_MQTT_URL` with LWT `{userId, status: "offline"}` targeting the presence topic.
- On `connect` event: publishes `{userId, status: "online"}` to `dcf/leagues/{id}/draft/presence` (QoS 1).
- On unmount: publishes `{userId, status: "offline"}` (QoS 1) then calls `client.end()`.
- `publishPickPreview`: publishes `{userId, corpsId, caption}` to `dcf/leagues/{id}/draft/pick` (QoS 0) if the client is connected. Stable reference via `useCallback` reading from a `clientRef`.
- Does nothing if `userId` is undefined (unauthenticated state guard).

### 3. `DraftRoom.tsx` — full redesign

The existing page is rewritten. It retains the MQTT subscription to `dcf/leagues/{id}/draft` for authoritative state, and adds a second `useMqtt` subscription for pick previews.

**Additional subscriptions:**
```typescript
const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
const pickPreview = useMqtt<PickPreview>(`dcf/leagues/${id}/draft/pick`);
const { publishPickPreview } = useDraftPresence(id!, user?.id);
```

**Redirect guard** (unchanged):
Redirects to `/leagues/${id}` when the REST `league.draftStatus` is `NotStarted` or `Scheduled`.

---

#### Layout structure

```
┌─────────────────── Top bar (full width) ───────────────────┐
│                                                              │
│  ┌──────────────── Pick grid ──────────┐  ┌── Side panel ─┐ │
│  │  (left, scrollable)                 │  │  (right, tabs) │ │
│  │                                     │  │               │ │
│  └─────────────────────────────────────┘  └───────────────┘ │
│  ┌──────────────── Submit bar ─────────┐                     │
└──┴─────────────────────────────────────┴────────────────────┘
```

---

#### Top bar

| State | Background | Left content | Right content |
|---|---|---|---|
| Open (lobby) | `linear-gradient(90deg, #0f1a0f, #101810)`, `border-bottom: 2px solid --green-border` | "DRAFT BEGINS IN" label (green, uppercase) + `HH:MM:SS` countdown (26px, weight 900, `--text-h`; colons in `--green`) | Scheduled datetime + league name; commissioner "Start Early" ghost button (`border: 1px solid --green-border`, `color: --green`) |
| InProgress — your turn | `linear-gradient(90deg, #2e1065, #1a1535)`, `border-bottom: 2px solid --accent` | "ON THE CLOCK" label (`--accent`, uppercase) + your display name (15px, weight 800, `--text-h`) | "Round N · Pick N" + league name |
| InProgress — other's turn | Same purple gradient | "NOW PICKING" label (`--accent`) + drafter display name | Same |
| Completed | `bg: --surface`, `border-bottom: 1px solid --border` | "DRAFT COMPLETE" (`--text-muted`) | League name |

---

#### Pick grid

Rendered for all states. Interactivity gated by `isMyTurn`.

**Structure:** rows = corps (from the `corps` fetch), columns = `league.draftableCaptions`. Row labels right-aligned (10px, weight 600, `--text-h`). Column headers: caption names, 8–9px uppercase, `--text-muted`.

**Cell size:** 44×44px. Leagues with ≤ 3 captions widen cells up to 88×44px. Grid scrolls vertically for long corps lists.

**Cell states:**

| State | Condition | Visual |
|---|---|---|
| Available | Not taken, not selected, not previewed | `bg: --green-bg`, `border: 1px solid --green-border`, green dot `●` centred |
| Taken | In `draftState.picks` | `bg: #12141a`, `border: 1px solid --border-subtle`, `—` in `--border` colour, `cursor: not-allowed` |
| Selected | My current local selection (my turn only) | `bg: --accent-bg`, `border: 2px solid --accent`, `★` centred, `box-shadow: 0 0 10px --accent-bg` |
| Previewed | `pickPreview` matches this cell, it's another user's turn, the drafter is the current drafter, and the combination is not taken | `bg: #1e1430`, `border: 1px dashed --accent-border`, 8px drafter display name below `●` in `--text-muted` |

Previewed state is only rendered when `pickPreview.userId === draftState.currentDrafterId` and the combination appears in the available set (prevents stale previews from prior picks landing on already-taken cells).

On cell click (my turn only): update local `selectedCell` state, call `publishPickPreview(corpsId, caption)`.

Grid is `pointer-events: none` when not my turn, or when status is Open/Completed.

**Locked lobby overlay:** when `draftState.status === 'Open'`, the grid renders at `opacity: 0.45` with `pointer-events: none` and a label above: "Pick board locks until the draft begins".

---

#### Submit bar

Visible when `draftState.status` is `Open` or `InProgress`. Hidden when `Completed`.

```
bg: --surface | border: 1px solid --accent-border | border-radius: 6px
[ Selected: Corps · Caption ]    [ SUBMIT PICK ▶ ]   [ Skip (commissioner) ]
```

- Left: "SELECTED" section label + selected corps name + `·` + caption name. Shows `—` placeholders when nothing is selected or when it is not the current user's turn.
- Centre/right: "SUBMIT PICK" primary button. Enabled only when it is the current user's turn, a cell is selected, and the submit is not already in flight. Disabled otherwise (grey, `cursor: not-allowed`).
- Commissioner only (InProgress, not the commissioner's turn): small ghost "SKIP PICK" button to the right of Submit. Calls `api.skipPick(id)` (REST, unchanged).

On submit: calls `api.submitPick(id, corpsId, caption)` (REST, unchanged). Does **not** call `publishPickPreview` on submit — the pick preview was already published on cell selection. Clears `selectedCell` on completion.

---

#### Side panel

Tabbed: `Draft Order | Picks`. Tabs follow the design system pattern (`color: --accent`, `border-bottom: 2px solid --accent` for active).

**Draft Order tab:**

*Lobby (Open status):* Renders the full `draftState.draftOrder` as an ordered list. Each row: pick-number circle, display name. Online status indicator: small filled circle (`●`, 6px, `--green`) immediately before the display name when the member's userId is in `onlineUserIds`; muted circle (`○`, `--text-faint`) when offline. Section label above the list: "DRAFT ORDER". Subtitle below the list if not all members are online: "N of M members online".

*InProgress / Completed:* Ordered list rendered in three sections:
- Completed picks (above current): faded, pick-number circle + drafter name + "Corps (Caption)" label.
- Current pick (if InProgress): purple highlight `bg: --accent-bg`, `border: 1px solid --accent-border`, pick-number circle, drafter name.
- Upcoming picks: "UP NEXT" section label, remaining picks in order at reduced opacity.

**Picks tab:**

Player switcher: pill buttons showing first names. Active pill: `bg: --accent`, `color: #0d0f14`. Inactive: `bg: --surface`, `border: 1px solid --border`.

Below: sections grouped by caption. Caption heading: name uppercase + count badge (`N / M` format, purple-tinted bg when N > 0). Filled picks: card with `#N` pick-number circle in `--accent` + corps name + "Pick #N overall" subtitle. Empty slots: dashed border, `"Empty"` italic in `--text-faint`.

---

### 4. `submitPick` in `api/client.ts`

No changes — picks continue through REST.

---

## Data Flow Summaries

### Presence join

```
DraftRoom mounts
  → useDraftPresence connects MQTT with LWT {userId, status:"offline"}
  → client.on('connect') → publish {userId, status:"online"} to presence topic
  → MqttService receives → PresenceService.HandlePresenceAsync(leagueId, userId, online:true)
  → PresenceService creates scope → DraftService.PublishStateAsync(leagueId)
  → DraftService fetches league, builds payload with onlineUserIds → MQTT retain publish
  → All clients receive updated DraftState with new onlineUserIds
```

### Presence leave (intentional)

```
DraftRoom unmounts
  → useDraftPresence cleanup: publish {userId, status:"offline"} → client.end()
  → Same flow as join with online:false
```

### Presence leave (unexpected disconnect)

```
Browser crash / network drop
  → Mosquitto TCP keepalive expires
  → Broker publishes LWT {userId, status:"offline"} to presence topic
  → Same server flow as intentional leave
```

### Pick preview

```
Drafter clicks a cell
  → selectedCell state updates → grid re-renders with Selected cell
  → publishPickPreview(corpsId, caption) → publish to dcf/leagues/{id}/draft/pick (QoS 0)
  → Other clients receive PickPreview via useMqtt subscription
  → Grid renders Previewed state for that cell
  → Drafter clicks Submit → api.submitPick(...) REST call
  → Server saves, publishes retained DraftState with new pick
  → All clients receive updated state → Previewed cell becomes Taken, selectedCell clears
```

---

## Error Handling

**Presence MQTT handler (server):** malformed topic or payload is logged and silently discarded. `PublishStateAsync` failure is caught and logged; presence map is still updated.

**`useDraftPresence` hook:** MQTT client errors are silently ignored (connection instability should not break the pick form). `publishPickPreview` no-ops if the client is not connected.

**Pick grid / submit bar:** no changes to REST error handling.

---

## Testing

**`PresenceServiceTests`** (new):
- `HandlePresenceAsync_Online_AddsToSet`
- `HandlePresenceAsync_Offline_RemovesFromSet`
- `HandlePresenceAsync_Offline_Unknown_NoOp` (userId not in map — no exception)
- `GetOnline_ReturnsCorrectSet`
- `HandlePresenceAsync_TriggersDraftStatePublish` (verifies `IDraftService.PublishStateAsync` is called)

**`DraftServiceTests`** (extend existing):
- `PublishStateAsync_NotFound_DoesNotThrow`
- `PublishStateAsync_IncludesOnlineUserIds` (injects fake `IPresenceService`, asserts MQTT payload includes the IDs)

---

## File Map

| File | Change |
|---|---|
| `DCF.Api/Services/IMqttPublisherService.cs` | Rename to `IMqttService.cs`, rename interface |
| `DCF.Api/Services/MqttPublisherService.cs` | Rename to `MqttService.cs`, rename class, add subscription + presence handler in `StartAsync` |
| `DCF.Api/Services/IPresenceService.cs` | New |
| `DCF.Api/Services/PresenceService.cs` | New |
| `DCF.Api/Services/IDraftService.cs` | Add `Task PublishStateAsync(Guid leagueId)` |
| `DCF.Api/Services/DraftService.cs` | Inject `IPresenceService`, add public `PublishStateAsync`, add `onlineUserIds` to payload |
| `DCF.Api/Program.cs` | Register `IPresenceService`, update `MqttService` registration |
| `DCF.Tests/Services/PresenceServiceTests.cs` | New |
| `DCF.Tests/Services/DraftServiceTests.cs` | Extend with `PublishStateAsync` and `onlineUserIds` tests |
| `DCF.Web/src/types/api.ts` | Add `onlineUserIds` to `DraftState`, add `PickPreview` type |
| `DCF.Web/src/mqtt/useDraftPresence.ts` | New hook |
| `DCF.Web/src/pages/DraftRoom.tsx` | Full rewrite — pick grid, top bar, side panel, presence + preview integration |

---

## Out of Scope

- MQTT authentication (username/password or TLS) — presence and pick preview are low-risk operations; picks remain on authenticated REST.
- Persisting presence to the database — in-memory only; presence resets on server restart (acceptable: users reconnect).
- Mobile-responsive Draft Room layout — follow-up per the site design spec.
- Disconnect timeout / heartbeat mechanism — Mosquitto's TCP keepalive is sufficient; LWT fires within one keepalive interval (default 60s in most clients).
