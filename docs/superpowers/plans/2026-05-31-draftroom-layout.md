# DraftRoom Layout Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Overhaul DraftRoom to use the standard page container (Nav + maxWidth 1200), replace the two top bars with a single combined bar spanning the grid column only, grey cells, caption shorthands, centred grid, and generous column spacing.

**Architecture:** All changes are in `DraftRoom.tsx` (layout, bar logic, grid polish) and a one-line wrap in `main.tsx`. No backend changes. The combined bar is a `renderBar()` function defined in the component; it replaces both `renderTopBar()` and `renderSubmitBar()` which are deleted.

**Tech Stack:** React 19, TypeScript, inline styles, react-router-dom Link/useNavigate

---

## File Map

| File | Changes |
|---|---|
| `DCF.Web/src/main.tsx` | Add `<AuthenticatedLayout>` wrapper to the `/leagues/:id/draft` route |
| `DCF.Web/src/pages/DraftRoom.tsx` | Module-level constants; remove `renderTopBar`, `renderSubmitBar`; add `renderBar`; update layout JSX; grid grey cells, captions, centering, spacing; remove console.logs |

---

## Task 1: Wrap route in AuthenticatedLayout and fix DraftRoom height

**Files:**
- Modify: `DCF.Web/src/main.tsx`
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Add AuthenticatedLayout to the draft route in main.tsx**

  In `DCF.Web/src/main.tsx`, change line 29:
  ```tsx
  // Before
  { path: '/leagues/:id/draft', element: <ProtectedRoute><DraftRoom /></ProtectedRoute> },
  
  // After
  { path: '/leagues/:id/draft', element: <ProtectedRoute><AuthenticatedLayout><DraftRoom /></AuthenticatedLayout></ProtectedRoute> },
  ```

- [ ] **Step 2: Fix DraftRoom outer container height and remove console.logs**

  In `DCF.Web/src/pages/DraftRoom.tsx`:

  Remove lines 29–30 (debug logs):
  ```tsx
  // DELETE these two lines:
  console.log('League:', league);
  console.log('Draft state:', draftState);
  ```

  In the `return` statement, change the outer div's height from `100vh` to account for the nav (44px) and `AuthenticatedLayout` padding (24px top + 24px bottom = 48px):
  ```tsx
  // Before
  <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', background: 'var(--bg)', color: 'var(--text)', overflow: 'hidden' }}>

  // After
  <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 92px)', background: 'var(--bg)', color: 'var(--text)', overflow: 'hidden' }}>
  ```

- [ ] **Step 3: Verify the page loads with a Nav bar and correct container width**

  Run: `npm run dev` in `DCF.Web/`, open a draft room URL. Confirm Nav appears, content is centred at max 1200px, and the draft room fills the viewport below without a scrollbar.

- [ ] **Step 4: Commit**
  ```bash
  git add DCF.Web/src/main.tsx DCF.Web/src/pages/DraftRoom.tsx
  git commit -m "feat: wrap DraftRoom in AuthenticatedLayout, fix viewport height"
  ```

---

## Task 2: Add module-level constants

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` (above the component function)

- [ ] **Step 1: Add CAPTION_SHORT and H_GAP constants**

  Insert immediately before `export function DraftRoom()`:
  ```tsx
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

  const H_GAP: Record<number, number> = { 3: 18, 4: 12, 5: 7 };
  ```

- [ ] **Step 2: Commit**
  ```bash
  git add DCF.Web/src/pages/DraftRoom.tsx
  git commit -m "feat: add CAPTION_SHORT and H_GAP constants to DraftRoom"
  ```

---

## Task 3: Implement renderBar and restructure layout

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

This task replaces `renderTopBar()` and `renderSubmitBar()` with `renderBar()`, and moves the bar into the grid column in the layout JSX.

- [ ] **Step 1: Add Link to the react-router-dom import**

  ```tsx
  // Before
  import { useNavigate, useParams } from 'react-router-dom';

  // After
  import { Link, useNavigate, useParams } from 'react-router-dom';
  ```
  (Note: `Link` is used for the ← League button. Alternatively `navigate` works too — using `Link` is preferred for semantics.)

- [ ] **Step 2: Replace renderTopBar and renderSubmitBar with renderBar**

  Delete the entire `renderTopBar()` function and the entire `renderSubmitBar()` function. In their place, add the following `renderBar()` function. Place it where `renderTopBar` was (just before `renderDraftOrderTab`).

  ```tsx
  const renderBar = () => {
    const hasScheduledTime = !!league.draftStartTime;
    const commissionerName = draftState.members.find(m => m.userId === league.commissionerUserId)?.displayName ?? 'the commissioner';
    const round = Math.floor(draftState.currentPickNumber / draftState.members.length) + 1;
    const pick = (draftState.currentPickNumber % draftState.members.length) + 1;
    const selectedCorps = corps.find(c => c.id === selectedCell?.corpsId);
    const canSubmit = isMyTurn && !!selectedCell && !submitting;
    const selectionLabel = selectedCorps
      ? `${selectedCorps.name} · ${CAPTION_SHORT[selectedCell!.caption] ?? selectedCell!.caption}`
      : '— · —';

    let barBg: string;
    let barAccent: string;

    if (status === 'Open') {
      barBg = 'linear-gradient(90deg, #0f1a0f, #101810)';
      barAccent = 'var(--green)';
    }
    else if (status === 'InProgress') {
      barBg = 'linear-gradient(90deg, #2e1065, #1a1535)';
      barAccent = 'var(--accent)';
    }
    else {
      barBg = 'var(--surface)';
      barAccent = 'var(--border)';
    }

    const renderStatus = () => {
      if (status === 'Open') {
        if (hasScheduledTime) {
          return (
            <>
              <div style={{ flexShrink: 0 }}>
                <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--green)', fontWeight: 700 }}>Draft Begins In</div>
                <div style={{ fontSize: 22, fontWeight: 900, color: 'var(--text-heading)', fontVariantNumeric: 'tabular-nums' }}>{getCountdown()}</div>
                {league.draftStartTime && (
                  <div style={{ fontSize: 9, color: 'var(--text-muted)' }}>{new Date(league.draftStartTime).toLocaleString()}</div>
                )}
              </div>
              {league.isCommissioner && (
                <button
                  onClick={startDraft}
                  style={{ border: '1px solid var(--green-border)', color: 'var(--green)', background: 'transparent', borderRadius: 5, padding: '5px 12px', fontSize: 10, cursor: 'pointer', fontWeight: 700, flexShrink: 0 }}
                >
                  Start Early
                </button>
              )}
            </>
          );
        }

        if (league.isCommissioner) {
          return (
            <button
              onClick={startDraft}
              style={{ border: '1px solid var(--green-border)', color: 'var(--green)', background: 'transparent', borderRadius: 5, padding: '5px 12px', fontSize: 10, cursor: 'pointer', fontWeight: 700, flexShrink: 0 }}
            >
              Start Draft
            </button>
          );
        }

        return (
          <span style={{ fontSize: 10, color: 'var(--text-muted)', fontStyle: 'italic' }}>
            Waiting for {commissionerName} to start the draft
          </span>
        );
      }

      if (status === 'InProgress') {
        return (
          <>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--accent)', fontWeight: 700 }}>
                {isMyTurn ? 'On the Clock' : 'Now Picking'}
              </div>
              <div style={{ fontSize: 14, fontWeight: 800, color: 'var(--text-heading)' }}>
                {isMyTurn ? (user?.displayName ?? '—') : (currentDrafter?.displayName ?? '—')}
                <span style={{ fontSize: 9, fontWeight: 400, color: 'var(--text-muted)', marginLeft: 6 }}>· Round {round} · Pick {pick}</span>
              </div>
            </div>
            {isMyTurn && (
              <>
                <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
                <div style={{ flexShrink: 0 }}>
                  <div style={{ fontSize: 7, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Selected</div>
                  <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{selectionLabel}</div>
                </div>
                <button
                  onClick={submitPick}
                  disabled={!canSubmit}
                  style={{
                    background: canSubmit ? 'var(--accent)' : 'var(--border)',
                    color: canSubmit ? '#0d0f14' : 'var(--text-faint)',
                    border: 'none', borderRadius: 5, padding: '5px 14px',
                    fontSize: 10, fontWeight: 800, letterSpacing: '0.5px',
                    textTransform: 'uppercase', cursor: canSubmit ? 'pointer' : 'not-allowed',
                    flexShrink: 0,
                  }}
                >
                  Submit Pick
                </button>
              </>
            )}
            {!isMyTurn && league.isCommissioner && (
              <button
                onClick={skipPick}
                style={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)', borderRadius: 5, padding: '5px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600, flexShrink: 0 }}
              >
                Skip Pick
              </button>
            )}
          </>
        );
      }

      return (
        <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)' }}>Draft Complete</div>
      );
    };

    return (
      <div style={{
        background: barBg,
        borderBottom: `2px solid ${barAccent}`,
        padding: '10px 16px',
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        flexShrink: 0,
      }}>
        <Link
          to={`/leagues/${id}`}
          style={{
            fontSize: 10, fontWeight: 600, color: 'var(--text-muted)',
            background: 'rgba(255,255,255,0.07)', border: '1px solid var(--border)',
            borderRadius: 4, padding: '3px 9px', textDecoration: 'none', flexShrink: 0,
          }}
        >
          ← League
        </Link>
        <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-heading)', flexShrink: 0 }}>
          {league.name}
        </div>
        <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, flex: 1 }}>
          {renderStatus()}
        </div>
      </div>
    );
  };
  ```

- [ ] **Step 3: Update the layout JSX**

  Replace the entire `return (...)` block at the bottom of the component with:

  ```tsx
  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 92px)', background: 'var(--bg)', color: 'var(--text)', overflow: 'hidden' }}>
      <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>
        {/* Left — bar + grid */}
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          {renderBar()}
          {renderGrid()}
        </div>
        {/* Right — side panel */}
        <div style={{ width: 280, background: 'var(--surface-2)', borderLeft: '1px solid var(--border)', display: 'flex', flexDirection: 'column', flexShrink: 0 }}>
          <div style={{ display: 'flex', borderBottom: '1px solid var(--border)', background: 'var(--surface)', flexShrink: 0 }}>
            {(['order', 'picks'] as const).map(tab => (
              <button
                key={tab}
                onClick={() => setActiveTab(tab)}
                style={{
                  flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600, cursor: 'pointer',
                  background: 'transparent', border: 'none',
                  color: activeTab === tab ? 'var(--accent)' : 'var(--text-muted)',
                  borderBottom: activeTab === tab ? '2px solid var(--accent)' : '2px solid transparent',
                }}
              >
                {tab === 'order' ? 'Draft Order' : 'Picks'}
              </button>
            ))}
          </div>
          <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
            {activeTab === 'order' ? renderDraftOrderTab() : renderPicksTab()}
          </div>
        </div>
      </div>
    </div>
  );
  ```

  Key difference from before: `renderBar()` is now inside the left column div (not above the flex row), and `renderSubmitBar()` is gone.

- [ ] **Step 4: Verify all bar states render correctly**

  Open the draft room. Check each state is reachable and renders as designed (you may need to temporarily set the draft status in the DB to test each one):
  - Lobby open, no start time, commissioner → "Start Draft" button
  - Lobby open, no start time, non-commissioner → "Waiting for..." text
  - Lobby open, with start time, commissioner → countdown + "Start Early" tight beside it
  - Lobby open, with start time, non-commissioner → countdown only
  - InProgress, your turn → "On the Clock" + Selected + Submit
  - InProgress, other's turn, commissioner → "Now Picking" + Skip
  - InProgress, other's turn, regular → "Now Picking" only
  - Completed → "Draft Complete"

- [ ] **Step 5: Commit**
  ```bash
  git add DCF.Web/src/pages/DraftRoom.tsx
  git commit -m "feat: replace dual bars with single renderBar spanning grid column only"
  ```

---

## Task 4: Grid polish — grey cells, caption shorthands, centred, column spacing

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` — `renderGrid()` function only

- [ ] **Step 1: Update renderGrid**

  Replace the entire `renderGrid()` function with the following. Changes are:
  - `hGap` computed from `H_GAP` lookup
  - Grid container gets `justifyContent: 'center'`
  - Table uses `borderSpacing: \`${hGap}px 3px\``
  - Caption `<th>` uses `CAPTION_SHORT` and 10px font
  - Available cell background changes from `var(--green-bg)` → `#1c1f2c` and border from `var(--green-border)` → `var(--border)`

  ```tsx
  const renderGrid = () => {
    const captions = league.draftableCaptions!;
    const gridLocked = status !== 'InProgress' || !isMyTurn;
    const cellWidth = captions.length <= 3 ? Math.min(88, Math.floor(176 / captions.length)) : 44;
    const hGap = H_GAP[captions.length] ?? 2;

    return (
      <div style={{ flex: 1, overflowY: 'auto', padding: 12, display: 'flex', justifyContent: 'center', alignItems: 'flex-start' }}>
        {status === 'Open' && (
          <div style={{ position: 'absolute', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>
            Pick board locks until the draft begins
          </div>
        )}
        <table style={{ borderCollapse: 'separate', borderSpacing: `${hGap}px 3px` }}>
          <thead>
            <tr>
              {captions.map(cap => (
                <th key={cap} style={{ width: cellWidth, fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-muted)', paddingBottom: 6, textAlign: 'center', fontWeight: 600 }}>
                  {CAPTION_SHORT[cap] ?? cap}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {corps.map(c => (
              <tr key={c.id}>
                {captions.map(cap => {
                  const taken = isTaken(c.id, cap);
                  const selected = !gridLocked && selectedCell?.corpsId === c.id && selectedCell?.caption === cap;
                  const previewed = !taken && !selected && validPreview?.corpsId === c.id && validPreview?.caption === cap;
                  const isLobby = status === 'Open';

                  let bg = '#1c1f2c';
                  let border = '1px solid var(--border)';
                  let boxShadow = 'none';
                  const cursor = gridLocked || taken ? 'not-allowed' : 'pointer';
                  let content: ReactNode;

                  if (taken) {
                    bg = '#12141a';
                    border = '1px solid var(--border-subtle)';
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
                  }
                  else if (selected) {
                    bg = 'var(--accent-bg)';
                    border = '2px solid var(--accent)';
                    boxShadow = '0 0 10px var(--accent-bg)';
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={34} style={{ outline: '1px solid var(--accent)', outlineOffset: 2 }} />;
                  }
                  else if (previewed) {
                    const drafter = draftState.members.find(m => m.userId === validPreview!.userId);
                    bg = '#1e1430';
                    border = '1px dashed var(--accent-border)';
                    content = (
                      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
                        <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={26} />
                        <span style={{ color: 'var(--text-muted)', fontSize: 7, lineHeight: 1 }}>
                          {drafter?.displayName.split(' ')[0] ?? ''}
                        </span>
                      </div>
                    );
                  }
                  else {
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} />;
                  }

                  return (
                    <td key={cap}>
                      <div
                        onClick={() => handleCellClick(c.id, cap)}
                        style={{
                          width: cellWidth,
                          height: 44,
                          background: bg,
                          border,
                          borderRadius: 4,
                          boxShadow,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          cursor,
                          opacity: isLobby ? 0.45 : 1,
                          userSelect: 'none',
                          transition: 'background 0.1s',
                          pointerEvents: gridLocked ? 'none' : 'auto',
                        }}
                      >
                        {content}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };
  ```

  Note: the "Pick board locks" message now uses `position: 'absolute'` so it doesn't push the table down — it floats over the top of the grid panel. If this overlaps uncomfortably, change to a `marginBottom: 8` and remove `absolute`. The exact position can be adjusted to taste.

- [ ] **Step 2: Verify grid polish**

  Open the draft room (InProgress state recommended):
  - Available cells are grey (#1c1f2c), not green
  - Caption headers show short labels (GE, GE-Vis, etc.) at 10px
  - Grid is horizontally centred in the panel (not left-aligned)
  - With 5 captions: 7px gap between columns; with 3 captions: 18px gap

- [ ] **Step 3: Run the TypeScript check**
  ```bash
  cd DCF.Web && npm run build
  ```
  Expected: no TypeScript errors.

- [ ] **Step 4: Commit**
  ```bash
  git add DCF.Web/src/pages/DraftRoom.tsx
  git commit -m "feat: grey cells, caption shorthands, centred grid, generous column spacing"
  ```

---

## Self-Review Notes

- **Spec §1 (AuthenticatedLayout):** Task 1 covers it. Height `calc(100vh - 92px)` = 44px nav + 48px padding. ✓
- **Spec §2 (Combined bar, all states):** Task 3 Step 2 implements all 8 bar states in `renderBar`. ✓
- **Spec §3 (Grey cells):** Task 4 changes `bg = '#1c1f2c'` and `border = '1px solid var(--border)'` for available cells. ✓
- **Spec §4 (Caption shorthands, 10px):** Task 2 adds `CAPTION_SHORT`; Task 4 uses it in `<th>` at `fontSize: 10`. ✓
- **Spec §5 (Centred grid):** Task 4 adds `display: 'flex', justifyContent: 'center'` to grid container. ✓
- **Spec §6 (Column spacing):** Task 2 adds `H_GAP`; Task 4 computes `hGap` and applies to `borderSpacing`. ✓
- **console.logs:** Task 1 Step 2 removes them. ✓
- **Dead code:** `renderTopBar` and `renderSubmitBar` are deleted in Task 3 Step 2. ✓
- **Type consistency:** `CAPTION_SHORT` defined in Task 2, used in Tasks 3 and 4. `H_GAP` defined in Task 2, used in Task 4. `canSubmit`, `selectionLabel`, `commissionerName` all defined locally inside `renderBar`. ✓
