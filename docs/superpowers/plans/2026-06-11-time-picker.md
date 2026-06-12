# Custom Time Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all native `type="time"` and `type="datetime-local"` inputs with a styled `TimePicker` component that matches the app's dark theme.

**Architecture:** A single `TimePicker` component in `src/components/` holds 12-hour numeric state and emits 24-hour `HH:MM` strings — the same interface as `type="time"`. `LeagueCreate` and `LeagueDetail` split their combined `datetime-local` string state into separate date + time state vars. `SeasonDetail` is a direct swap.

**Tech Stack:** React 19, TypeScript, inline styles using app CSS variables (`--surface`, `--border-input`, `--accent`, `--text-heading`, `--text-faint`, `--bg`, `--border`)

**Spec:** `docs/superpowers/specs/2026-06-10-custom-time-picker-design.md`

---

## File Map

| Action | File |
|---|---|
| **Create** | `DCF.Web/src/components/TimePicker.tsx` |
| **Modify** | `DCF.Web/src/pages/LeagueCreate.tsx` |
| **Modify** | `DCF.Web/src/pages/LeagueDetail.tsx` |
| **Modify** | `DCF.Web/src/pages/SeasonDetail.tsx` |

---

## Task 1: Create `TimePicker` component

**Files:**
- Create: `DCF.Web/src/components/TimePicker.tsx`

- [ ] **Step 1: Create the file with helpers and full component**

`DCF.Web/src/components/TimePicker.tsx`:

```tsx
import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';

interface TimePickerProps {
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  style?: CSSProperties;
}

function to12(h24: number): { hour: number; ampm: 'AM' | 'PM' }
{
  if (h24 === 0) return { hour: 12, ampm: 'AM' };
  if (h24 < 12) return { hour: h24, ampm: 'AM' };
  if (h24 === 12) return { hour: 12, ampm: 'PM' };
  return { hour: h24 - 12, ampm: 'PM' };
}

function to24(hour: number, ampm: 'AM' | 'PM'): number
{
  if (ampm === 'AM') return hour === 12 ? 0 : hour;
  return hour === 12 ? 12 : hour + 12;
}

function parseValue(v: string): { hour: number; minute: number; ampm: 'AM' | 'PM' } | null
{
  if (!v) return null;
  const parts = v.split(':');
  if (parts.length !== 2) return null;
  const h24 = parseInt(parts[0], 10);
  const m = parseInt(parts[1], 10);
  if (isNaN(h24) || isNaN(m)) return null;
  return { ...to12(h24), minute: m };
}

export function TimePicker({ value, onChange, required = false, style }: TimePickerProps)
{
  const initial = parseValue(value);
  const [hour, setHour] = useState(initial?.hour ?? 12);
  const [minute, setMinute] = useState(initial?.minute ?? 0);
  const [ampm, setAmpm] = useState<'AM' | 'PM'>(initial?.ampm ?? 'PM');
  const [isEmpty, setIsEmpty] = useState(!value);
  const [typedHour, setTypedHour] = useState('');
  const [typedMinute, setTypedMinute] = useState('');
  const [focusedField, setFocusedField] = useState<'hour' | 'minute' | null>(null);

  useEffect(() => {
    if (!value) {
      setIsEmpty(true);
      return;
    }
    const parsed = parseValue(value);
    if (!parsed) return;
    setHour(parsed.hour);
    setMinute(parsed.minute);
    setAmpm(parsed.ampm);
    setIsEmpty(false);
  }, [value]);

  function emit(h: number, m: number, ap: 'AM' | 'PM')
  {
    const h24 = to24(h, ap);
    onChange(`${String(h24).padStart(2, '0')}:${String(m).padStart(2, '0')}`);
  }

  function initDefault()
  {
    setHour(12);
    setMinute(0);
    setAmpm('PM');
    setIsEmpty(false);
    emit(12, 0, 'PM');
  }

  function stepHour(dir: 1 | -1)
  {
    if (isEmpty)
    {
      initDefault();
      return;
    }
    const next = dir === 1 ? (hour === 12 ? 1 : hour + 1) : (hour === 1 ? 12 : hour - 1);
    setHour(next);
    emit(next, minute, ampm);
  }

  function stepMinute(dir: 1 | -1)
  {
    if (isEmpty)
    {
      initDefault();
      return;
    }
    const newMin = minute + dir * 5;

    if (newMin >= 60)
    {
      const newHour = hour === 12 ? 1 : hour + 1;
      setMinute(0);
      setHour(newHour);
      emit(newHour, 0, ampm);
    }
    else if (newMin < 0)
    {
      const newHour = hour === 1 ? 12 : hour - 1;
      setMinute(55);
      setHour(newHour);
      emit(newHour, 55, ampm);
    }
    else
    {
      setMinute(newMin);
      emit(hour, newMin, ampm);
    }
  }

  function commitHour(typed: string)
  {
    setTypedHour('');

    if (typed === '')
    {
      if (!required)
      {
        setIsEmpty(true);
        onChange('');
      }
      return;
    }

    const parsed = parseInt(typed, 10);
    if (isNaN(parsed)) return;
    const clamped = Math.max(1, Math.min(12, parsed));
    setHour(clamped);
    setIsEmpty(false);
    emit(clamped, minute, ampm);
  }

  function commitMinute(typed: string)
  {
    setTypedMinute('');

    if (typed === '') return;
    const parsed = parseInt(typed, 10);
    if (isNaN(parsed)) return;
    const clamped = Math.max(0, Math.min(59, parsed));
    const rounded = Math.round(clamped / 5) * 5 % 60;
    setMinute(rounded);
    emit(hour, rounded, ampm);
  }

  const arrowStyle: CSSProperties = {
    width: 28, height: 20, borderRadius: 3, fontSize: 9, fontWeight: 700,
    background: 'var(--surface)', border: '1px solid var(--border)',
    color: 'var(--text-heading)', cursor: 'pointer',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    lineHeight: 1, padding: 0,
  };

  const fieldStyle: CSSProperties = {
    width: 30, height: 26, textAlign: 'center', fontSize: 14, fontWeight: 700,
    background: 'transparent', border: 'none', outline: 'none',
    color: isEmpty && focusedField === null ? 'var(--text-faint)' : 'var(--text-heading)',
    padding: 0,
  };

  const hourDisplay = focusedField === 'hour'
    ? typedHour
    : isEmpty
      ? '--'
      : String(hour).padStart(2, '0');

  const minuteDisplay = focusedField === 'minute'
    ? typedMinute
    : isEmpty
      ? '--'
      : String(minute).padStart(2, '0');

  return (
    <div
      style={{
        display: 'inline-flex', alignItems: 'center',
        background: 'var(--bg)', border: '1px solid var(--border-input)',
        borderRadius: 5, padding: '4px 10px', gap: 2,
        ...style,
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
        <button type="button" style={arrowStyle} onClick={() => stepHour(1)}>▲</button>
        <input
          value={hourDisplay}
          style={fieldStyle}
          onFocus={() => {
            setFocusedField('hour');
            setTypedHour(isEmpty ? '' : String(hour).padStart(2, '0'));
          }}
          onBlur={() => {
            setFocusedField(null);
            commitHour(typedHour);
          }}
          onChange={e => {
            const v = e.target.value.replace(/\D/g, '');
            if (v.length <= 2) setTypedHour(v);
          }}
        />
        <button type="button" style={arrowStyle} onClick={() => stepHour(-1)}>▼</button>
      </div>

      <span style={{ fontSize: 16, fontWeight: 700, color: isEmpty ? 'var(--text-faint)' : 'var(--text-heading)', paddingBottom: 2 }}>:</span>

      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
        <button type="button" style={arrowStyle} onClick={() => stepMinute(1)}>▲</button>
        <input
          value={minuteDisplay}
          style={fieldStyle}
          onFocus={() => {
            setFocusedField('minute');
            setTypedMinute(isEmpty ? '' : String(minute).padStart(2, '0'));
          }}
          onBlur={() => {
            setFocusedField(null);
            commitMinute(typedMinute);
          }}
          onChange={e => {
            const v = e.target.value.replace(/\D/g, '');
            if (v.length <= 2) setTypedMinute(v);
          }}
        />
        <button type="button" style={arrowStyle} onClick={() => stepMinute(-1)}>▼</button>
      </div>

      <select
        value={isEmpty ? '' : ampm}
        disabled={isEmpty}
        onChange={e => {
          const ap = e.target.value as 'AM' | 'PM';
          setAmpm(ap);
          emit(hour, minute, ap);
        }}
        style={{
          marginLeft: 6, background: 'transparent', border: 'none', outline: 'none',
          color: isEmpty ? 'var(--text-faint)' : 'var(--text-heading)',
          fontSize: 11, fontWeight: 700,
          cursor: isEmpty ? 'not-allowed' : 'pointer',
        }}
      >
        {isEmpty && <option value="">--</option>}
        <option value="AM">AM</option>
        <option value="PM">PM</option>
      </select>
    </div>
  );
}
```

- [ ] **Step 2: Verify TypeScript compilation**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds with no TypeScript errors in `TimePicker.tsx`.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/components/TimePicker.tsx
git commit -m "feat: add TimePicker component (FD-4)"
```

---

## Task 2: Update `LeagueCreate.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

**Context:** Currently `draftStartTime` holds a combined `YYYY-MM-DDTHH:MM` string fed into a single `datetime-local` input. The `datetimeLocalToIso` function appends `:00` + timezone offset — it stays unchanged.

- [ ] **Step 1: Add the TimePicker import**

In `LeagueCreate.tsx`, add the import after the existing imports:

```tsx
import { TimePicker } from '../components/TimePicker';
```

- [ ] **Step 2: Split the combined state into two vars**

Replace (line ~160):
```tsx
  const [draftStartTime, setDraftStartTime] = useState('');
```

With:
```tsx
  const [draftStartDate, setDraftStartDate] = useState('');
  const [draftStartTime, setDraftStartTime] = useState('');
```

- [ ] **Step 3: Update the submit handler to recombine**

Find in `handleSubmit` (line ~206):
```tsx
        draftStartTime: draftStartTime ? datetimeLocalToIso(draftStartTime) : null,
```

Replace with:
```tsx
        draftStartTime: (draftStartDate && draftStartTime) ? datetimeLocalToIso(`${draftStartDate}T${draftStartTime}`) : null,
```

- [ ] **Step 4: Replace the datetime-local input with date + TimePicker**

Find the Draft Start section (line ~363–373):
```tsx
        <div>
          <div style={labelStyle}>
            Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(optional)</span>
          </div>
          <input
            type="datetime-local"
            value={draftStartTime}
            onChange={e => { setDraftStartTime(e.target.value); setIsDirty(true); }}
            style={inputStyle}
          />
        </div>
```

Replace with:
```tsx
        <div>
          <div style={labelStyle}>
            Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(optional)</span>
          </div>
          <input
            type="date"
            value={draftStartDate}
            onChange={e => { setDraftStartDate(e.target.value); setIsDirty(true); }}
            style={inputStyle}
          />
          <TimePicker
            value={draftStartTime}
            onChange={v => { setDraftStartTime(v); setIsDirty(true); }}
            style={{ marginTop: 6, width: '100%', boxSizing: 'border-box' }}
          />
        </div>
```

- [ ] **Step 5: Verify TypeScript compilation**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds with no errors in `LeagueCreate.tsx`.

- [ ] **Step 6: Commit**

```
git add DCF.Web/src/pages/LeagueCreate.tsx
git commit -m "feat: replace datetime-local with TimePicker in LeagueCreate (FD-4)"
```

---

## Task 3: Update `LeagueDetail.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`

**Context:** `editDraftStartTime` holds a combined `YYYY-MM-DDTHH:MM` string. The `toDatetimeLocal` helper (line 71) already produces `YYYY-MM-DDTHH:MM` — split on `T` when populating state. The `datetimeLocalToIso` helper stays unchanged.

- [ ] **Step 1: Add the TimePicker import**

In `LeagueDetail.tsx`, add the import after the existing imports:

```tsx
import { TimePicker } from '../components/TimePicker';
```

- [ ] **Step 2: Split the combined state into two vars**

Replace (line ~105):
```tsx
  const [editDraftStartTime, setEditDraftStartTime] = useState('');
```

With:
```tsx
  const [editDraftStartDate, setEditDraftStartDate] = useState('');
  const [editDraftStartTime, setEditDraftStartTime] = useState('');
```

- [ ] **Step 3: Update `startEditing` to split on T**

Find in `startEditing` (line ~207):
```tsx
    setEditDraftStartTime(toDatetimeLocal(league.draftStartTime));
```

Replace with:
```tsx
    const combined = toDatetimeLocal(league.draftStartTime);
    setEditDraftStartDate(combined.split('T')[0] ?? '');
    setEditDraftStartTime(combined.split('T')[1] ?? '');
```

- [ ] **Step 4: Also reset both vars on null draftStartTime**

The current `startEditing` calls `toDatetimeLocal(league.draftStartTime)` which returns `''` when `league.draftStartTime` is null/undefined. After your Task 3 Step 3 change, this would set `editDraftStartDate` to `''` and `editDraftStartTime` to `''` — which is correct. No additional change needed.

- [ ] **Step 5: Update `saveEdits` to recombine**

Find in `saveEdits` (line ~219):
```tsx
        draftStartTime: editDraftStartTime ? datetimeLocalToIso(editDraftStartTime) : null,
```

Replace with:
```tsx
        draftStartTime: (editDraftStartDate && editDraftStartTime) ? datetimeLocalToIso(`${editDraftStartDate}T${editDraftStartTime}`) : null,
```

- [ ] **Step 6: Replace the datetime-local input with date + TimePicker**

Find in `renderInfoTab` (line ~580–590):
```tsx
              <div>
                <div style={labelStyle}>
                  Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(leave blank to remove)</span>
                </div>
                <input
                  type="datetime-local"
                  value={editDraftStartTime}
                  onChange={e => setEditDraftStartTime(e.target.value)}
                  style={inputStyle}
                />
              </div>
```

Replace with:
```tsx
              <div>
                <div style={labelStyle}>
                  Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(leave blank to remove)</span>
                </div>
                <input
                  type="date"
                  value={editDraftStartDate}
                  onChange={e => setEditDraftStartDate(e.target.value)}
                  style={inputStyle}
                />
                <TimePicker
                  value={editDraftStartTime}
                  onChange={v => setEditDraftStartTime(v)}
                  style={{ marginTop: 6, width: '100%', boxSizing: 'border-box' }}
                />
              </div>
```

- [ ] **Step 7: Verify TypeScript compilation**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds with no errors in `LeagueDetail.tsx`.

- [ ] **Step 8: Commit**

```
git add DCF.Web/src/pages/LeagueDetail.tsx
git commit -m "feat: replace datetime-local with TimePicker in LeagueDetail (FD-4)"
```

---

## Task 4: Update `SeasonDetail.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Context:** There are four `type="time"` inputs to swap. All four use the same `HH:MM` string interface as TimePicker — no state changes needed.

The four inputs:
1. **Add Show form — Start time** (`showStartTime`, optional): line ~554
2. **Add Show form — Scores time** (`showScoresTime`, required): line ~556
3. **Edit Show form — Start time** (`editShow.startTime`, optional): line ~628
4. **Edit Show form — Scores time** (`editShow.scoresTime`, required): line ~630

- [ ] **Step 1: Add the TimePicker import**

In `SeasonDetail.tsx`, add the import after the existing imports:

```tsx
import { TimePicker } from '../components/TimePicker';
```

- [ ] **Step 2: Swap the Add Show form's Start and Scores time inputs**

Find (line ~552–557):
```tsx
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={labelStyle}>Start</label>
                  <input type="time" value={showStartTime} onChange={e => setShowStartTime(e.target.value)} style={{ ...inputStyle, flex: 1 }} />
                  <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                  <input type="time" value={showScoresTime} onChange={e => setShowScoresTime(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
                </div>
```

Replace with:
```tsx
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={labelStyle}>Start</label>
                  <TimePicker value={showStartTime} onChange={setShowStartTime} style={{ flex: 1 }} />
                  <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                  <TimePicker value={showScoresTime} onChange={setShowScoresTime} required style={{ flex: 1 }} />
                </div>
```

- [ ] **Step 3: Swap the Edit Show form's Start and Scores time inputs**

Find (line ~626–631):
```tsx
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <label style={labelStyle}>Start</label>
                          <input type="time" value={editShow.startTime} onChange={e => setEditShow(p => p && ({ ...p, startTime: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                          <input type="time" value={editShow.scoresTime} onChange={e => setEditShow(p => p && ({ ...p, scoresTime: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                        </div>
```

Replace with:
```tsx
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <label style={labelStyle}>Start</label>
                          <TimePicker value={editShow.startTime} onChange={v => setEditShow(p => p && ({ ...p, startTime: v }))} style={{ flex: 1 }} />
                          <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                          <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                        </div>
```

- [ ] **Step 4: Verify TypeScript compilation**

```
cd DCF.Web && npm run build
```

Expected: Build succeeds with no errors in `SeasonDetail.tsx`.

- [ ] **Step 5: Commit**

```
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: replace time inputs with TimePicker in SeasonDetail (FD-4)"
```

---

## Manual UI Verification Checklist

After all tasks are complete, run `npm run dev` and verify these scenarios in the browser:

- [ ] **SeasonDetail — Add Show:** Start and Scores time pickers render; arrows step in 5-minute increments; minute wrap carries into hour (7:55 → 8:00); AM/PM select works; typing a number and blurring commits it; Scores field (required) never emits `""`
- [ ] **SeasonDetail — Edit Show:** Pre-populates from existing show data; save reconstructs ISO correctly
- [ ] **LeagueCreate:** Date picker + TimePicker render side by side; submitting with both set sends a valid ISO timestamp; submitting with TimePicker empty (no time set) sends `null`
- [ ] **LeagueDetail — Info tab → Edit:** Pre-populates from existing `draftStartTime`; clearing TimePicker then saving removes the scheduled time; setting a new value saves correctly
- [ ] **Visual:** All pickers use `--surface`, `--border-input`, `--text-heading` — no native browser chrome visible
