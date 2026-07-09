# Admin Show-List UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the trigger-scrape button's misleading always-success result and missing loading state; add a "Fetch from DCI" button (plus the editable schedule/location fields it needs) to the show edit form; add search and a curated status filter to the admin shows list.

**Architecture:** Entirely frontend, entirely within `DCF.Web/src/pages/SeasonDetail.tsx` and its already-established `SeasonDetail.helpers.ts` pure-helper file. Three new testable pure functions (`getScrapeResultMessage`, `buildSchedulePayload`, `getShowFilterBucket`) follow the same extract-and-unit-test pattern `getShowStatusBadge` established in the previous spec. No backend or API-client contract changes beyond widening one existing endpoint's declared TypeScript return type to match what it already sends.

**Tech Stack:** React 19 + TypeScript + Vite, Vitest for tests.

## Global Constraints

- All work in this plan happens on branch `feat/admin-show-list-ux` (already created off `master`; the design spec commit is already on it). Commit after every task.
- **No backend or `DCF.Api`/`DCF.Data`/`DCF.Tests` changes anywhere in this plan.** Every endpoint this plan needs already exists and already accepts/returns everything required (`PrefillShowAsync`, `UpdateShowRequest`'s existing `Location`/`Latitude`/`Longitude`/`Schedule` fields, `TriggerScrapeAsync`'s existing `{ outcome, error }` response). If a task seems to require a backend change, stop — that means something about the current API surface was misunderstood, not that a new backend change is needed.
- Frontend tests use Vitest, colocated as `<name>.test.ts` next to the source file, matching the existing `SeasonDetail.helpers.test.ts` convention.
- The new edit-form fields (Fetch from DCI button, Location/Lat/Lng, Schedule preview) live **inside** the existing edit-form lock (`!started && !hasScoresAnnounced(s)`), not given the always-available treatment the no-score-reason control has. They're part of the same core-identity editing surface as name/date/corps.
- The edit-form's "Fetch from DCI" merge semantics must mirror the Add form's `fetchFromDci` exactly: `date`/`startTime`/`scoresTime`(`scoresAnnouncedTime`)/`tz`(`timezone`)/`corpsIds` only overwrite when the fetch actually returned a value; `schedule`/`location`/`latitude`/`longitude` always overwrite (including to empty/null if DCI didn't have them). This is a deliberate consistency choice — don't reinterpret it as "always overwrite everything unconditionally."
- `SeasonDetail.helpers.ts`'s `toNullableIso` becomes dead code once Task 3 changes `saveShowEdit` to stop calling it. Task 3 removes it (function, its tests, its import) rather than leaving it orphaned — confirmed via grep that its only production call site is the one Task 3 replaces.

---

## File Structure

**Frontend — modified, no new files:**
- `DCF.Web/src/types/api.ts` — new `TriggerScrapeResult` interface.
- `DCF.Web/src/api/client.ts` — `adminTriggerScrape`'s declared return type changes from `void` to `TriggerScrapeResult` (the backend already sends this body; the frontend just wasn't typed to read it).
- `DCF.Web/src/pages/SeasonDetail.helpers.ts` — three new exports (`getScrapeResultMessage`, `buildSchedulePayload`, `getShowFilterBucket`, each with a supporting type/interface); `toNullableIso` removed in Task 3.
- `DCF.Web/src/pages/SeasonDetail.helpers.test.ts` — tests for the three new helpers; `toNullableIso`'s tests removed in Task 3.
- `DCF.Web/src/pages/SeasonDetail.tsx` — trigger-scrape loading/result wiring (Task 1); `addShow` refactored onto the extracted helper (Task 2); `editShow` state gains location/lat/lng/schedule, new edit-form UI, `saveShowEdit` sends real values instead of echoing the original show (Task 3); search input + filter dropdown + filtered rendering (Task 4).

---

### Task 1: Trigger-scrape loading + real result UX

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Produces: `TriggerScrapeResult { outcome: 'Succeeded' | 'Failed' | 'Skipped'; error: string | null }` (in `types/api.ts`); `api.adminTriggerScrape(showId): Promise<TriggerScrapeResult>`; `getScrapeResultMessage(result: TriggerScrapeResult): { text: string; color: string; sticky: boolean }`.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`, changing the import on line 2:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge } from './SeasonDetail.helpers';
```

to:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge, getScrapeResultMessage } from './SeasonDetail.helpers';
```

Then add at the end of the file:

```typescript

describe('getScrapeResultMessage', () => {
  it('returns a non-sticky success message', () => {
    expect(getScrapeResultMessage({ outcome: 'Succeeded', error: null })).toEqual({
      text: '✓ Scrape succeeded', color: 'var(--green)', sticky: false,
    });
  });

  it('returns a sticky failure message including the error text', () => {
    expect(getScrapeResultMessage({ outcome: 'Failed', error: 'Connection timed out' })).toEqual({
      text: '✗ Scrape failed: Connection timed out', color: 'var(--red)', sticky: true,
    });
  });

  it('falls back to a generic message when a failure has no error text', () => {
    expect(getScrapeResultMessage({ outcome: 'Failed', error: null })).toEqual({
      text: '✗ Scrape failed: Unknown error', color: 'var(--red)', sticky: true,
    });
  });

  it('returns a non-sticky skipped message', () => {
    expect(getScrapeResultMessage({ outcome: 'Skipped', error: null })).toEqual({
      text: 'Scrape skipped', color: 'var(--accent)', sticky: false,
    });
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: FAIL — `does not provide an export named 'getScrapeResultMessage'`.

- [ ] **Step 3: Add `TriggerScrapeResult`, widen the client's return type, and add the helper**

In `DCF.Web/src/types/api.ts`, add after the `Show` interface's closing brace (after `export interface Show { ... }`):

```typescript

export interface TriggerScrapeResult {
  outcome: 'Succeeded' | 'Failed' | 'Skipped';
  error: string | null;
}
```

In `DCF.Web/src/api/client.ts`, change the import on line 1:

```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, ShowScheduleEntry, Standing, UpdateLeagueRequest, UserProfile } from '../types/api';
```

to:

```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, ShowScheduleEntry, Standing, TriggerScrapeResult, UpdateLeagueRequest, UserProfile } from '../types/api';
```

Then change:

```typescript
  adminTriggerScrape: (showId: string) =>
    request<void>(`/api/admin/shows/${showId}/scrape`, { method: 'POST' }),
```

to:

```typescript
  adminTriggerScrape: (showId: string) =>
    request<TriggerScrapeResult>(`/api/admin/shows/${showId}/scrape`, { method: 'POST' }),
```

In `DCF.Web/src/pages/SeasonDetail.helpers.ts`, change the import on line 1:

```typescript
import type { Show } from '../types/api';
```

to:

```typescript
import type { Show, TriggerScrapeResult } from '../types/api';
```

Then add at the end of the file:

```typescript

export interface ScrapeResultMessage {
  text: string;
  color: string;
  sticky: boolean;
}

export function getScrapeResultMessage(result: TriggerScrapeResult): ScrapeResultMessage {
  if (result.outcome === 'Succeeded') {
    return { text: '✓ Scrape succeeded', color: 'var(--green)', sticky: false };
  }

  if (result.outcome === 'Failed') {
    return { text: `✗ Scrape failed: ${result.error ?? 'Unknown error'}`, color: 'var(--red)', sticky: true };
  }

  return { text: 'Scrape skipped', color: 'var(--accent)', sticky: false };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: PASS (all tests, including the 4 new ones)

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — confirms `client.ts`'s changed return type doesn't break any existing caller (it won't yet, since `SeasonDetail.tsx` doesn't read the response body until Step 5 below).

- [ ] **Step 5: Wire loading + real result into `SeasonDetail.tsx`**

Change the import block (near the top of the file):

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge,
} from './SeasonDetail.helpers';
```

to:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage,
} from './SeasonDetail.helpers';
import type { TriggerScrapeResult } from '../types/api';
```

Change:

```typescript
  const [error, setError] = useState<string | null>(null);
  const [scrapeSuccessId, setScrapeSuccessId] = useState<string | null>(null);
```

to:

```typescript
  const [error, setError] = useState<string | null>(null);
  const [triggeringScrapeId, setTriggeringScrapeId] = useState<string | null>(null);
  const [scrapeResult, setScrapeResult] = useState<{ showId: string; result: TriggerScrapeResult } | null>(null);
```

Change:

```typescript
          {shows.map(s => {
            const expanded = expandedShowId === s.id;
            const started = hasStarted(s);
            const statusBadge = getShowStatusBadge(s);

            return (
```

to:

```typescript
          {shows.map(s => {
            const expanded = expandedShowId === s.id;
            const started = hasStarted(s);
            const statusBadge = getShowStatusBadge(s);
            const scrapeMessage = scrapeResult && scrapeResult.showId === s.id ? getScrapeResultMessage(scrapeResult.result) : null;

            return (
```

Finally, change the trigger-scrape block:

```jsx
                    {!s.isExhibition && !s.noScoreReason && (started || hasScoresAnnounced(s)) && (
                      <div style={{ marginTop: 10 }}>
                        <button
                          type="button"
                          onClick={() => {
                            api.adminTriggerScrape(s.id)
                              .then(() => {
                                setError(null);
                                setScrapeSuccessId(s.id);
                                setTimeout(() => setScrapeSuccessId(null), 3000);
                              })
                              .catch(() => setError('Scrape trigger failed.'));
                          }}
                          style={{
                            width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                            background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                          }}
                        >
                          Trigger Score Scrape
                        </button>

                        {scrapeSuccessId === s.id && (
                          <div style={{ marginTop: 6, fontSize: 11, fontWeight: 600, color: 'var(--green)', textAlign: 'center' }}>
                            ✓ Scrape triggered successfully
                          </div>
                        )}
                      </div>
                    )}
```

to:

```jsx
                    {!s.isExhibition && !s.noScoreReason && (started || hasScoresAnnounced(s)) && (
                      <div style={{ marginTop: 10 }}>
                        <button
                          type="button"
                          onClick={() => {
                            setTriggeringScrapeId(s.id);
                            setScrapeResult(null);

                            api.adminTriggerScrape(s.id)
                              .then(async result => {
                                setError(null);
                                setScrapeResult({ showId: s.id, result });

                                const updated = await api.adminGetShows(id!);

                                setShows(updated);

                                if (!getScrapeResultMessage(result).sticky) {
                                  setTimeout(() => setScrapeResult(null), 3000);
                                }
                              })
                              .catch(() => setError('Scrape trigger failed.'))
                              .finally(() => setTriggeringScrapeId(null));
                          }}
                          disabled={triggeringScrapeId === s.id}
                          style={{
                            width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                            background: triggeringScrapeId === s.id ? 'var(--border)' : 'var(--accent)',
                            color: triggeringScrapeId === s.id ? 'var(--text-faint)' : 'var(--bg)',
                            border: 'none', cursor: triggeringScrapeId === s.id ? 'not-allowed' : 'pointer',
                          }}
                        >
                          {triggeringScrapeId === s.id ? 'Scraping…' : 'Trigger Score Scrape'}
                        </button>

                        {scrapeMessage && (
                          <div style={{ marginTop: 6, fontSize: 11, fontWeight: 600, color: scrapeMessage.color, textAlign: 'center' }}>
                            {scrapeMessage.text}
                          </div>
                        )}
                      </div>
                    )}
```

- [ ] **Step 6: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — no type errors

Run: `npm run lint` (from `DCF.Web/`)
Expected: no new issues in `SeasonDetail.tsx`/`SeasonDetail.helpers.ts` (repo has pre-existing unrelated lint failures elsewhere — confirm via `npx eslint src/pages/SeasonDetail.tsx src/pages/SeasonDetail.helpers.ts` directly, exit code 0)

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (regression check)

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts DCF.Web/src/pages/SeasonDetail.helpers.ts DCF.Web/src/pages/SeasonDetail.helpers.test.ts DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: show real trigger-scrape outcome and loading state instead of a fake always-success message"
```

---

### Task 2: Extract `buildSchedulePayload`, refactor `addShow` onto it

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Produces: `SchedulePayloadEntry { time: string | null; label: string; corpsId: string | null }`; `buildSchedulePayload(entries: SchedulePayloadEntry[], baseDate: string, tz: string): SchedulePayloadEntry[]` — Task 3's `saveShowEdit` change calls this directly.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`, changing the import on line 2 (now widened by Task 1):

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge, getScrapeResultMessage } from './SeasonDetail.helpers';
```

to:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload } from './SeasonDetail.helpers';
```

Then add at the end of the file:

```typescript

describe('buildSchedulePayload', () => {
  it('builds ISO times for each entry using the base date', () => {
    const entries = [
      { time: '13:40', label: 'Guardians - McKinney, TX', corpsId: 'c1' },
      { time: '14:20', label: 'Bluecoats - Canton, OH', corpsId: 'c2' },
    ];
    expect(buildSchedulePayload(entries, '2026-08-15', 'CT')).toEqual([
      { time: '2026-08-15T18:40:00.000Z', label: 'Guardians - McKinney, TX', corpsId: 'c1' },
      { time: '2026-08-15T19:20:00.000Z', label: 'Bluecoats - Canton, OH', corpsId: 'c2' },
    ]);
  });

  it('passes through null (TBD) entries without a time', () => {
    const entries = [{ time: null, label: 'Blue Devils - Concord, CA', corpsId: 'c3' }];
    expect(buildSchedulePayload(entries, '2026-08-15', 'CT')).toEqual([
      { time: null, label: 'Blue Devils - Concord, CA', corpsId: 'c3' },
    ]);
  });

  it('rolls the date forward when a late-night entry wraps past midnight', () => {
    const entries = [
      { time: '23:30', label: 'Late Corps', corpsId: 'c1' },
      { time: '00:15', label: 'After Midnight Corps', corpsId: 'c2' },
    ];
    const result = buildSchedulePayload(entries, '2026-08-15', 'CT');
    expect(result[0].time).toBe('2026-08-16T04:30:00.000Z');
    expect(result[1].time).toBe('2026-08-16T05:15:00.000Z');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: FAIL — `does not provide an export named 'buildSchedulePayload'`.

- [ ] **Step 3: Add the helper**

In `DCF.Web/src/pages/SeasonDetail.helpers.ts`, add at the end of the file:

```typescript

export interface SchedulePayloadEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}

export function buildSchedulePayload(
  entries: SchedulePayloadEntry[],
  baseDate: string,
  tz: string
): SchedulePayloadEntry[] {
  let rolloverDate = baseDate;
  let prevTime = '';

  return entries.map(entry => {
    if (entry.time && prevTime && entry.time < prevTime && prevTime >= '12:00') {
      const d = new Date(`${rolloverDate}T00:00:00`);
      d.setDate(d.getDate() + 1);
      rolloverDate = d.toISOString().slice(0, 10);
    }

    if (entry.time) {
      prevTime = entry.time;
    }

    return {
      time: buildScheduleEntryTime(rolloverDate, entry.time, tz),
      label: entry.label,
      corpsId: entry.corpsId,
    };
  });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: PASS (all tests, including the 3 new ones)

- [ ] **Step 5: Refactor `addShow` to use the extracted helper**

In `DCF.Web/src/pages/SeasonDetail.tsx`, add `buildSchedulePayload` to the helpers import (from Task 1's already-widened import block):

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage,
} from './SeasonDetail.helpers';
```

to:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload,
} from './SeasonDetail.helpers';
```

Then in `addShow`, change:

```typescript
      let rolloverDate = showDate;
      let prevTime = '';

      const schedulePayload = showSchedule.map(entry => {
        if (entry.time && prevTime && entry.time < prevTime && prevTime >= '12:00') {
          const d = new Date(`${rolloverDate}T00:00:00`);
          d.setDate(d.getDate() + 1);
          rolloverDate = d.toISOString().slice(0, 10);
        }

        if (entry.time) {
          prevTime = entry.time;
        }

        return {
          time: buildScheduleEntryTime(rolloverDate, entry.time, showTz),
          label: entry.label,
          corpsId: entry.corpsId,
        };
      });
```

to:

```typescript
      const schedulePayload = buildSchedulePayload(showSchedule, showDate, showTz);
```

- [ ] **Step 6: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — no type errors

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (regression check — this refactor is a behavior-preserving extraction; no existing test targets `addShow`'s exact schedule construction directly, so this is confirmed by the unchanged build/type-check plus manual verification in Task 5)

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.helpers.ts DCF.Web/src/pages/SeasonDetail.helpers.test.ts DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "refactor: extract buildSchedulePayload helper out of addShow's inline rollover logic"
```

---

### Task 3: Edit-form "Fetch from DCI" + editable schedule/location

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`

**Interfaces:**
- Consumes: `buildSchedulePayload` (Task 2); `api.adminPrefillShow` (existing); `ShowPrefillScheduleEntry` (existing type).

This task has no dedicated automated test of its own — it's UI wiring plus a save-payload change with no new pure-function seam (the logic worth unit-testing, `buildSchedulePayload`, was already extracted and tested in Task 2). Verified manually in Task 5. It does, however, remove now-dead code (`toNullableIso`) as a direct consequence of its own change — that removal's verification is "the build still succeeds with the import gone."

- [ ] **Step 1: Expand `editShow` state and add edit-fetch state**

In `DCF.Web/src/pages/SeasonDetail.tsx`, change:

```typescript
  const [editShow, setEditShow] = useState<{
    name: string; url: string; date: string;
    startTime: string; scoresTime: string; tz: string;
    corpsIds: Set<string>;
  } | null>(null);
  const [savingShowEdit, setSavingShowEdit] = useState(false);
  const [deletingShowId, setDeletingShowId] = useState<string | null>(null);
```

to:

```typescript
  const [editShow, setEditShow] = useState<{
    name: string; url: string; date: string;
    startTime: string; scoresTime: string; tz: string;
    corpsIds: Set<string>;
    location: string; latitude: number | null; longitude: number | null;
    schedule: ShowPrefillScheduleEntry[];
  } | null>(null);
  const [savingShowEdit, setSavingShowEdit] = useState(false);
  const [deletingShowId, setDeletingShowId] = useState<string | null>(null);
  const [editPrefetchError, setEditPrefetchError] = useState<string | null>(null);
  const [editPrefetching, setEditPrefetching] = useState(false);
  const [editPrefetched, setEditPrefetched] = useState(false);
```

(`ShowPrefillScheduleEntry` is already imported at the top of the file.)

- [ ] **Step 2: Seed the new fields in `expandShow`, reset edit-fetch state on collapse/expand**

Change:

```typescript
  function expandShow(show: Show) {
    if (expandedShowId === show.id) {
      setExpandedShowId(null);
      expandedShowIdRef.current = null;
      setEditShow(null);
      setNoScoreReasonInput('');
      return;
    }
    const tz = show.timezone ?? 'ET';
    const toHHMM = (iso: string) => {
      const d = new Date(iso);
      d.setUTCHours(d.getUTCHours() - (TZ_HOURS[tz] ?? 4));
      return d.toISOString().slice(11, 16);
    };
    setExpandedShowId(show.id);
    expandedShowIdRef.current = show.id;
    setEditShow({
      name: show.name,
      url: show.url ?? '',
      date: show.date,
      startTime: show.startTime ? toHHMM(show.startTime) : '',
      scoresTime: show.scoresAnnouncedTime ? toHHMM(show.scoresAnnouncedTime) : '',
      tz,
      corpsIds: new Set(show.corpsIds),
    });
    setNoScoreReasonInput(show.noScoreReason ?? '');
  }
```

to:

```typescript
  function expandShow(show: Show) {
    if (expandedShowId === show.id) {
      setExpandedShowId(null);
      expandedShowIdRef.current = null;
      setEditShow(null);
      setNoScoreReasonInput('');
      setEditPrefetchError(null);
      setEditPrefetched(false);
      return;
    }
    const tz = show.timezone ?? 'ET';
    const toHHMM = (iso: string) => {
      const d = new Date(iso);
      d.setUTCHours(d.getUTCHours() - (TZ_HOURS[tz] ?? 4));
      return d.toISOString().slice(11, 16);
    };
    setExpandedShowId(show.id);
    expandedShowIdRef.current = show.id;
    setEditShow({
      name: show.name,
      url: show.url ?? '',
      date: show.date,
      startTime: show.startTime ? toHHMM(show.startTime) : '',
      scoresTime: show.scoresAnnouncedTime ? toHHMM(show.scoresAnnouncedTime) : '',
      tz,
      corpsIds: new Set(show.corpsIds),
      location: show.location ?? '',
      latitude: show.latitude ?? null,
      longitude: show.longitude ?? null,
      schedule: show.schedule.map(e => ({
        time: e.time ? toHHMM(e.time) : null,
        label: e.label,
        corpsId: e.corpsId,
      })),
    });
    setNoScoreReasonInput(show.noScoreReason ?? '');
    setEditPrefetchError(null);
    setEditPrefetched(false);
  }
```

- [ ] **Step 3: Add the `editFetchFromDci` handler**

Add after `expandShow`'s closing brace, before `saveShowEdit`:

```typescript

  const editFetchFromDci = async () => {
    if (!id || !editShow || editPrefetching || editPrefetched) return;
    setEditPrefetching(true);
    setEditPrefetchError(null);

    try {
      const data = await api.adminPrefillShow(id, editShow.name);

      setEditShow(p => p && ({
        ...p,
        date: data.date ?? p.date,
        startTime: data.startTime ?? p.startTime,
        scoresTime: data.scoresAnnouncedTime ?? p.scoresTime,
        tz: data.timezone ?? p.tz,
        corpsIds: data.corpsIds.length > 0 ? new Set(data.corpsIds) : p.corpsIds,
        location: data.location ?? '',
        latitude: data.latitude ?? null,
        longitude: data.longitude ?? null,
        schedule: data.schedule,
      }));
      setEditPrefetched(true);
    } catch {
      setEditPrefetchError('Could not fetch from DCI — fill in manually.');
    } finally {
      setEditPrefetching(false);
    }
  };
```

- [ ] **Step 4: Update `saveShowEdit` to send `editShow`'s location/schedule instead of echoing the original show**

Change:

```typescript
      await api.adminUpdateShow(showId, {
        name: editShow.name,
        url: show.isExhibition ? null : editShow.url,
        date: editShow.date,
        startTime: startTimeIso,
        scoresAnnouncedTime: scoresTimeIso,
        timezone: editShow.tz,
        isExhibition: show.isExhibition,
        location: show.location ?? null,
        latitude: show.latitude ?? null,
        longitude: show.longitude ?? null,
        corpsIds: Array.from(editShow.corpsIds),
        schedule: show.schedule.map(e => ({
          time: toNullableIso(e.time),
          label: e.label,
          corpsId: e.corpsId,
        })),
      });
```

to:

```typescript
      await api.adminUpdateShow(showId, {
        name: editShow.name,
        url: show.isExhibition ? null : editShow.url,
        date: editShow.date,
        startTime: startTimeIso,
        scoresAnnouncedTime: scoresTimeIso,
        timezone: editShow.tz,
        isExhibition: show.isExhibition,
        location: editShow.location || null,
        latitude: editShow.latitude,
        longitude: editShow.longitude,
        corpsIds: Array.from(editShow.corpsIds),
        schedule: buildSchedulePayload(editShow.schedule, editShow.date, editShow.tz),
      });
```

- [ ] **Step 5: Add the new edit-form UI**

Change:

```jsx
                      <>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 10 }}>
                          <label style={labelStyle}>Name</label>
                          <input value={editShow.name} onChange={e => setEditShow(p => p && ({ ...p, name: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                        </div>
                        {!s.isExhibition && (
                          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                            <label style={labelStyle}>URL</label>
                            <input value={editShow.url} onChange={e => setEditShow(p => p && ({ ...p, url: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          </div>
                        )}
                        {/* Date / TZ */}
                        <div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>Date</label>
                            <input type="date" value={editShow.date} onChange={e => setEditShow(p => p && ({ ...p, date: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          </div>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>TZ</label>
                            <select value={editShow.tz} onChange={e => setEditShow(p => p && ({ ...p, tz: e.target.value }))} style={{ ...inputStyle, width: 62 }}>
                              {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
                            </select>
                          </div>
                        </div>

                        {/* Start / Scores */}
                        <div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>Start</label>
                            <TimePicker value={editShow.startTime} onChange={v => setEditShow(p => p && ({ ...p, startTime: v }))} style={{ flex: 1 }} />
                          </div>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>{s.isExhibition ? 'Concludes' : 'Scores'}</label>
                            <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                          </div>
                        </div>
                        <div>
                          <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                            {seasonCorps.map(c => (
                              <Chip
                                key={c.id}
                                label={c.name}
                                selected={editShow.corpsIds.has(c.id)}
                                onClick={() => setEditShow(p => {
                                  if (!p) return p;
                                  const next = new Set(p.corpsIds);
                                  if (next.has(c.id)) next.delete(c.id); else next.add(c.id);
                                  return { ...p, corpsIds: next };
                                })}
                              />
                            ))}
                          </div>
                        </div>
                        <div style={{ display: 'flex', gap: 8 }}>
```

to:

```jsx
                      <>
                        <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end', marginTop: 10 }}>
                          <div style={{ flex: 1 }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                              <label style={labelStyle}>Name</label>
                              <input value={editShow.name} onChange={e => setEditShow(p => p && ({ ...p, name: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                            </div>
                          </div>
                          <button
                            type="button"
                            onClick={editFetchFromDci}
                            disabled={editPrefetching || editPrefetched}
                            style={{
                              padding: '7px 12px', borderRadius: 5, fontSize: 10, fontWeight: 600,
                              background: 'var(--accent)', border: 'none', color: 'var(--bg)',
                              cursor: editPrefetching || editPrefetched ? 'not-allowed' : 'pointer',
                              opacity: editPrefetching || editPrefetched ? 0.5 : 1, whiteSpace: 'nowrap',
                            }}
                          >
                            {editPrefetching ? 'Fetching…' : editPrefetched ? 'Fetched' : 'Fetch from DCI'}
                          </button>
                        </div>

                        {editPrefetchError && (
                          <p style={{ fontSize: 10, color: 'var(--red)', margin: '2px 0 0' }}>{editPrefetchError}</p>
                        )}

                        {!s.isExhibition && (
                          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                            <label style={labelStyle}>URL</label>
                            <input value={editShow.url} onChange={e => setEditShow(p => p && ({ ...p, url: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          </div>
                        )}

                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <label style={labelStyle}>Location</label>
                          <input
                            style={{ ...inputStyle, flex: 2 }}
                            value={editShow.location}
                            onChange={e => setEditShow(p => p && ({ ...p, location: e.target.value }))}
                            placeholder="City, ST"
                          />
                          <input
                            type="number"
                            step="any"
                            placeholder="Lat"
                            style={{ ...inputStyle, width: 90 }}
                            value={editShow.latitude ?? ''}
                            onChange={e => setEditShow(p => p && ({ ...p, latitude: e.target.value ? parseFloat(e.target.value) : null }))}
                          />
                          <input
                            type="number"
                            step="any"
                            placeholder="Lng"
                            style={{ ...inputStyle, width: 90 }}
                            value={editShow.longitude ?? ''}
                            onChange={e => setEditShow(p => p && ({ ...p, longitude: e.target.value ? parseFloat(e.target.value) : null }))}
                          />
                        </div>

                        {/* Date / TZ */}
                        <div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>Date</label>
                            <input type="date" value={editShow.date} onChange={e => setEditShow(p => p && ({ ...p, date: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          </div>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>TZ</label>
                            <select value={editShow.tz} onChange={e => setEditShow(p => p && ({ ...p, tz: e.target.value }))} style={{ ...inputStyle, width: 62 }}>
                              {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
                            </select>
                          </div>
                        </div>

                        {/* Start / Scores */}
                        <div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>Start</label>
                            <TimePicker value={editShow.startTime} onChange={v => setEditShow(p => p && ({ ...p, startTime: v }))} style={{ flex: 1 }} />
                          </div>
                          <div className="admin-show-form-pair">
                            <label style={labelStyle}>{s.isExhibition ? 'Concludes' : 'Scores'}</label>
                            <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                          </div>
                        </div>

                        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
                          <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                              {seasonCorps.map(c => (
                                <Chip
                                  key={c.id}
                                  label={c.name}
                                  selected={editShow.corpsIds.has(c.id)}
                                  onClick={() => setEditShow(p => {
                                    if (!p) return p;
                                    const next = new Set(p.corpsIds);
                                    if (next.has(c.id)) next.delete(c.id); else next.add(c.id);
                                    return { ...p, corpsIds: next };
                                  })}
                                />
                              ))}
                            </div>
                          </div>

                          {editShow.schedule.length > 0 && (
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Schedule</div>
                              <div style={{
                                background: 'var(--bg)', border: '1px solid var(--border)',
                                borderRadius: 5, padding: '4px 8px', fontSize: 10, color: 'var(--text-muted)',
                              }}>
                                {editShow.schedule.map((entry, i) => (
                                  <div key={i} style={{ display: 'flex', gap: 8, padding: '2px 0' }}>
                                    <span style={{
                                      minWidth: 36, fontVariantNumeric: 'tabular-nums',
                                      color: entry.time ? undefined : 'var(--text-faint)',
                                    }}>
                                      {entry.time ?? 'TBD'}
                                    </span>
                                    <span>{entry.label}</span>
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}
                        </div>

                        <div style={{ display: 'flex', gap: 8 }}>
```

(The block's tail — the Delete/Save buttons and their closing tags — is unchanged; only the opening portion up through "Participating Corps" changes, and the `<div style={{ display: 'flex', gap: 8 }}>` that starts the Delete/Save row is the resync point back to unchanged code.)

- [ ] **Step 6: Remove the now-dead `toNullableIso`**

Confirm via `grep -rn "toNullableIso" DCF.Web/src` that the only remaining references are its own definition and its own tests (Step 4 above removed the one production call site). Then:

In `DCF.Web/src/pages/SeasonDetail.helpers.ts`, remove:

```typescript

export function toNullableIso(time: string | null): string | null {
  return time ? new Date(time).toISOString() : null;
}
```

In `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`, remove:

```typescript

describe('toNullableIso', () => {
  it('converts an existing ISO time string to ISO', () => {
    expect(toNullableIso('2026-08-15T23:00:00.000Z')).toBe('2026-08-15T23:00:00.000Z');
  });

  it('returns null for an unscheduled (TBD) entry instead of the Unix epoch', () => {
    expect(toNullableIso(null)).toBeNull();
  });
});
```

Then, in the same file, change its import line:

```typescript
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload } from './SeasonDetail.helpers';
```

to:

```typescript
import { buildDateTime, buildScheduleEntryTime, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload } from './SeasonDetail.helpers';
```

Finally, in `DCF.Web/src/pages/SeasonDetail.tsx`, change its helpers import block:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime, toNullableIso,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload,
} from './SeasonDetail.helpers';
```

to:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload,
} from './SeasonDetail.helpers';
```

- [ ] **Step 7: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — no type errors, and no "unused import" complaint for `toNullableIso`

Run: `npm run lint` (from `DCF.Web/`)
Expected: no new issues in the touched files

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (the `toNullableIso` describe block is gone; all remaining tests pass)

- [ ] **Step 8: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/pages/SeasonDetail.helpers.ts DCF.Web/src/pages/SeasonDetail.helpers.test.ts
git commit -m "feat: add Fetch from DCI and editable schedule/location to the show edit form"
```

---

### Task 4: Search + status filter

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

**Interfaces:**
- Consumes: `hasScoresAnnounced` (existing, from helpers).
- Produces: `ShowFilterBucket = 'upcoming' | 'needsAttention' | 'done'`; `getShowFilterBucket(show: Show): ShowFilterBucket`.

- [ ] **Step 1: Write the failing tests**

Add to `DCF.Web/src/pages/SeasonDetail.helpers.test.ts`, changing the import line (now widened by Tasks 1-2, with `toNullableIso` removed by Task 3):

```typescript
import { buildDateTime, buildScheduleEntryTime, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload } from './SeasonDetail.helpers';
```

to:

```typescript
import { buildDateTime, buildScheduleEntryTime, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload, getShowFilterBucket } from './SeasonDetail.helpers';
```

Then add at the end of the file:

```typescript

describe('getShowFilterBucket', () => {
  it('returns needsAttention for a competitive show with a failed scrape and no reason', () => {
    const show = makeShow({ scrapeStatus: 'Failed' });
    expect(getShowFilterBucket(show)).toBe('needsAttention');
  });

  it('returns done, not needsAttention, once a no-score reason is set on a failed scrape', () => {
    const show = makeShow({ scrapeStatus: 'Failed', noScoreReason: 'Rained out' });
    expect(getShowFilterBucket(show)).toBe('done');
  });

  it('returns done for a successful scrape', () => {
    const show = makeShow({ scrapeStatus: 'Succeeded' });
    expect(getShowFilterBucket(show)).toBe('done');
  });

  it('returns done for a concluded exhibition show', () => {
    const past = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    const show = makeShow({ isExhibition: true, scoresAnnouncedTime: past });
    expect(getShowFilterBucket(show)).toBe('done');
  });

  it('returns upcoming for a show that has not started', () => {
    const show = makeShow();
    expect(getShowFilterBucket(show)).toBe('upcoming');
  });

  it('returns upcoming for an unconcluded exhibition show even with a failed-looking scrapeStatus, since exhibitions are never scraped', () => {
    const show = makeShow({ isExhibition: true, scrapeStatus: 'Failed' });
    expect(getShowFilterBucket(show)).toBe('upcoming');
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: FAIL — `does not provide an export named 'getShowFilterBucket'`.

- [ ] **Step 3: Add the helper**

In `DCF.Web/src/pages/SeasonDetail.helpers.ts`, add at the end of the file:

```typescript

export type ShowFilterBucket = 'upcoming' | 'needsAttention' | 'done';

export function getShowFilterBucket(show: Show): ShowFilterBucket {
  if (!show.isExhibition && show.scrapeStatus === 'Failed' && !show.noScoreReason) {
    return 'needsAttention';
  }

  if (show.noScoreReason || show.scrapeStatus === 'Succeeded' || (show.isExhibition && hasScoresAnnounced(show))) {
    return 'done';
  }

  return 'upcoming';
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npm test -- SeasonDetail.helpers.test.ts` (from `DCF.Web/`)
Expected: PASS (all tests, including the 6 new ones)

- [ ] **Step 5: Wire search + filter into `SeasonDetail.tsx`**

Add `getShowFilterBucket` and the `ShowFilterBucket` type to the helpers import:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload,
} from './SeasonDetail.helpers';
```

to:

```typescript
import {
  TZ_HOURS, buildDateTime, buildScheduleEntryTime,
  hasStarted, hasScoresAnnounced, getShowStatusBadge, getScrapeResultMessage, buildSchedulePayload,
  getShowFilterBucket,
} from './SeasonDetail.helpers';
import type { ShowFilterBucket } from './SeasonDetail.helpers';
```

Add new state next to `corpsOpen`:

```typescript
  const [corpsSortInputs, setCorpsSortInputs] = useState<Record<string, string>>({});
  const [savingOrder, setSavingOrder] = useState(false);
  const [corpsOpen, setCorpsOpen] = useState(false);
```

to:

```typescript
  const [corpsSortInputs, setCorpsSortInputs] = useState<Record<string, string>>({});
  const [savingOrder, setSavingOrder] = useState(false);
  const [corpsOpen, setCorpsOpen] = useState(false);
  const [showSearch, setShowSearch] = useState('');
  const [showFilter, setShowFilter] = useState<'all' | ShowFilterBucket>('all');
```

Compute the filtered list right before rendering the shows list. Change:

```jsx
          {shows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows yet.</div>
          )}

          {shows.map(s => {
```

to:

```jsx
          {shows.length > 0 && (
            <div style={{ display: 'flex', gap: 8 }}>
              <input
                value={showSearch}
                onChange={e => setShowSearch(e.target.value)}
                placeholder="Search shows…"
                style={{ ...inputStyle, flex: 1 }}
              />
              <select
                value={showFilter}
                onChange={e => setShowFilter(e.target.value as 'all' | ShowFilterBucket)}
                style={{ ...inputStyle, width: 160 }}
              >
                <option value="all">All</option>
                <option value="upcoming">Upcoming</option>
                <option value="needsAttention">Needs Attention</option>
                <option value="done">Done</option>
              </select>
            </div>
          )}

          {shows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows yet.</div>
          )}

          {shows.length > 0 && filteredShows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows match your search/filter.</div>
          )}

          {filteredShows.map(s => {
```

Then add the `filteredShows` computation just above the JSX `return` statement. Change:

```typescript
  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  const sortedSeasonCorps = [...seasonCorps].sort((a, b) => {
```

to:

```typescript
  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  const filteredShows = shows.filter(s => {
    const matchesSearch = s.name.toLowerCase().includes(showSearch.toLowerCase());
    const matchesFilter = showFilter === 'all' || getShowFilterBucket(s) === showFilter;
    return matchesSearch && matchesFilter;
  });

  const sortedSeasonCorps = [...seasonCorps].sort((a, b) => {
```

- [ ] **Step 6: Run the full frontend check**

Run: `npm run build` (from `DCF.Web/`)
Expected: SUCCESS — no type errors

Run: `npm run lint` (from `DCF.Web/`)
Expected: no new issues in the touched files

Run: `npm test` (from `DCF.Web/`)
Expected: PASS (regression check)

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.helpers.ts DCF.Web/src/pages/SeasonDetail.helpers.test.ts DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: add search and a curated status filter to the admin shows list"
```

---

### Task 5: End-to-end manual verification

**Files:** none (verification only — no commit at the end of this task)

**Interfaces:**
- Consumes: everything produced by Tasks 1-4.

- [ ] **Step 1: Start the local stack**

```bash
docker compose up -d postgres mosquitto mailpit
dotnet run --project DCF.Api/DCF.Api.csproj
```
In a second terminal, from `DCF.Web/`:
```bash
npm run dev
```
Expected: API listening, Vite dev server on `http://localhost:5173`, no startup errors in either terminal. (No migration to apply — this plan makes no schema changes.)

- [ ] **Step 2: Verify trigger-scrape loading + real result**

Sign in, open an admin season with at least one competitive show whose start time has passed. Click "Trigger Score Scrape". Confirm the button disables and reads "Scraping…" while in flight. Confirm the result message reflects what actually happened (not a blanket success message) — if it fails, confirm the message includes the actual error text and stays visible (doesn't auto-clear after 3s); if it succeeds, confirm it auto-clears after ~3s. Confirm the SCRAPE COMPLETED/FAILED badge on the collapsed card updates immediately without a page reload.

- [ ] **Step 3: Verify edit-form Fetch from DCI**

Expand an existing competitive show whose name matches a real DCI event page. Confirm Location/Lat/Lng fields and a Schedule preview now appear (seeded from the show's current data). Click "Fetch from DCI". Confirm the button shows "Fetching…" then "Fetched" and disables. Confirm location, schedule (with TBD entries rendered distinctly), and any newly-available date/time/corps fields update. Click Save. Reload the page, re-expand the same show, confirm the fetched data persisted (schedule and location in particular, since those previously silently reverted to the original on every save).

- [ ] **Step 4: Verify search**

Type a partial show name into the search box. Confirm the list narrows to matching shows only, case-insensitively. Clear it, confirm the full list returns.

- [ ] **Step 5: Verify the status filter**

Select each of Upcoming / Needs Attention / Done in turn. Confirm: a not-yet-started show appears under Upcoming; a competitive show with a failed scrape and no reason appears under Needs Attention and disappears once marked with a no-score reason; a successfully-scraped or concluded-exhibition show appears under Done. Confirm "All" shows everything again. If no shows currently exist in a given bucket, confirm the "No shows match your search/filter" message appears rather than an empty blank area.

- [ ] **Step 6: Run the full automated suites one more time**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj
```
Expected: all PASS (this plan makes no backend changes, so this is a pure regression check)

```bash
npm test
```
(from `DCF.Web/`)
Expected: all PASS

- [ ] **Step 7: Stop local services**

```bash
docker compose down
```
