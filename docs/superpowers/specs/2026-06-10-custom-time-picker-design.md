# Custom Time Picker — Design Spec

**Issue:** FD-4  
**Date:** 2026-06-10  

## Problem

Browser-native `datetime-local` and `time` inputs render inconsistently across browsers and cannot be styled to match the app's dark theme. This spec covers replacing all native time inputs with a single custom `TimePicker` component.

## Scope

Three files are affected:

| File | Current input | Change |
|---|---|---|
| `LeagueCreate.tsx` | `type="datetime-local"` (draft start time) | Split state → `<input type="date">` + `<TimePicker>` |
| `LeagueDetail.tsx` | `type="datetime-local"` (draft start time) | Split state → `<input type="date">` + `<TimePicker>` |
| `SeasonDetail.tsx` | Two `type="time"` inputs (show start, scores announced) | Direct swap to `<TimePicker>` |

`type="date"` inputs are not replaced — they are less broken cross-browser and out of scope.

## Component: `src/components/TimePicker.tsx`

### Props

```ts
interface TimePickerProps {
  value: string;                        // 24-hour "HH:MM", or "" for unset
  onChange: (value: string) => void;    // emits 24-hour "HH:MM", or "" when cleared
  required?: boolean;                   // default false; if true, no empty/clear state
  style?: CSSProperties;                // applied to outer container for sizing
}
```

### Value contract

- Input and output are always 24-hour `HH:MM` strings.
- This matches the existing `type="time"` interface — `buildDateTime` in `SeasonDetail` and `datetimeLocalToIso` in `LeagueCreate`/`LeagueDetail` need no changes.
- Callers never need to validate the emitted string; by construction it is always valid.

### Internal state

```ts
hour: number       // 1–12 (display / 12-hour)
minute: number     // 0 | 5 | 10 | … | 55
ampm: 'AM' | 'PM'
typedHour: string  // staging string while user is mid-type in hour field
typedMinute: string
```

Numbers are used instead of strings so the output is *constructed* from valid state rather than *parsed* from a potentially invalid string. This makes malformed output structurally impossible.

### Visual structure

```
[ ▲ ]  [ ▲ ]
[ 07 ] [ 30 ] [ PM ▾ ]
[ ▼ ]  [ ▼ ]
```

Rendered as a single bordered box using the app's CSS variables (`--surface`, `--border-input`, `--accent`, `--text-heading`). Hour and minute columns are separated by a colon divider. AM/PM is a styled `<select>`.

### Behaviour

**Arrow stepping**
- Hour: wraps 12 → 1 (up) and 1 → 12 (down).
- Minute: steps in increments of 5, wraps 55 → 0 (up) and 0 → 55 (down).
- Minute wrap carries into hour: e.g. 7:55 PM → minute up → 8:00 PM.

**Direct typing**
- Hour/minute fields are editable `<input>` elements.
- While focused, a staging string (`typedHour`/`typedMinute`) is shown and updated on each keystroke; only digits are accepted.
- On blur: parse the staged string, clamp to valid range (hour 1–12, minute 0–59 then round to nearest 5), commit to numeric state. If unparseable, revert to the previous valid value.
- `onChange` is called only when numeric state is committed (not on every keystroke).

**AM/PM**
- A `<select>` with `AM` / `PM` options.
- Selecting AM/PM changes the period only; the displayed hour number does not change (7 AM → select PM → 7 PM).

**Empty state** (`required={false}`)
- Component initialises showing `--:-- --` when `value=""`.
- First arrow click initialises to `12:00 PM` and calls `onChange`.
- User can clear by deleting all content in the hour field on blur, which emits `""`.

**Conversion**
- On mount and when `value` prop changes: parse 24-hour `HH:MM` → set `hour`, `minute`, `ampm`.
- On any state change: construct 24-hour `HH:MM` string and call `onChange`.

## Integration changes

### `LeagueCreate.tsx`

Split `draftStartTime: string` (combined `YYYY-MM-DDTHH:MM`) into two state variables:

```ts
const [draftStartDate, setDraftStartDate] = useState('');
const [draftStartTime, setDraftStartTime] = useState('');
```

Render:
```tsx
<input type="date" value={draftStartDate} onChange={e => setDraftStartDate(e.target.value)} style={inputStyle} />
<TimePicker value={draftStartTime} onChange={v => setDraftStartTime(v)} style={{ marginTop: 6 }} />
```

Submit (no change to `datetimeLocalToIso`):
```ts
draftStartTime: (draftStartDate && draftStartTime)
  ? datetimeLocalToIso(`${draftStartDate}T${draftStartTime}`)
  : null,
```

### `LeagueDetail.tsx`

Same state split as `LeagueCreate`. The existing `isoToDatetimeLocal` helper already produces `YYYY-MM-DDTHH:MM` — split on `T` when populating initial state from the league object. When `draftStartTime` is `null`, both vars initialise to `""`:

```ts
if (league.draftStartTime) {
  const combined = isoToDatetimeLocal(league.draftStartTime);
  setDraftStartDate(combined.split('T')[0]);
  setDraftStartTime(combined.split('T')[1]);
} else {
  setDraftStartDate('');
  setDraftStartTime('');
}
```

Submit unchanged (recombine before `datetimeLocalToIso`).

### `SeasonDetail.tsx`

Direct swap — no state changes needed. `showStartTime` and `showScoresTime` are already `HH:MM` strings:

```tsx
// Before
<input type="time" value={showStartTime} onChange={e => setShowStartTime(e.target.value)} style={{ ...inputStyle, flex: 1 }} />

// After
<TimePicker value={showStartTime} onChange={setShowStartTime} required style={{ flex: 1 }} />
```

Same swap for `showScoresTime`, `editShow.startTime`, and `editShow.scoresTime`.

## What does not change

- `datetimeLocalToIso` in `LeagueCreate` and `LeagueDetail` — unchanged.
- `buildDateTime` in `SeasonDetail` — unchanged.
- `type="date"` inputs in `SeasonDetail` and `Admin` — out of scope.
- Any backend API or DTO — no changes.
