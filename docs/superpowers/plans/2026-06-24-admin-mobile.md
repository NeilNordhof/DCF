# Admin Mobile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `SeasonDetail.tsx` usable on mobile for the primary use cases of triggering score scrapes and fixing show recap URLs remotely.

**Architecture:** Pure frontend — CSS overrides + class names added to existing JSX, plus one `useState` boolean for the corps panel toggle. No new files. All mobile overrides go inside the existing `@media (max-width: 640px)` block in `index.css`. Non-media base classes (toggle visibility, corps body flex layout) go above that block alongside existing component classes.

**Tech Stack:** React 19, TypeScript, global CSS (`index.css` — no CSS modules, no Tailwind)

## Global Constraints

- Mobile breakpoint: `max-width: 640px` (matches existing site-wide breakpoint in `index.css`)
- Class names: kebab-case, `admin-` prefix for all new classes
- Inline styles stay on their elements — class names are added alongside them, not replacing them
- No backend changes
- No new dependencies
- Branch: `feat/admin-mobile` from `master`

---

### Task 1: Stack layout, collapsible corps panel, full-width publish button

Two-column flex row stacks to single column on mobile (shows first via `order`). Corps panel gets a toggle button (hidden on desktop, visible on mobile). Publish button becomes full-width on mobile.

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/index.css`

- [ ] **Step 1: Create branch**

```bash
git checkout master
git checkout -b feat/admin-mobile
```

- [ ] **Step 2: Add `corpsOpen` state to SeasonDetail**

In `SeasonDetail.tsx`, add alongside the other `useState` declarations near the top of the component (around line 98, after `const [corpsSortInputs, ...]`):

```tsx
const [corpsOpen, setCorpsOpen] = useState(false);
```

- [ ] **Step 3: Add class names to the outer layout and panels**

Find the outer two-column flex row at line 436:
```tsx
// BEFORE
<div style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>

// AFTER
<div className="admin-season-layout" style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
```

Find the corps sidebar at line 437:
```tsx
// BEFORE
<div style={{ flex: '0 0 280px', display: 'flex', flexDirection: 'column', gap: 12 }}>

// AFTER
<div className="admin-corps-panel" style={{ flex: '0 0 280px', display: 'flex', flexDirection: 'column', gap: 12 }}>
```

Find the shows column at line 526:
```tsx
// BEFORE
<div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12 }}>

// AFTER
<div className="admin-shows-panel" style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12 }}>
```

- [ ] **Step 4: Add toggle button and wrap corps body**

Inside the `admin-corps-panel` div, before the existing content (the "Corps this season" label at line 438), add the toggle button and wrap all the existing corps panel children in an `admin-corps-body` div:

```tsx
<div className="admin-corps-panel" style={{ flex: '0 0 280px', display: 'flex', flexDirection: 'column', gap: 12 }}>
  <button
    type="button"
    className="admin-corps-toggle"
    onClick={() => setCorpsOpen(o => !o)}
  >
    <span>Corps &amp; Draft Order</span>
    <span>{corpsOpen ? '▲' : '▼'}</span>
  </button>

  <div className={`admin-corps-body${corpsOpen ? ' open' : ''}`}>
    <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Corps this season</div>
    <form onSubmit={saveCorps}>
      {/* ...unchanged... */}
    </form>
    {seasonCorps.length > 0 && (
      <>
        {/* ...unchanged draft order section... */}
      </>
    )}
  </div>
</div>
```

The `admin-corps-body` wrapper contains everything that was previously a direct child of `admin-corps-panel` (the corps-this-season label, the form, and the draft order section). Do not change any of that content.

- [ ] **Step 5: Add class to Publish button**

Find the Publish button around line 416:
```tsx
// BEFORE
<button
  onClick={() => setShowPublishConfirm(true)}
  disabled={publishing}
  style={{
    padding: '7px 16px', borderRadius: 5, fontSize: 11, fontWeight: 800,
    background: publishing ? 'var(--border)' : 'var(--accent)',
    color: publishing ? 'var(--text-faint)' : 'var(--bg)',
    border: 'none', cursor: publishing ? 'not-allowed' : 'pointer',
  }}
>

// AFTER
<button
  onClick={() => setShowPublishConfirm(true)}
  disabled={publishing}
  className="admin-publish-btn"
  style={{
    padding: '7px 16px', borderRadius: 5, fontSize: 11, fontWeight: 800,
    background: publishing ? 'var(--border)' : 'var(--accent)',
    color: publishing ? 'var(--text-faint)' : 'var(--bg)',
    border: 'none', cursor: publishing ? 'not-allowed' : 'pointer',
  }}
>
```

- [ ] **Step 6: Add base CSS (non-media) to index.css**

Add these rules to `index.css` just before the `/* === Mobile === */` comment, alongside the existing component classes like `.draft-bar-submit-row`:

```css
.admin-corps-toggle {
  display: none;
}

.admin-corps-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
```

- [ ] **Step 7: Add mobile CSS overrides to index.css**

Inside the existing `@media (max-width: 640px)` block, add after the existing rules:

```css
  .admin-season-layout {
    flex-direction: column;
  }

  .admin-corps-panel {
    flex: none !important;
    width: 100%;
    order: 2;
  }

  .admin-shows-panel {
    order: 1;
  }

  .admin-corps-toggle {
    display: flex;
    width: 100%;
    padding: 10px 14px;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 5px;
    cursor: pointer;
    font-size: 11px;
    font-weight: 600;
    color: var(--text-heading);
    justify-content: space-between;
    align-items: center;
  }

  .admin-corps-body {
    display: none;
  }

  .admin-corps-body.open {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .admin-publish-btn {
    width: 100%;
    margin-top: 8px;
  }
```

- [ ] **Step 8: Verify in browser**

Run `npm run dev` in `DCF.Web/`. Navigate to a season detail page.

In browser devtools at **390px** width (mobile):
- Shows panel appears above the corps panel
- "Corps & Draft Order" toggle button is visible below the shows
- Clicking toggle reveals the corps chip selector and draft order — clicking again hides it
- If the season is unpublished with corps selected, Publish button is full-width below the title

At **1024px** width (desktop):
- Two-column layout is intact (corps sidebar left, shows right)
- Toggle button is not visible
- Corps content is always visible
- Publish button is its normal size

- [ ] **Step 9: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/index.css
git commit -m "feat: stack admin season layout and collapsible corps panel on mobile"
```

---

### Task 2: Stack show form rows on mobile

The Date/TZ row and Start/Scores row in both the **Add Show** and **Edit Show** forms each contain two label+input pairs inline. On mobile they need to stack, one pair per line.

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Consumes: nothing from Task 1
- Produces: `admin-show-form-row`, `admin-show-form-pair` CSS classes

Note: wrapping label+input pairs in `.admin-show-form-pair` divs adds an intermediate wrapper. On desktop, each pair becomes a `flex: 1` flex item in the row (same visual result as before). On mobile both pairs stack full-width.

- [ ] **Step 1: Update Date/TZ and Start/Scores rows in the Add Show form**

Find the Add Show form inside the `addShowOpen && (...)` block around line 551. Replace the Date/TZ row (currently lines 569-576) and Start/Scores row (currently lines 577-582):

```tsx
{/* Date / TZ */}
<div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
  <div className="admin-show-form-pair">
    <label style={labelStyle}>Date</label>
    <input type="date" value={showDate} onChange={e => setShowDate(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
  </div>
  <div className="admin-show-form-pair">
    <label style={labelStyle}>TZ</label>
    <select value={showTz} onChange={e => setShowTz(e.target.value)} style={{ ...inputStyle, width: 62 }}>
      {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
    </select>
  </div>
</div>

{/* Start / Scores */}
<div className="admin-show-form-row" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
  <div className="admin-show-form-pair">
    <label style={labelStyle}>Start</label>
    <TimePicker value={showStartTime} onChange={setShowStartTime} style={{ flex: 1 }} />
  </div>
  <div className="admin-show-form-pair">
    <label style={labelStyle}>Scores</label>
    <TimePicker value={showScoresTime} onChange={setShowScoresTime} required style={{ flex: 1 }} />
  </div>
</div>
```

- [ ] **Step 2: Update the same rows in the Edit Show form**

Find the Edit Show form inside the `!started && !hasScoresAnnounced(s)` block around line 641. Replace the Date/TZ row (currently lines 650-657) and Start/Scores row (currently lines 658-663):

```tsx
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
    <label style={labelStyle}>Scores</label>
    <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
  </div>
</div>
```

- [ ] **Step 3: Add base CSS (non-media) to index.css**

Add after the `.admin-corps-body` rule added in Task 1:

```css
.admin-show-form-pair {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
}
```

- [ ] **Step 4: Add mobile CSS override to index.css**

Inside the `@media (max-width: 640px)` block, add after the rules from Task 1:

```css
  .admin-show-form-row {
    flex-wrap: wrap;
  }

  .admin-show-form-pair {
    flex: 1 1 100%;
  }
```

- [ ] **Step 5: Verify in browser**

At **390px** width:
- Expand a show card that has not started (edit form visible):
  - Date input occupies its own full-width row
  - TZ selector occupies its own full-width row below that
  - Start time picker occupies its own full-width row
  - Scores time picker occupies its own full-width row
- Expand the Add Show form and confirm the same stacking

At **1024px** width:
- Date + TZ appear side by side on one line
- Start + Scores appear side by side on one line
- (Matches original desktop appearance)

- [ ] **Step 6: Commit and push**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/index.css
git commit -m "feat: stack show form rows on mobile"
git push -u origin feat/admin-mobile
```

Open a PR targeting `master`.
