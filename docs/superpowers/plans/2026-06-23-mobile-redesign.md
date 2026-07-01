# Mobile Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the DCF frontend usable on mobile phones using CSS `@media` queries at a single 640px breakpoint.

**Architecture:** All responsive rules go in a `/* === Mobile === */` block at the bottom of `index.css`. Components add `className` props alongside existing inline styles — the two coexist because they serve different jobs (layout vs. runtime state).

**Tech Stack:** React 19, TypeScript, Vite, plain CSS (`@media` queries)

## Global Constraints

- Single breakpoint: `max-width: 640px`
- All `@media` rules go inside `/* === Mobile === */` at the bottom of `DCF.Web/src/index.css`
- Inline `style` props remain for runtime/data-driven values; `className` is for structural layout overrides only
- Admin pages (`/admin/*`) are out of scope

---

### Task 1: Foundation — mobile CSS block + AuthenticatedLayout padding

**Files:**
- Note: `DCF.Web/index.html` already has `<meta name="viewport" content="width=device-width, initial-scale=1.0">` — no change needed
- Modify: `DCF.Web/src/index.css`
- Modify: `DCF.Web/src/App.tsx`

**Interfaces:**
- Produces: `/* === Mobile === */` block in `index.css`; `.page-content` class used by all subsequent tasks

- [ ] **Step 1: Add the mobile CSS block to `index.css`**

At the very end of `DCF.Web/src/index.css`, after all existing rules, add:

```css
/* === Mobile === */

@media (max-width: 640px) {
  .page-content {
    padding: 16px 12px;
  }
}
```

- [ ] **Step 2: Add `className="page-content"` to the content div in `AuthenticatedLayout`**

In `DCF.Web/src/App.tsx`, change:

```tsx
      <div style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '24px 20px', boxSizing: 'border-box' }}>
```

to:

```tsx
      <div className="page-content" style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '24px 20px', boxSizing: 'border-box' }}>
```

- [ ] **Step 3: Verify**

Run `npm run dev` inside `DCF.Web/`. In Chrome DevTools, set device to 375px wide. Navigate to `/leagues`. Content padding should be `16px 12px` instead of `24px 20px`.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/index.css DCF.Web/src/App.tsx
git commit -m "feat: add mobile CSS foundation and page-content padding"
```

---

### Task 2: Nav — hamburger menu

**Files:**
- Modify: `DCF.Web/src/components/Nav.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Produces: `.nav-links`, `.nav-hamburger`, `.nav-mobile-menu`, `.nav-mobile-menu.open` CSS classes; hamburger toggle in `Nav`

- [ ] **Step 1: Add global and mobile nav CSS to `index.css`**

Add these rules outside the `@media` block (global, applies at all sizes):

```css
.nav-hamburger {
  display: none;
}

.nav-mobile-menu {
  position: absolute;
  top: 44px;
  left: 0;
  right: 0;
  background: var(--surface);
  border-bottom: 1px solid var(--border);
  z-index: 100;
}

.nav-mobile-menu a,
.nav-mobile-menu button {
  display: flex;
  align-items: center;
  min-height: 44px;
  padding: 0 20px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-heading);
  text-decoration: none;
  background: none;
  border: none;
  border-bottom: 1px solid var(--border-subtle);
  cursor: pointer;
  letter-spacing: 0.5px;
  text-transform: uppercase;
  width: 100%;
  box-sizing: border-box;
  text-align: left;
}

.nav-mobile-menu a:last-child,
.nav-mobile-menu button:last-child {
  border-bottom: none;
}
```

Add inside the `@media (max-width: 640px)` block:

```css
  .nav-links {
    display: none !important;
  }

  .nav-hamburger {
    display: flex;
  }

  .nav-mobile-menu {
    display: none;
  }

  .nav-mobile-menu.open {
    display: flex;
    flex-direction: column;
  }
```

- [ ] **Step 2: Rewrite `Nav.tsx`**

Replace the entire content of `DCF.Web/src/components/Nav.tsx` with:

```tsx
import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useUser } from '../context/UserContext';

export function Nav() {
  const { user } = useUser();
  const { logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const isAdmin = location.pathname.startsWith('/admin');
  const [menuOpen, setMenuOpen] = useState(false);

  const initials = user?.displayName
    ? user.displayName.split(' ').filter(Boolean).map((w: string) => w[0]).join('').slice(0, 2).toUpperCase()
    : '?';

  const linkStyle = (prefix: string): CSSProperties => ({
    fontSize: 11,
    color: location.pathname.startsWith(prefix) ? 'var(--accent)' : 'var(--text-muted)',
    textDecoration: 'none',
    fontWeight: 600,
    letterSpacing: '0.5px',
    paddingBottom: 2,
    borderBottom: location.pathname.startsWith(prefix) ? '2px solid var(--accent)' : '2px solid transparent',
  });

  useEffect(() => {
    if (!menuOpen) {
      return;
    }

    function handleOutsideClick() {
      setMenuOpen(false);
    }

    document.addEventListener('click', handleOutsideClick);

    return () => document.removeEventListener('click', handleOutsideClick);
  }, [menuOpen]);

  function closeMenu() {
    setMenuOpen(false);
  }

  return (
    <nav style={{
      background: 'var(--surface)',
      borderBottom: '1px solid var(--border)',
      height: 44,
      display: 'flex',
      alignItems: 'center',
      padding: '0 20px',
      gap: 20,
      flexShrink: 0,
      position: 'relative',
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 20, flex: 1 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Link to="/leagues" style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px', textDecoration: 'none' }}>
            DCF - Drum Corps Fantasy
          </Link>
          {isAdmin && (
            <span style={{
              fontSize: 8,
              padding: '2px 6px',
              background: 'var(--surface-elevated)',
              color: 'var(--text-muted)',
              borderRadius: 4,
              fontWeight: 700,
              letterSpacing: '0.5px',
              textTransform: 'uppercase',
            }}>
              ADMIN
            </span>
          )}
        </div>
        <Link to="/leagues" className="nav-links" style={linkStyle('/leagues')}>LEAGUES</Link>
      </div>
      <div className="nav-links" style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
        {user?.isAdmin && (
          <Link to="/admin" style={linkStyle('/admin')}>ADMIN</Link>
        )}
        <Link to="/profile" style={linkStyle('/profile')}>PROFILE</Link>
        <button
          onClick={() => { logout(); navigate('/'); }}
          title="Switch user"
          style={{
            width: 28,
            height: 28,
            borderRadius: '50%',
            background: 'var(--accent)',
            color: 'var(--bg)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 11,
            fontWeight: 700,
            flexShrink: 0,
            border: 'none',
            cursor: 'pointer',
          }}
        >
          {initials}
        </button>
      </div>
      <button
        className="nav-hamburger"
        onClick={e => { e.stopPropagation(); setMenuOpen(m => !m); }}
        aria-label="Toggle menu"
        style={{
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: 4,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
          width: 20,
          height: 16,
          flexShrink: 0,
        }}
      >
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
        <div style={{ width: '100%', height: 2, background: 'var(--text-heading)', borderRadius: 1 }} />
      </button>
      <div
        className={`nav-mobile-menu${menuOpen ? ' open' : ''}`}
        onClick={e => e.stopPropagation()}
      >
        <Link to="/leagues" onClick={closeMenu}>LEAGUES</Link>
        {user?.isAdmin && (
          <Link to="/admin" onClick={closeMenu}>ADMIN</Link>
        )}
        <Link to="/profile" onClick={closeMenu}>PROFILE</Link>
        <button onClick={() => { logout(); navigate('/'); closeMenu(); }}>Logout</button>
      </div>
    </nav>
  );
}
```

- [ ] **Step 3: Verify**

At 375px in DevTools:
- Nav links (LEAGUES) and the right group (ADMIN / PROFILE / avatar) are hidden
- Hamburger icon (three lines) appears on the right
- Tapping it opens a full-width dropdown below the nav with LEAGUES, PROFILE, (ADMIN if `user.isAdmin`), and Logout — each row at least 44px tall
- Tapping any item navigates and the menu closes
- Tapping anywhere outside the menu closes it

At desktop width (> 640px): hamburger is hidden, all nav links and avatar are visible as before.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/components/Nav.tsx DCF.Web/src/index.css
git commit -m "feat: add hamburger menu for mobile nav"
```

---

### Task 3: Home page

**Files:**
- Modify: `DCF.Web/src/pages/Home.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Produces: `.home-split-card`, `.home-brand-panel`, `.home-brand-body`, `.home-auth-panel` CSS classes

- [ ] **Step 1: Add CSS to `index.css`**

Add inside the `@media (max-width: 640px)` block:

```css
  .home-split-card {
    flex-direction: column;
    min-height: auto;
  }

  .home-brand-panel {
    flex: none;
    padding: 24px 20px;
  }

  .home-brand-body {
    display: none;
  }

  .home-auth-panel {
    flex: none;
    border-left: none;
    border-top: 1px solid var(--border);
  }
```

- [ ] **Step 2: Add `className` props to the split card elements in `Home.tsx`**

Change the outer split card div (the one with `minHeight: 480`):

```tsx
      <div style={{
        display: 'flex',
        width: '100%',
        maxWidth: 780,
        border: '1px solid var(--border)',
        borderRadius: 8,
        overflow: 'hidden',
        position: 'relative',
        zIndex: 1,
        minHeight: 480,
      }}>
```

to:

```tsx
      <div className="home-split-card" style={{
        display: 'flex',
        width: '100%',
        maxWidth: 780,
        border: '1px solid var(--border)',
        borderRadius: 8,
        overflow: 'hidden',
        position: 'relative',
        zIndex: 1,
        minHeight: 480,
      }}>
```

Change the left brand panel div (with `flex: '1 1 340px'`):

```tsx
        <div style={{
          flex: '1 1 340px',
          background: 'linear-gradient(135deg, #1a0e2e, var(--surface))',
          padding: 40,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 24,
        }}>
```

to:

```tsx
        <div className="home-brand-panel" style={{
          flex: '1 1 340px',
          background: 'linear-gradient(135deg, #1a0e2e, var(--surface))',
          padding: 40,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 24,
        }}>
```

Inside the brand panel, wrap the second `<div>` (the one containing `<h1>` and `<p>`) with `className="home-brand-body"`. Change:

```tsx
          <div>
            <h1 style={{ fontSize: 19, fontWeight: 800, color: 'var(--text-heading)', lineHeight: 1.35, marginBottom: 10 }}>
              Draft corps.<br />Score points.<br />Win the season.
            </h1>
            <p style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.65 }}>
              The fantasy league built for Drum Corps International fans. Join a league with your friends and drafft captions from your favourite corps. Track real DCI scores and compete all season long to see who has the best fantasy corps.
            </p>
          </div>
```

to:

```tsx
          <div className="home-brand-body">
            <h1 style={{ fontSize: 19, fontWeight: 800, color: 'var(--text-heading)', lineHeight: 1.35, marginBottom: 10 }}>
              Draft corps.<br />Score points.<br />Win the season.
            </h1>
            <p style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.65 }}>
              The fantasy league built for Drum Corps International fans. Join a league with your friends and drafft captions from your favourite corps. Track real DCI scores and compete all season long to see who has the best fantasy corps.
            </p>
          </div>
```

Change the right auth panel div (with `flex: '0 0 340px'`):

```tsx
        <div style={{
          flex: '0 0 340px',
          background: 'var(--surface-2)',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          minHeight: 480,
        }}>
```

to:

```tsx
        <div className="home-auth-panel" style={{
          flex: '0 0 340px',
          background: 'var(--surface-2)',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          minHeight: 480,
        }}>
```

- [ ] **Step 3: Verify**

At 375px in DevTools, navigate to `/` (Home):
- Split card stacks vertically: brand panel on top, auth panel below
- Brand panel shows only the DCF logo + tagline; the `h1` and `<p>` are hidden
- Brand panel padding is `24px 20px`
- Auth panel has `border-top` (horizontal divider) instead of `border-left`

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/pages/Home.tsx DCF.Web/src/index.css
git commit -m "feat: responsive home page split card for mobile"
```

---

### Task 4: Leagues page

**Files:**
- Modify: `DCF.Web/src/pages/Leagues.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Produces: `.league-card-badge`, `.league-browse-row`, `.league-browse-name` CSS classes

- [ ] **Step 1: Add CSS to `index.css`**

Add this rule outside the media query (global — harmless on desktop):

```css
.league-card-badge {
  flex-shrink: 0;
  margin-left: 8px;
}
```

Add inside the `@media (max-width: 640px)` block:

```css
  .league-browse-row {
    flex-wrap: wrap;
    gap: 6px;
  }

  .league-browse-name {
    flex: 1;
    min-width: 120px;
  }
```

- [ ] **Step 2: Wrap `StatusBadge` in `LeagueCard` with `.league-card-badge`**

In `Leagues.tsx`, in the `LeagueCard` function, find:

```tsx
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)' }}>{league.name}</span>
        <StatusBadge status={league.draftStatus} />
      </div>
```

Change to:

```tsx
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)' }}>{league.name}</span>
        <div className="league-card-badge">
          <StatusBadge status={league.draftStatus} />
        </div>
      </div>
```

- [ ] **Step 3: Add `className` props to the public league rows in `JoinTab`**

In `Leagues.tsx`, in the `JoinTab` function, find the `<Link>` for each public league:

```tsx
              <Link
                key={l.id}
                to={`/leagues/${l.id}`}
                style={{
                  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                  padding: '10px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                  textDecoration: 'none', color: 'inherit',
                }}
              >
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-heading)' }}>{l.name}</span>
```

Change to:

```tsx
              <Link
                key={l.id}
                to={`/leagues/${l.id}`}
                className="league-browse-row"
                style={{
                  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                  padding: '10px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                  textDecoration: 'none', color: 'inherit',
                }}
              >
                <span className="league-browse-name" style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-heading)' }}>{l.name}</span>
```

- [ ] **Step 4: Verify**

At 375px in DevTools:
- My Leagues tab: league card status badge has `flex-shrink: 0; margin-left: 8px` — it doesn't get squished on long league names
- Browse/Join tab: public league rows wrap when the league name is long; the name takes its own line if needed

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/pages/Leagues.tsx DCF.Web/src/index.css
git commit -m "feat: responsive leagues page for mobile"
```

---

### Task 5: LeagueDetail page

**Files:**
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Produces: `.league-header-bar`, `.league-header-actions`, `.invite-code-row`, `.invite-code-value` CSS classes

- [ ] **Step 1: Add CSS to `index.css`**

Add inside the `@media (max-width: 640px)` block:

```css
  .league-header-bar {
    flex-direction: column;
    align-items: flex-start;
  }

  .league-header-actions {
    align-items: flex-start;
    flex-wrap: wrap;
  }

  .invite-code-row {
    flex-wrap: wrap;
  }

  .invite-code-value {
    flex: 1 1 100%;
  }
```

- [ ] **Step 2: Add `className="league-header-bar"` to the header div**

In `LeagueDetail.tsx`, find the header bar div (the one with `background: 'var(--surface)'` and `justifyContent: 'space-between'` and `gap: 16`):

```tsx
      <div style={{
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: 6,
        padding: '16px 20px',
        marginBottom: 0,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 16,
      }}>
```

Change to:

```tsx
      <div className="league-header-bar" style={{
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: 6,
        padding: '16px 20px',
        marginBottom: 0,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 16,
      }}>
```

- [ ] **Step 3: Add `className="league-header-actions"` to the right-side actions div**

In `LeagueDetail.tsx`, inside the header bar, find:

```tsx
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {statusBadge()}
```

Change to:

```tsx
        <div className="league-header-actions" style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {statusBadge()}
```

- [ ] **Step 4: Add `className` props to the invite code row**

In `LeagueDetail.tsx`, in `renderInfoTab()`, find the invite code container div:

```tsx
            <div style={{
              display: 'flex', alignItems: 'center', gap: 10,
              padding: '10px 14px', background: 'var(--surface-2)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--accent)', flex: 1, letterSpacing: '0.5px' }}>
                {league.inviteCode}
              </span>
```

Change to:

```tsx
            <div className="invite-code-row" style={{
              display: 'flex', alignItems: 'center', gap: 10,
              padding: '10px 14px', background: 'var(--surface-2)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <span className="invite-code-value" style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--accent)', flex: 1, letterSpacing: '0.5px' }}>
                {league.inviteCode}
              </span>
```

- [ ] **Step 5: Verify**

At 375px in DevTools, navigate to a league detail page:
- Header bar stacks vertically: league name/meta on top, badge + action buttons below
- Action buttons align to the left and wrap if needed
- In the Info tab: invite code takes its own full line; Copy and Refresh buttons sit below it

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/LeagueDetail.tsx DCF.Web/src/index.css
git commit -m "feat: responsive LeagueDetail header and invite code for mobile"
```

---

### Task 6: DraftRoom page

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`
- Modify: `DCF.Web/src/index.css`

**Interfaces:**
- Produces: `mobileView` state (`'board' | 'more'`); `.draft-bar`, `.draft-bar-league-name`, `.draft-bar-separator`, `.draft-bar-submit-row`, `.draft-mobile-toggle`, `.draft-grid-panel`, `.draft-side-panel` CSS classes

**Layout restructuring note:** The current layout has the bar inside the left flex column. This task moves the bar and mobile toggle outside both panels, so the toggle is always visible on mobile regardless of which view is active.

- [ ] **Step 1: Add CSS to `index.css`**

Add these rules outside the `@media` block (global):

```css
.draft-mobile-toggle {
  display: none;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  flex-shrink: 0;
}

.draft-mobile-toggle button {
  flex: 1;
  padding: 10px 0;
  font-size: 11px;
  font-weight: 600;
  cursor: pointer;
  background: transparent;
  border: none;
  color: var(--text-muted);
  border-bottom: 2px solid transparent;
}

.draft-mobile-toggle button.active {
  color: var(--accent);
  border-bottom: 2px solid var(--accent);
}
```

Add inside the `@media (max-width: 640px)` block:

```css
  .draft-bar {
    flex-wrap: wrap;
  }

  .draft-bar-league-name {
    display: none;
  }

  .draft-bar-separator {
    display: none;
  }

  .draft-bar-submit-row {
    flex: 1 1 100%;
    display: flex;
    align-items: center;
    gap: 10px;
    padding-top: 6px;
    border-top: 1px solid rgba(255, 255, 255, 0.06);
  }

  .draft-mobile-toggle {
    display: flex;
  }

  [data-mobile-view="more"] .draft-grid-panel {
    display: none;
  }

  [data-mobile-view="board"] .draft-side-panel {
    display: none;
  }

  [data-mobile-view="more"] .draft-side-panel {
    width: 100%;
    border-left: none;
  }
```

- [ ] **Step 2: Add `mobileView` state to `DraftRoom`**

In `DraftRoom.tsx`, add after the `const [error, setError] = useState<string | null>(null);` line:

```tsx
  const [mobileView, setMobileView] = useState<'board' | 'more'>('board');
```

- [ ] **Step 3: Add `className` props to the bar elements in `renderBar()`**

Add `className="draft-bar"` to the outer bar div return. Change:

```tsx
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
```

to:

```tsx
    return (
      <div className="draft-bar" style={{
        background: barBg,
        borderBottom: `2px solid ${barAccent}`,
        padding: '10px 16px',
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        flexShrink: 0,
      }}>
```

Add `className="draft-bar-separator"` to the first separator div (after the `← League` link):

```tsx
        <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-heading)', flexShrink: 0 }}>
```

Change the first div to:

```tsx
        <div className="draft-bar-separator" style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-heading)', flexShrink: 0 }}>
```

Add `className="draft-bar-league-name"` to the league name div:

```tsx
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-heading)', flexShrink: 0 }}>
          {league.name}
        </div>
        <div style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
```

Change to:

```tsx
        <div className="draft-bar-league-name" style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-heading)', flexShrink: 0 }}>
          {league.name}
        </div>
        <div className="draft-bar-separator" style={{ width: 1, height: 32, background: 'var(--border)', flexShrink: 0 }} />
```

- [ ] **Step 4: Wrap the submit section in `.draft-bar-submit-row` in `renderStatus()`**

There are two `isMyTurn` blocks in `renderStatus()` — one in the makeup phase branch and one in the main InProgress branch. Apply the same change to both.

**In the makeup phase branch** (inside `if (inMakeupPhase)`), change:

```tsx
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
```

to:

```tsx
              {isMyTurn && (
                <div className="draft-bar-submit-row">
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
                </div>
              )}
```

**In the main InProgress branch** (the second `return` block), change:

```tsx
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
```

to:

```tsx
            {isMyTurn && (
              <div className="draft-bar-submit-row">
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
              </div>
            )}
```

- [ ] **Step 5: Restructure the DraftRoom layout**

The current layout `return` (starting at line 646) is:

```tsx
  return (
    <>
      <Nav />
      <div style={{ height: 'calc(100vh - 44px)', overflow: 'hidden', background: 'var(--bg)', color: 'var(--text)' }}>
        <div style={{ maxWidth: 1200, width: '100%', height: '100%', margin: '0 auto', padding: '0 20px', boxSizing: 'border-box', display: 'flex' }}>
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
```

Replace with:

```tsx
  return (
    <>
      <Nav />
      <div style={{ height: 'calc(100vh - 44px)', overflow: 'hidden', background: 'var(--bg)', color: 'var(--text)', display: 'flex', flexDirection: 'column' }}>
        {renderBar()}
        <div className="draft-mobile-toggle">
          <button
            className={mobileView === 'board' ? 'active' : ''}
            onClick={() => setMobileView('board')}
          >
            Draft Board
          </button>
          <button
            className={mobileView === 'more' ? 'active' : ''}
            onClick={() => setMobileView('more')}
          >
            More
          </button>
        </div>
        <div data-mobile-view={mobileView} style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '0 20px', boxSizing: 'border-box', display: 'flex', overflow: 'hidden' }}>
          {/* Left — grid panel */}
          <div className="draft-grid-panel" style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
            {renderGrid()}
          </div>
          {/* Right — side panel */}
          <div className="draft-side-panel" style={{ width: 280, background: 'var(--surface-2)', borderLeft: '1px solid var(--border)', display: 'flex', flexDirection: 'column', flexShrink: 0 }}>
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
```

- [ ] **Step 6: Verify**

At 375px in DevTools, navigate to a draft room:
- Status bar is visible at the top; league name and both separator lines are hidden
- When it's the user's turn (simulate or observe): Selected + Submit Pick appear on a second line below the status content, separated by a subtle top border
- "Draft Board" / "More" tab buttons appear below the status bar
- Tapping "More" hides the pick grid and shows the side panel (Draft Order / Picks) full-width with no left border
- Tapping "Draft Board" shows the pick grid and hides the side panel
- The grid retains its horizontal scroll when captions exceed screen width

At desktop width (> 640px): mobile toggle is hidden, grid and side panel are both shown side-by-side as before.

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/pages/DraftRoom.tsx DCF.Web/src/index.css
git commit -m "feat: responsive DraftRoom layout for mobile"
```
