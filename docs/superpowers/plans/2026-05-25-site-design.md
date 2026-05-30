# Site Design Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the dark sports-app design from `docs/superpowers/specs/2026-05-24-site-design.md` across all pages of the DCF Fantasy React frontend, plus a new standings breakdown backend endpoint for the League Detail Scores tab.

**Architecture:** Replace `index.css` with always-dark CSS custom-property tokens, create a shared `Nav` component inserted via `AuthenticatedLayout` in `App.tsx`, restyle every page with inline styles referencing those CSS variables (same pattern as the existing `DraftRoom.tsx`), and add one new backend endpoint (`GET /api/leagues/:id/standings/breakdown`) to power the Scores tab.

**Tech Stack:** React 19, TypeScript, Vite, ASP.NET Core 10, auth0-lock (new), @auth0/auth0-react (existing), EF Core + PostgreSQL

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `DCF.Web/src/index.css` | Modify | Replace tokens with always-dark design system |
| `DCF.Web/src/App.css` | Modify | Clear Vite boilerplate |
| `DCF.Web/vite.config.ts` | Modify | Add `define: { global: 'window' }` for auth0-lock |
| `DCF.Web/src/components/Nav.tsx` | Create | Top nav bar shared across authenticated pages |
| `DCF.Web/src/App.tsx` | Modify | Add `AuthenticatedLayout` with Nav; exclude DraftRoom |
| `DCF.Web/src/pages/Home.tsx` | Modify | Split-card landing page with Auth0 Lock inline |
| `DCF.Web/src/pages/Leagues.tsx` | Modify | Featured league card + list + empty state |
| `DCF.Web/src/pages/LeagueDetail.tsx` | Modify | Header bar + tabs (Home, Scores, Members, Picks, Info) |
| `DCF.Web/src/components/LeagueScoresTab.tsx` | Create | Scores spreadsheet tab (uses breakdown endpoint) |
| `DCF.Api/Services/IStandingsService.cs` | Modify | Add `GetScoreBreakdownAsync` method signature |
| `DCF.Api/Services/StandingsService.cs` | Modify | Implement breakdown computation |
| `DCF.Api/Controllers/LeaguesController.cs` | Modify | Add `GET {id}/standings/breakdown` action |
| `DCF.Web/src/types/api.ts` | Modify | Add `PlayerScoreBreakdown` types |
| `DCF.Web/src/api/client.ts` | Modify | Add `getScoreBreakdown` method |
| `DCF.Web/src/pages/LeagueCreate.tsx` | Modify | Caption chip grid + full dark restyling |
| `DCF.Web/src/pages/Admin.tsx` | Modify | Styled admin panel with tab pattern |
| `DCF.Web/src/pages/SeasonDetail.tsx` | Modify | Two-panel styled season detail |
| `DCF.Web/src/pages/DraftRoom.tsx` | Modify | Remove `Scheduled` redirect; add lobby for Scheduled state |

---

## Task 1: Design System — CSS Tokens + App Reset

**Files:**
- Modify: `DCF.Web/src/index.css`
- Modify: `DCF.Web/src/App.css`
- Modify: `DCF.Web/vite.config.ts`

- [ ] **Step 1: Replace `DCF.Web/src/index.css` entirely**

```css
:root {
  --bg: #0d0f14;
  --surface: #161822;
  --surface-2: #0f1117;
  --border: #2a2d3a;
  --border-subtle: #1e2030;
  --text: #9ca3af;
  --text-h: #f3f4f6;
  --text-muted: #6b7280;
  --text-faint: #4b5563;
  --accent: #c084fc;
  --accent-bg: #3b0764;
  --accent-border: #c084fc55;
  --green: #4ade80;
  --green-bg: #052e16;
  --green-border: #166534;

  --sans: system-ui, 'Segoe UI', Roboto, sans-serif;
  --mono: ui-monospace, Consolas, monospace;

  font: 11px/160% var(--sans);
  color: var(--text);
  background: var(--bg);
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

*, *::before, *::after {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
}

#root {
  min-height: 100svh;
  display: flex;
  flex-direction: column;
}

h1, h2, h3, h4 {
  margin: 0;
  color: var(--text-h);
  font-family: var(--sans);
}

p {
  margin: 0;
}

button {
  font-family: var(--sans);
  cursor: pointer;
}

input, select, textarea {
  font-family: var(--sans);
  font-size: 11px;
}
```

- [ ] **Step 2: Replace `DCF.Web/src/App.css` with a comment**

```css
/* Vite template boilerplate removed — design system lives in index.css */
```

- [ ] **Step 3: Update `DCF.Web/vite.config.ts` to define `global` for auth0-lock compatibility**

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    sourcemap: true,
  },
  define: {
    global: 'window',
  },
})
```

- [ ] **Step 4: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: Build completes with no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/index.css DCF.Web/src/App.css DCF.Web/vite.config.ts
git commit -m "feat: apply always-dark design system tokens"
```

---

## Task 2: Nav Component + AuthenticatedLayout

**Files:**
- Create: `DCF.Web/src/components/Nav.tsx`
- Modify: `DCF.Web/src/App.tsx`

- [ ] **Step 1: Create `DCF.Web/src/components/Nav.tsx`**

```tsx
import { Link, useLocation } from 'react-router-dom';
import { useUser } from '../context/UserContext';

export function Nav() {
  const { user } = useUser();
  const location = useLocation();
  const isAdmin = location.pathname.startsWith('/admin');

  const initials = user?.displayName
    ? user.displayName.split(' ').map((w: string) => w[0]).join('').slice(0, 2).toUpperCase()
    : '?';

  const linkStyle = (prefix: string): React.CSSProperties => ({
    fontSize: 11,
    color: location.pathname.startsWith(prefix) ? 'var(--accent)' : 'var(--text-muted)',
    textDecoration: 'none',
    fontWeight: 600,
    letterSpacing: '0.5px',
    paddingBottom: 2,
    borderBottom: location.pathname.startsWith(prefix) ? '2px solid var(--accent)' : '2px solid transparent',
  });

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
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 1 }}>
        <Link to="/leagues" style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px', textDecoration: 'none' }}>
          DCF FANTASY
        </Link>
        {isAdmin && (
          <span style={{
            fontSize: 8,
            padding: '2px 6px',
            background: '#374151',
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
      <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
        <Link to="/leagues" style={linkStyle('/leagues')}>LEAGUES</Link>
        <Link to="/profile" style={linkStyle('/profile')}>PROFILE</Link>
        <div style={{
          width: 28,
          height: 28,
          borderRadius: '50%',
          background: 'var(--accent)',
          color: '#0d0f14',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontWeight: 700,
          flexShrink: 0,
        }}>
          {initials}
        </div>
      </div>
    </nav>
  );
}
```

- [ ] **Step 2: Replace `DCF.Web/src/App.tsx` entirely**

```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect } from 'react';
import { Route, Routes, useNavigate } from 'react-router-dom';
import { api, setTokenGetter } from './api/client';
import { AdminRoute } from './components/AdminRoute';
import { Nav } from './components/Nav';
import { ProtectedRoute } from './components/ProtectedRoute';
import { useUser } from './context/UserContext';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Onboarding } from './pages/Onboarding';
import { Profile } from './pages/Profile';
import { SeasonDetail } from './pages/SeasonDetail';

function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100svh' }}>
      <Nav />
      <div style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '24px 20px', boxSizing: 'border-box' }}>
        {children}
      </div>
    </div>
  );
}

export default function App() {
  const { getAccessTokenSilently, isAuthenticated } = useAuth0();
  const { setUser } = useUser();
  const navigate = useNavigate();

  useEffect(() => {
    setTokenGetter(() =>
      getAccessTokenSilently({
        authorizationParams: { audience: import.meta.env.VITE_AUTH0_AUDIENCE },
      })
    );
  }, [getAccessTokenSilently]);

  useEffect(() => {
    if (!isAuthenticated) return;

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
      } else {
        navigate('/onboarding');
      }
    }).catch((err) => {
      console.error('Failed to load user profile:', err);
    });
  }, [isAuthenticated, navigate, setUser]);

  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/onboarding" element={<ProtectedRoute><Onboarding /></ProtectedRoute>} />
      <Route path="/leagues" element={<ProtectedRoute><AuthenticatedLayout><Leagues /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/create" element={<ProtectedRoute><AuthenticatedLayout><LeagueCreate /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/:id" element={<ProtectedRoute><AuthenticatedLayout><LeagueDetail /></AuthenticatedLayout></ProtectedRoute>} />
      <Route path="/leagues/:id/draft" element={<ProtectedRoute><DraftRoom /></ProtectedRoute>} />
      <Route path="/admin" element={<AdminRoute><AuthenticatedLayout><Admin /></AuthenticatedLayout></AdminRoute>} />
      <Route path="/admin/seasons/:id" element={<AdminRoute><AuthenticatedLayout><SeasonDetail /></AuthenticatedLayout></AdminRoute>} />
      <Route path="/profile" element={<ProtectedRoute><AuthenticatedLayout><Profile /></AuthenticatedLayout></ProtectedRoute>} />
    </Routes>
  );
}
```

Note: `DraftRoom` is intentionally excluded from `AuthenticatedLayout` — it manages its own full-viewport layout.

- [ ] **Step 3: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: Build completes with no TypeScript errors.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/components/Nav.tsx DCF.Web/src/App.tsx
git commit -m "feat: add Nav component and AuthenticatedLayout wrapper"
```

---

## Task 3: Landing Page with Auth0 Lock

**Files:**
- Modify: `DCF.Web/src/pages/Home.tsx`

The spec requires rendering Auth0 Lock inline in the right panel of a split card. Auth0 Lock handles its own authentication flow — do not use `loginWithRedirect` on this page. The `vite.config.ts` `global: 'window'` added in Task 1 is required for auth0-lock to bundle correctly.

- [ ] **Step 1: Install auth0-lock**

Run: `cd DCF.Web && npm install auth0-lock @types/auth0-lock`
Expected: Package added to `package.json`.

- [ ] **Step 2: Replace `DCF.Web/src/pages/Home.tsx` entirely**

```tsx
import { useEffect, useRef } from 'react';

export function Home() {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const Auth0Lock = (window as any).Auth0Lock ?? require('auth0-lock').default ?? require('auth0-lock');

    const lock = new Auth0Lock(
      import.meta.env.VITE_AUTH0_CLIENT_ID as string,
      import.meta.env.VITE_AUTH0_DOMAIN as string,
      {
        container: 'lock-container',
        auth: {
          redirectUrl: `${window.location.origin}/leagues`,
          responseType: 'code',
          params: { audience: import.meta.env.VITE_AUTH0_AUDIENCE as string },
        },
        theme: { primaryColor: '#c084fc' },
        languageDictionary: { title: '' },
        allowShowPassword: true,
        closable: false,
      }
    );

    lock.show();

    return () => { lock.hide(); };
  }, []);

  return (
    <div style={{
      minHeight: '100svh',
      background: 'var(--bg)',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      padding: 20,
      position: 'relative',
    }}>
      {/* Radial purple glow */}
      <div style={{
        position: 'fixed',
        top: '40%',
        left: '50%',
        transform: 'translate(-50%, -50%)',
        width: 700,
        height: 700,
        background: 'radial-gradient(circle, rgba(192,132,252,0.07) 0%, transparent 70%)',
        pointerEvents: 'none',
      }} />

      {/* Minimal nav */}
      <nav style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        height: 44,
        display: 'flex',
        alignItems: 'center',
        padding: '0 20px',
        zIndex: 10,
      }}>
        <span style={{ color: 'var(--accent)', fontWeight: 700, fontSize: 13, letterSpacing: '0.5px' }}>
          DCF FANTASY
        </span>
      </nav>

      {/* Split card */}
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
        {/* Left — brand panel */}
        <div style={{
          flex: '1 1 340px',
          background: 'linear-gradient(135deg, #1a0e2e, var(--surface))',
          padding: 40,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 24,
        }}>
          <div>
            <div style={{ fontSize: 34, fontWeight: 900, color: 'var(--accent)', letterSpacing: '-0.5px', lineHeight: 1 }}>DCF</div>
            <div style={{ fontSize: 9, fontWeight: 700, color: 'var(--text-faint)', letterSpacing: '1px', textTransform: 'uppercase', marginTop: 2 }}>FANTASY</div>
          </div>
          <div>
            <h1 style={{ fontSize: 19, fontWeight: 800, color: 'var(--text-h)', lineHeight: 1.35, marginBottom: 10 }}>
              Draft corps.<br />Score points.<br />Win the season.
            </h1>
            <p style={{ fontSize: 11, color: 'var(--text)', lineHeight: 1.65 }}>
              The fantasy league built for Drum Corps International fans. Draft your favourite corps, track real DCI scores, and compete all season long.
            </p>
          </div>
          <ul style={{ listStyle: 'none', padding: 0, margin: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
            {[
              'Snake draft with your league',
              'Real DCI scores, auto-updated',
              'Private leagues with invite codes',
            ].map(text => (
              <li key={text} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 11, color: 'var(--text)' }}>
                <span style={{ color: 'var(--accent)', fontSize: 8 }}>●</span>
                {text}
              </li>
            ))}
          </ul>
        </div>

        {/* Right — Auth0 Lock */}
        <div style={{
          flex: '0 0 340px',
          background: '#0f1117',
          borderLeft: '1px solid var(--border)',
          display: 'flex',
          flexDirection: 'column',
          minHeight: 480,
        }}>
          <div id="lock-container" ref={containerRef} style={{ flex: 1, minHeight: 480 }} />
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: Build completes. If `auth0-lock` has type issues, add `// @ts-ignore` above the `require` line.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/pages/Home.tsx DCF.Web/package.json DCF.Web/package-lock.json
git commit -m "feat: landing page with Auth0 Lock split card"
```

---

## Task 4: Leagues Page

**Files:**
- Modify: `DCF.Web/src/pages/Leagues.tsx`

The featured card is shown for the first `isMember` league that is `Open` or `InProgress`. It needs the user's rank + score from `getStandings`. All other member leagues appear in the list below.

- [ ] **Step 1: Replace `DCF.Web/src/pages/Leagues.tsx` entirely**

```tsx
import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useUser } from '../context/UserContext';
import type { League, Standing } from '../types/api';

function StatusBadge({ status }: { status: string }) {
  const isLive = status === 'InProgress';
  const isOpen = status === 'Open';

  if (isLive) {
    return (
      <span style={{
        fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
        textTransform: 'uppercase', letterSpacing: '0.5px',
        background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
      }}>
        LIVE DRAFT
      </span>
    );
  }

  if (isOpen) {
    return (
      <span style={{
        fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
        textTransform: 'uppercase', letterSpacing: '0.5px',
        background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
      }}>
        LOBBY OPEN
      </span>
    );
  }

  return (
    <span style={{
      fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 600,
      textTransform: 'uppercase', letterSpacing: '0.5px',
      border: '1px solid var(--border)', color: 'var(--text-muted)',
    }}>
      {status === 'NotStarted' ? 'NOT STARTED' : status === 'Scheduled' ? 'SCHEDULED' : status === 'Completed' ? 'COMPLETED' : status}
    </span>
  );
}

export function Leagues() {
  const { user } = useUser();
  const navigate = useNavigate();
  const [leagues, setLeagues] = useState<League[]>([]);
  const [featuredStandings, setFeaturedStandings] = useState<Standing[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getLeagues().then(setLeagues).catch(() => setError('Failed to load leagues.'));
  }, []);

  const featured = leagues.find(l => l.isMember && (l.draftStatus === 'Open' || l.draftStatus === 'InProgress'));
  const others = leagues.filter(l => l !== featured);

  useEffect(() => {
    if (!featured) return;
    api.getStandings(featured.id).then(setFeaturedStandings).catch(() => {});
  }, [featured?.id]);

  const userRank = featuredStandings.findIndex(s => s.userId === user?.id) + 1;
  const userScore = featuredStandings.find(s => s.userId === user?.id)?.score ?? 0;

  if (error) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error}</div>;
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)' }}>My Leagues</h2>
        <Link
          to="/leagues/create"
          style={{
            fontSize: 11, fontWeight: 800, padding: '6px 14px', borderRadius: 5,
            background: 'var(--accent)', color: '#0d0f14', textDecoration: 'none',
            letterSpacing: '0.5px',
          }}
        >
          + New
        </Link>
      </div>

      {/* Empty state */}
      {leagues.length === 0 && (
        <div style={{
          textAlign: 'center', padding: '60px 20px',
          border: '1px solid var(--border)', borderRadius: 6, color: 'var(--text-muted)',
        }}>
          <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-h)', marginBottom: 8 }}>No leagues yet</div>
          <div style={{ fontSize: 11, marginBottom: 20 }}>Create a league or ask a commissioner for an invite code.</div>
          <Link
            to="/leagues/create"
            style={{
              fontSize: 11, fontWeight: 800, padding: '7px 16px', borderRadius: 5,
              background: 'var(--accent)', color: '#0d0f14', textDecoration: 'none',
            }}
          >
            Create League
          </Link>
        </div>
      )}

      {/* Featured card */}
      {featured && (
        <div style={{
          background: 'linear-gradient(135deg, #1e1230, var(--surface))',
          border: '1px solid var(--accent-border)',
          borderRadius: 6,
          padding: 24,
        }}>
          <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 20 }}>
            <div>
              <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)', marginBottom: 6 }}>{featured.name}</div>
              <StatusBadge status={featured.draftStatus} />
            </div>
            <button
              onClick={() => navigate(`/leagues/${featured.id}/draft`)}
              style={{
                fontSize: 11, fontWeight: 800, padding: '7px 16px', borderRadius: 5,
                background: 'var(--accent)', color: '#0d0f14', border: 'none',
                letterSpacing: '0.5px',
              }}
            >
              Draft Room →
            </button>
          </div>
          <div style={{ display: 'flex', gap: 20 }}>
            {[
              { label: 'Rank', value: userRank > 0 ? `#${userRank}` : '—' },
              { label: 'Points', value: userScore > 0 ? userScore.toFixed(2) : '—' },
              { label: 'Members', value: String(featured.memberCount ?? '—') },
            ].map(stat => (
              <div key={stat.label} style={{ flex: 1, background: 'rgba(0,0,0,0.25)', borderRadius: 5, padding: '10px 14px' }}>
                <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 4 }}>{stat.label}</div>
                <div style={{ fontSize: 16, fontWeight: 900, color: 'var(--accent)' }}>{stat.value}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Other leagues */}
      {others.length > 0 && (
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 10 }}>
            {featured ? 'Other Leagues' : 'All Leagues'}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {others.map(l => (
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
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-h)' }}>{l.name}</span>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{l.memberCount ?? 0} members</span>
                  <StatusBadge status={l.draftStatus} />
                  <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>›</span>
                </div>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/Leagues.tsx
git commit -m "feat: leagues page with featured card and styled list"
```

---

## Task 5: Standings Breakdown API Endpoint

**Files:**
- Modify: `DCF.Api/Services/IStandingsService.cs`
- Modify: `DCF.Api/Services/StandingsService.cs`
- Modify: `DCF.Api/Controllers/LeaguesController.cs`
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

The Scores tab needs per-player, per-caption, per-corps score data. This is a new endpoint that extends the existing standings computation to return a richer breakdown.

- [ ] **Step 1: Replace `DCF.Api/Services/IStandingsService.cs`**

```csharp
namespace DCF.Api.Services;

public interface IStandingsService
{
    Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId);
    Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId);
}
```

- [ ] **Step 2: Add new records and `GetScoreBreakdownAsync` to `DCF.Api/Services/StandingsService.cs`**

Add these records directly after the existing `MemberStanding` record (line 7 in the current file):

```csharp
public record PickScore(string CorpsName, double? Score);
public record CaptionBreakdown(double Avg, List<PickScore> Picks);
public record MemberScoreBreakdown(Guid UserId, string DisplayName, double TotalScore, Dictionary<string, CaptionBreakdown> Captions);
```

Then add the following method to `StandingsService`, after `GetStandingsAsync`:

```csharp
public async Task<List<MemberScoreBreakdown>> GetScoreBreakdownAsync(Guid leagueId)
{
    var league = await db.Leagues.FindAsync(leagueId)
        ?? throw new ArgumentException("League not found", nameof(leagueId));

    var members = await db.LeagueMembers
        .Include(m => m.User)
        .Where(m => m.LeagueId == leagueId)
        .ToListAsync();

    var allCorps = await db.Corps.ToListAsync();

    var result = new List<MemberScoreBreakdown>();

    foreach (var member in members)
    {
        double totalScore = 0;
        var captionBreakdowns = new Dictionary<string, CaptionBreakdown>();

        foreach (var caption in league.DraftableCaptions)
        {
            var picks = await db.DraftPicks
                .Where(p => p.LeagueId == leagueId &&
                            p.UserId == member.UserId &&
                            p.Caption == caption)
                .ToListAsync();

            var pickScores = new List<PickScore>();
            var captionScores = new List<double>();

            foreach (var pick in picks)
            {
                var corpsName = allCorps.FirstOrDefault(c => c.Id == pick.CorpsId)?.Name ?? "Unknown";
                var score = await GetEffectiveScoreAsync(pick.CorpsId, caption);
                pickScores.Add(new PickScore(corpsName, score));

                if (score.HasValue)
                {
                    captionScores.Add(score.Value);
                }
            }

            var avg = captionScores.Count > 0 ? captionScores.Average() : 0;

            if (captionScores.Count > 0)
            {
                totalScore += avg;
            }

            captionBreakdowns[caption.ToString()] = new CaptionBreakdown(avg, pickScores);
        }

        result.Add(new MemberScoreBreakdown(member.UserId, member.User.DisplayName, totalScore, captionBreakdowns));
    }

    return result.OrderByDescending(r => r.TotalScore).ToList();
}
```

- [ ] **Step 3: Add the breakdown endpoint to `DCF.Api/Controllers/LeaguesController.cs`**

Add this action after the existing `Standings` action (around line 78):

```csharp
[HttpGet("{id}/standings/breakdown")]
public async Task<IActionResult> StandingsBreakdown(Guid id)
{
    try
    {
        var breakdown = await standingsService.GetScoreBreakdownAsync(id);

        return Ok(breakdown);
    }
    catch (ArgumentException)
    {
        return NotFound();
    }
}
```

- [ ] **Step 4: Verify backend builds**

Run: `dotnet build DCF.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Add types to `DCF.Web/src/types/api.ts`**

Append to the bottom of the file:

```ts
export interface PickScore {
  corpsName: string;
  score: number | null;
}

export interface CaptionBreakdown {
  avg: number;
  picks: PickScore[];
}

export interface PlayerScoreBreakdown {
  userId: string;
  displayName: string;
  totalScore: number;
  captions: Record<string, CaptionBreakdown>;
}
```

- [ ] **Step 6: Add `getScoreBreakdown` to `DCF.Web/src/api/client.ts`**

Add after the `getStandings` line:

```ts
getScoreBreakdown: (id: string) => request<PlayerScoreBreakdown[]>(`/api/leagues/${id}/standings/breakdown`),
```

- [ ] **Step 7: Verify frontend build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 8: Commit**

```bash
git add DCF.Api/Services/IStandingsService.cs DCF.Api/Services/StandingsService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: add standings breakdown endpoint for Scores tab"
```

---

## Task 6: League Detail Scores Tab Component

**Files:**
- Create: `DCF.Web/src/components/LeagueScoresTab.tsx`

The Scores tab renders a horizontally-scrollable table with: sticky player column, caption-group headers spanning 3 sub-columns (Corps / Score / Avg), 3 data rows per player per caption group, and a total score column.

- [ ] **Step 1: Create `DCF.Web/src/components/LeagueScoresTab.tsx`**

```tsx
import type { PlayerScoreBreakdown } from '../types/api';

interface Props {
  breakdown: PlayerScoreBreakdown[];
  captions: string[];
  currentUserId?: string;
}

export function LeagueScoresTab({ breakdown, captions, currentUserId }: Props) {
  if (breakdown.length === 0) {
    return (
      <div style={{ padding: 24, color: 'var(--text-muted)', fontSize: 11 }}>
        No scores available yet.
      </div>
    );
  }

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: 10, whiteSpace: 'nowrap' }}>
        <thead>
          {/* Caption group headers */}
          <tr>
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              minWidth: 80, background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '6px 10px',
            }} />
            {captions.map(cap => (
              <th
                key={cap}
                colSpan={3}
                style={{
                  fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
                  color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
                  padding: '6px 4px',
                  borderBottom: '1px solid var(--border)',
                  borderRight: '1px solid var(--border-subtle)',
                }}
              >
                {cap}
              </th>
            ))}
            <th style={{
              fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
              color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
              padding: '6px 8px',
              borderBottom: '1px solid var(--border)',
            }}>
              Total
            </th>
          </tr>
          {/* Sub-headers */}
          <tr>
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '4px 10px',
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600, textAlign: 'left',
            }}>
              Player
            </th>
            {captions.map(cap => (
              ['Corps', 'Score', 'Avg'].map(sub => (
                <th
                  key={`${cap}-${sub}`}
                  style={{
                    fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
                    padding: '4px 8px',
                    borderBottom: '1px solid var(--border)',
                    borderRight: sub === 'Avg' ? '1px solid var(--border-subtle)' : undefined,
                    textAlign: sub === 'Avg' ? 'right' : 'left',
                  }}
                >
                  {sub}
                </th>
              ))
            ))}
            <th style={{
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
              padding: '4px 8px',
              borderBottom: '1px solid var(--border)',
              textAlign: 'right',
            }} />
          </tr>
        </thead>
        <tbody>
          {breakdown.map(player => {
            const isMe = player.userId === currentUserId;
            const maxRows = Math.max(1, ...captions.map(cap => player.captions[cap]?.picks.length ?? 0));
            const rows = Array.from({ length: maxRows }, (_, i) => i);

            return rows.map((rowIdx, i) => {
              const isFirstRow = rowIdx === 0;
              const isLastRow = rowIdx === maxRows - 1;

              return (
                <tr key={`${player.userId}-${rowIdx}`}>
                  {/* Sticky player column — only rendered on the first row of this player */}
                  {isFirstRow && (
                    <td
                      rowSpan={maxRows}
                      style={{
                        position: 'sticky', left: 0, zIndex: 1,
                        background: isMe ? '#130d1f' : 'var(--surface)',
                        borderRight: '1px solid var(--border-subtle)',
                        padding: '6px 10px',
                        minWidth: 80,
                        verticalAlign: 'middle',
                        fontSize: 10, fontWeight: 600,
                        color: 'var(--text-h)',
                        borderBottom: '1px solid var(--border)',
                      }}
                    >
                      {player.displayName}
                    </td>
                  )}

                  {/* Caption data columns */}
                  {captions.map(cap => {
                    const cb = player.captions[cap];
                    const pick = cb?.picks[rowIdx];
                    const avg = cb?.avg ?? 0;

                    return (
                      <React.Fragment key={cap}>
                        {/* Corps name */}
                        <td style={{
                          padding: '4px 8px',
                          color: 'var(--text)',
                          borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                        }}>
                          {pick?.corpsName ?? ''}
                        </td>
                        {/* Score */}
                        <td style={{
                          padding: '4px 8px',
                          color: 'var(--text-muted)',
                          borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                        }}>
                          {pick?.score != null ? pick.score.toFixed(3) : pick ? '—' : ''}
                        </td>
                        {/* Avg — only shown on the first row, spans visually */}
                        <td style={{
                          padding: '4px 8px',
                          textAlign: 'right',
                          fontWeight: 600,
                          color: isMe ? 'var(--accent)' : 'var(--text-muted)',
                          borderRight: '1px solid var(--border-subtle)',
                          borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                        }}>
                          {isFirstRow && avg > 0 ? avg.toFixed(3) : ''}
                        </td>
                      </React.Fragment>
                    );
                  })}

                  {/* Total — only on first row */}
                  <td style={{
                    padding: '4px 8px',
                    textAlign: 'right',
                    fontSize: 12, fontWeight: 900,
                    color: isMe ? 'var(--accent)' : 'var(--text-h)',
                    borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                  }}>
                    {isFirstRow && player.totalScore > 0 ? player.totalScore.toFixed(3) : ''}
                  </td>
                </tr>
              );
            });
          })}
        </tbody>
      </table>
    </div>
  );
}
```

Note: The `React.Fragment` usage requires `import React from 'react'` (or the JSX transform handles it). Add `import React from 'react';` at the top of the file.

Full file with correct imports:

```tsx
import React from 'react';
import type { PlayerScoreBreakdown } from '../types/api';

interface Props {
  breakdown: PlayerScoreBreakdown[];
  captions: string[];
  currentUserId?: string;
}

export function LeagueScoresTab({ breakdown, captions, currentUserId }: Props) {
  if (breakdown.length === 0) {
    return (
      <div style={{ padding: 24, color: 'var(--text-muted)', fontSize: 11 }}>
        No scores available yet.
      </div>
    );
  }

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: 10, whiteSpace: 'nowrap' }}>
        <thead>
          <tr>
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              minWidth: 80, background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '6px 10px',
            }} />
            {captions.map(cap => (
              <th
                key={cap}
                colSpan={3}
                style={{
                  fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
                  color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
                  padding: '6px 4px',
                  borderBottom: '1px solid var(--border)',
                  borderRight: '1px solid var(--border-subtle)',
                }}
              >
                {cap}
              </th>
            ))}
            <th style={{
              fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
              color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
              padding: '6px 8px',
              borderBottom: '1px solid var(--border)',
            }}>
              Total
            </th>
          </tr>
          <tr>
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '4px 10px',
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600, textAlign: 'left',
            }}>
              Player
            </th>
            {captions.flatMap(cap =>
              ['Corps', 'Score', 'Avg'].map(sub => (
                <th
                  key={`${cap}-${sub}`}
                  style={{
                    fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
                    padding: '4px 8px',
                    borderBottom: '1px solid var(--border)',
                    borderRight: sub === 'Avg' ? '1px solid var(--border-subtle)' : undefined,
                    textAlign: sub === 'Avg' ? 'right' : 'left',
                  }}
                >
                  {sub}
                </th>
              ))
            )}
            <th style={{
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
              padding: '4px 8px',
              borderBottom: '1px solid var(--border)',
              textAlign: 'right',
            }} />
          </tr>
        </thead>
        <tbody>
          {breakdown.map(player => {
            const isMe = player.userId === currentUserId;
            const maxRows = Math.max(1, ...captions.map(cap => player.captions[cap]?.picks.length ?? 0));

            return Array.from({ length: maxRows }, (_, rowIdx) => {
              const isFirstRow = rowIdx === 0;
              const isLastRow = rowIdx === maxRows - 1;

              return (
                <tr key={`${player.userId}-${rowIdx}`}>
                  {isFirstRow && (
                    <td
                      rowSpan={maxRows}
                      style={{
                        position: 'sticky', left: 0, zIndex: 1,
                        background: isMe ? '#130d1f' : 'var(--surface)',
                        borderRight: '1px solid var(--border-subtle)',
                        padding: '6px 10px',
                        minWidth: 80,
                        verticalAlign: 'middle',
                        fontSize: 10, fontWeight: 600,
                        color: 'var(--text-h)',
                        borderBottom: '1px solid var(--border)',
                      }}
                    >
                      {player.displayName}
                    </td>
                  )}
                  {captions.flatMap(cap => {
                    const cb = player.captions[cap];
                    const pick = cb?.picks[rowIdx];
                    const avg = cb?.avg ?? 0;

                    return [
                      <td key={`${cap}-corps`} style={{
                        padding: '4px 8px', color: 'var(--text)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick?.corpsName ?? ''}
                      </td>,
                      <td key={`${cap}-score`} style={{
                        padding: '4px 8px', color: 'var(--text-muted)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick?.score != null ? pick.score.toFixed(3) : pick ? '—' : ''}
                      </td>,
                      <td key={`${cap}-avg`} style={{
                        padding: '4px 8px', textAlign: 'right', fontWeight: 600,
                        color: isMe ? 'var(--accent)' : 'var(--text-muted)',
                        borderRight: '1px solid var(--border-subtle)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {isFirstRow && avg > 0 ? avg.toFixed(3) : ''}
                      </td>,
                    ];
                  })}
                  <td style={{
                    padding: '4px 8px', textAlign: 'right',
                    fontSize: 12, fontWeight: 900,
                    color: isMe ? 'var(--accent)' : 'var(--text-h)',
                    borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                  }}>
                    {isFirstRow && player.totalScore > 0 ? player.totalScore.toFixed(3) : ''}
                  </td>
                </tr>
              );
            });
          })}
        </tbody>
      </table>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/components/LeagueScoresTab.tsx
git commit -m "feat: add LeagueScoresTab component with spreadsheet layout"
```

---

## Task 7: League Detail Page

**Files:**
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`

Full redesign: header bar + 5 tabs (Home, Scores, Members, Picks, Info). The Picks tab reuses the same structure as DraftRoom's Picks tab but reads from `league.picks`.

- [ ] **Step 1: Replace `DCF.Web/src/pages/LeagueDetail.tsx` entirely**

```tsx
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { LeagueScoresTab } from '../components/LeagueScoresTab';
import { useMqtt } from '../mqtt/useMqtt';
import { useUser } from '../context/UserContext';
import type { DraftState, League, PlayerScoreBreakdown, Standing } from '../types/api';

type Tab = 'home' | 'scores' | 'members' | 'picks' | 'info';

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const { user } = useUser();
  const navigate = useNavigate();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  const [breakdown, setBreakdown] = useState<PlayerScoreBreakdown[]>([]);
  const [activeTab, setActiveTab] = useState<Tab>('home');
  const [activePicksPlayer, setActivePicksPlayer] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [joinCode, setJoinCode] = useState('');
  const [copying, setCopying] = useState(false);
  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) {
      api.getLeague(id).then(setLeague).catch(() => setError('Failed to load league.'));
    }
  }, [id]);

  useEffect(() => {
    if (id) {
      api.getStandings(id).then(setStandings).catch(() => {});
    }
  }, [id, scoresUpdated]);

  useEffect(() => {
    if (id && activeTab === 'scores') {
      api.getScoreBreakdown(id).then(setBreakdown).catch(() => {});
    }
  }, [id, activeTab]);

  if (error) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error}</div>;
  }

  if (!league) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>Loading…</div>;
  }

  const isCommissioner = user?.id !== undefined && user.id === league.commissionerUserId;
  const effectiveStatus = draftState?.status ?? league.draftStatus;
  const isDraftAccessible = effectiveStatus === 'Open' || effectiveStatus === 'InProgress' || effectiveStatus === 'Completed' || effectiveStatus === 'Scheduled';

  const joinLeague = async () => {
    const code = league.isPublic ? undefined : joinCode || (prompt('Enter invite code:') ?? undefined);
    await api.joinLeague(league.id, code);
    window.location.reload();
  };

  const openDraft = () => {
    if (id) api.openDraft(id).catch(() => {});
  };

  const copyInviteCode = () => {
    if (league.inviteCode) {
      navigator.clipboard.writeText(league.inviteCode);
      setCopying(true);
      setTimeout(() => setCopying(false), 1500);
    }
  };

  const statusBadge = () => {
    const s = effectiveStatus;

    if (s === 'InProgress') {
      return (
        <span style={{
          fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
          textTransform: 'uppercase', letterSpacing: '0.5px',
          background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
        }}>LIVE DRAFT</span>
      );
    }

    if (s === 'Open') {
      return (
        <span style={{
          fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
          textTransform: 'uppercase', letterSpacing: '0.5px',
          background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
        }}>LOBBY OPEN</span>
      );
    }

    const label = s === 'Scheduled' ? 'SCHEDULED' : s === 'Completed' ? 'COMPLETED' : 'NOT STARTED';

    return (
      <span style={{
        fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 600,
        textTransform: 'uppercase', letterSpacing: '0.5px',
        border: '1px solid var(--border)', color: 'var(--text-muted)',
      }}>{label}</span>
    );
  };

  const tabs: { key: Tab; label: string }[] = [
    { key: 'home', label: 'Home' },
    { key: 'scores', label: 'Scores' },
    { key: 'members', label: 'Members' },
    { key: 'picks', label: 'Picks' },
    { key: 'info', label: 'Info' },
  ];

  // ── Home tab ──────────────────────────────────────────────────────────────

  const renderHomeTab = () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {standings.length === 0 && (
        <div style={{ color: 'var(--text-muted)', fontSize: 11, padding: '20px 0' }}>No standings yet.</div>
      )}
      {standings.map((s, i) => {
        const isMe = s.userId === user?.id;
        const rank = i + 1;

        return (
          <div
            key={s.userId}
            style={{
              display: 'flex', alignItems: 'center', gap: 12,
              padding: '10px 14px',
              background: isMe ? 'var(--accent-bg)' : 'var(--surface)',
              border: `1px solid ${isMe ? 'var(--accent-border)' : 'var(--border)'}`,
              borderRadius: 5,
            }}
          >
            <span style={{
              fontSize: 12, fontWeight: 800, minWidth: 20,
              color: rank === 1 ? 'var(--accent)' : 'var(--text-muted)',
            }}>
              {rank}
            </span>
            <span style={{ flex: 1, fontSize: 11, fontWeight: 600, color: 'var(--text-h)' }}>{s.displayName}</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: isMe ? 'var(--accent)' : 'var(--text-h)' }}>
              {s.score.toFixed(3)}
            </span>
          </div>
        );
      })}
    </div>
  );

  // ── Members tab ───────────────────────────────────────────────────────────

  const renderMembersTab = () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {(league.members ?? []).map(m => (
        <div
          key={m.userId}
          style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            padding: '9px 14px',
            background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5,
          }}
        >
          <span style={{ fontSize: 11, color: 'var(--text-h)' }}>{m.displayName}</span>
          {m.userId === league.commissionerUserId && (
            <span style={{ fontSize: 8, color: 'var(--text-faint)', textTransform: 'uppercase', letterSpacing: '0.5px' }}>
              Commissioner
            </span>
          )}
        </div>
      ))}
    </div>
  );

  // ── Picks tab ─────────────────────────────────────────────────────────────

  const renderPicksTab = () => {
    const members = league.members ?? [];
    const effectivePlayer = activePicksPlayer ?? members[0]?.userId ?? null;
    const currentPlayer = members.find(m => m.userId === effectivePlayer) ?? members[0];
    if (!currentPlayer) return <div style={{ color: 'var(--text-muted)', fontSize: 11 }}>No members yet.</div>;

    const allPicks = league.picks ?? [];
    const playerPicks = allPicks.filter(p => p.userId === currentPlayer.userId);

    return (
      <div>
        <div style={{ display: 'flex', gap: 4, marginBottom: 16, flexWrap: 'wrap' }}>
          {members.map(m => (
            <button
              key={m.userId}
              onClick={() => setActivePicksPlayer(m.userId)}
              style={{
                padding: '4px 12px', borderRadius: 12, fontSize: 10, fontWeight: 600,
                cursor: 'pointer', border: 'none',
                background: effectivePlayer === m.userId ? 'var(--accent)' : 'var(--surface)',
                color: effectivePlayer === m.userId ? '#0d0f14' : 'var(--text-muted)',
              }}
            >
              {m.displayName.split(' ')[0]}
            </button>
          ))}
        </div>
        {league.draftableCaptions.map(cap => {
          const capPicks = playerPicks.filter(p => p.caption === cap);
          const filled = capPicks.length;
          const total = league.corpsPerCaption;

          return (
            <div key={cap} style={{ marginBottom: 12 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
                <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>{cap}</div>
                <div style={{
                  fontSize: 8, padding: '1px 6px', borderRadius: 8, fontWeight: 700,
                  background: filled > 0 ? 'var(--accent-bg)' : 'var(--surface)',
                  color: filled > 0 ? 'var(--accent)' : 'var(--text-faint)',
                  border: `1px solid ${filled > 0 ? 'var(--accent-border)' : 'var(--border)'}`,
                }}>
                  {filled} / {total}
                </div>
              </div>
              {Array.from({ length: total }).map((_, i) => {
                const pick = capPicks[i];

                if (pick) {
                  return (
                    <div key={i} style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '6px 10px', background: 'var(--surface)',
                      border: '1px solid var(--border)', borderRadius: 5, marginBottom: 4,
                    }}>
                      <div style={{
                        width: 20, height: 20, borderRadius: '50%',
                        background: 'var(--accent-bg)', border: '1px solid var(--accent-border)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 8, color: 'var(--accent)', flexShrink: 0,
                      }}>
                        #{pick.pickNumber + 1}
                      </div>
                      <div>
                        <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-h)' }}>{pick.corpsName}</div>
                        <div style={{ fontSize: 8, color: 'var(--text-muted)' }}>Pick #{pick.pickNumber + 1} overall</div>
                      </div>
                    </div>
                  );
                }

                return (
                  <div key={i} style={{ padding: '6px 10px', border: '1px dashed var(--border)', borderRadius: 5, marginBottom: 4 }}>
                    <span style={{ fontSize: 10, fontStyle: 'italic', color: 'var(--text-faint)' }}>Empty</span>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    );
  };

  // ── Info tab ──────────────────────────────────────────────────────────────

  const renderInfoTab = () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      {league.inviteCode && (
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>Invite Code</div>
          <div style={{
            display: 'flex', alignItems: 'center', gap: 10,
            padding: '10px 14px', background: 'var(--surface-2)',
            border: '1px solid var(--border)', borderRadius: 5,
          }}>
            <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--accent)', flex: 1, letterSpacing: '0.5px' }}>
              {league.inviteCode}
            </span>
            <button
              onClick={copyInviteCode}
              style={{
                fontSize: 10, fontWeight: 600, padding: '4px 10px', borderRadius: 4,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-muted)',
              }}
            >
              {copying ? 'Copied!' : 'Copy'}
            </button>
          </div>
        </div>
      )}
      <div>
        <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>Draft Settings</div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {[
            { label: 'Captions', value: league.draftableCaptions.join(', ') },
            { label: 'Corps per Caption', value: String(league.corpsPerCaption) },
            { label: 'Draft Start', value: league.draftStartTime ? new Date(league.draftStartTime).toLocaleString() : 'Not scheduled' },
          ].map(item => (
            <div key={item.label} style={{
              display: 'flex', justifyContent: 'space-between', alignItems: 'center',
              padding: '8px 14px', background: 'var(--surface)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{item.label}</span>
              <span style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-h)' }}>{item.value}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );

  // ── Layout ────────────────────────────────────────────────────────────────

  return (
    <div>
      {/* Header bar */}
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
        <div>
          <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)', marginBottom: 4 }}>{league.name}</h2>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            {league.seasonYear} · {league.memberCount ?? league.members?.length ?? 0} members
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {statusBadge()}
          {isDraftAccessible && (
            <button
              onClick={() => navigate(`/leagues/${id}/draft`)}
              style={{
                fontSize: 11, fontWeight: 800, padding: '6px 14px', borderRadius: 5,
                background: 'var(--accent)', color: '#0d0f14', border: 'none',
                letterSpacing: '0.5px',
              }}
            >
              Draft Room →
            </button>
          )}
          {!league.isMember && (
            <button
              onClick={joinLeague}
              style={{
                fontSize: 11, fontWeight: 600, padding: '6px 14px', borderRadius: 5,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)',
              }}
            >
              Join League
            </button>
          )}
          {isCommissioner && league.draftStatus === 'NotStarted' && !draftState && (
            <button
              onClick={openDraft}
              style={{
                fontSize: 11, fontWeight: 600, padding: '6px 14px', borderRadius: 5,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-muted)',
              }}
            >
              Open Draft
            </button>
          )}
        </div>
      </div>

      {/* Tab bar */}
      <div style={{
        display: 'flex',
        background: 'var(--surface)',
        borderLeft: '1px solid var(--border)',
        borderRight: '1px solid var(--border)',
        borderBottom: '1px solid var(--border)',
        borderBottomLeftRadius: 0,
        borderBottomRightRadius: 0,
      }}>
        {tabs.map(t => (
          <button
            key={t.key}
            onClick={() => setActiveTab(t.key)}
            style={{
              flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600,
              cursor: 'pointer', background: 'transparent', border: 'none',
              color: activeTab === t.key ? 'var(--accent)' : 'var(--text-muted)',
              borderBottom: activeTab === t.key ? '2px solid var(--accent)' : '2px solid transparent',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div style={{
        background: 'var(--surface-2)',
        border: '1px solid var(--border)',
        borderTop: 'none',
        borderBottomLeftRadius: 6,
        borderBottomRightRadius: 6,
        padding: 20,
        minHeight: 200,
      }}>
        {activeTab === 'home' && renderHomeTab()}
        {activeTab === 'scores' && <LeagueScoresTab breakdown={breakdown} captions={league.draftableCaptions} currentUserId={user?.id} />}
        {activeTab === 'members' && renderMembersTab()}
        {activeTab === 'picks' && renderPicksTab()}
        {activeTab === 'info' && renderInfoTab()}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No TypeScript errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/LeagueDetail.tsx
git commit -m "feat: league detail page with header, 5 tabs, and scores breakdown"
```

---

## Task 8: Create League Redesign

**Files:**
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

Replace the radio-preset caption groups with individual toggle chips for each caption. Group chips visually by GE / Visual / Music, but each chip independently toggles that caption. The existing `PresetGroup` component is removed.

- [ ] **Step 1: Replace `DCF.Web/src/pages/LeagueCreate.tsx` entirely**

```tsx
import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';

const ALL_CAPTIONS: { value: string; label: string; group: string }[] = [
  { value: 'GeneralEffect',        label: 'GE',            group: 'General Effect' },
  { value: 'GeneralEffectMusic',   label: 'GE1 Music',     group: 'General Effect' },
  { value: 'GeneralEffectVisual',  label: 'GE2 Visual',    group: 'General Effect' },
  { value: 'Visual',               label: 'Visual',        group: 'Visual' },
  { value: 'VisualPerformance',    label: 'Vis Perf',      group: 'Visual' },
  { value: 'VisualAnalysis',       label: 'Vis Analysis',  group: 'Visual' },
  { value: 'VisualProficiency',    label: 'Vis Prof',      group: 'Visual' },
  { value: 'ColorGuard',           label: 'Color Guard',   group: 'Visual' },
  { value: 'Music',                label: 'Music',         group: 'Music' },
  { value: 'Brass',                label: 'Brass',         group: 'Music' },
  { value: 'MusicAnalysis',        label: 'Mus Analysis',  group: 'Music' },
  { value: 'Percussion',           label: 'Percussion',    group: 'Music' },
];

const CAPTION_GROUPS = ['General Effect', 'Visual', 'Music'];

function Chip({ label, selected, onClick }: { label: string; selected: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '5px 12px', borderRadius: 5, fontSize: 10, fontWeight: selected ? 600 : 500,
        cursor: 'pointer', border: `1px solid ${selected ? 'var(--accent)' : 'var(--border)'}`,
        background: selected ? 'var(--accent-bg)' : 'var(--surface)',
        color: selected ? 'var(--text-h)' : 'var(--text-muted)',
      }}
    >
      {label}
    </button>
  );
}

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [selectedCaptions, setSelectedCaptions] = useState<Set<string>>(new Set(['Brass', 'Percussion', 'ColorGuard', 'GeneralEffectMusic', 'GeneralEffectVisual', 'VisualAnalysis', 'VisualProficiency']));
  const [draftStartTime, setDraftStartTime] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleCaption = (value: string) => {
    setSelectedCaptions(prev => {
      const next = new Set(prev);
      if (next.has(value)) {
        next.delete(value);
      } else {
        next.add(value);
      }
      return next;
    });
  };

  const totalPicks = corpsPerCaption * selectedCaptions.size;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedCaptions.size === 0) {
      setError('Select at least one caption.');
      return;
    }
    setSubmitting(true);
    setError(null);

    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        draftableCaptions: Array.from(selectedCaptions),
        draftStartTime: draftStartTime || null,
      });

      navigate(`/leagues/${league.id}`);
    } catch {
      setError('Failed to create league. Please try again.');
      setSubmitting(false);
    }
  };

  const inputStyle: React.CSSProperties = {
    width: '100%', padding: '8px 10px', borderRadius: 5,
    background: 'var(--bg)', border: '1px solid #3d3f4e',
    color: 'var(--text-h)', fontSize: 11, outline: 'none',
  };

  return (
    <div style={{ maxWidth: 480 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 24 }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)' }}>Create League</h2>
        <Link to="/leagues" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none' }}>← Back to Leagues</Link>
      </div>

      <form onSubmit={submit} style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        {/* League Name */}
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>League Name</div>
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            required
            placeholder="My Fantasy League"
            style={inputStyle}
          />
        </div>

        {/* Visibility toggle */}
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Visibility</div>
          <div style={{ display: 'flex', gap: 4 }}>
            {[true, false].map(pub => (
              <button
                key={String(pub)}
                type="button"
                onClick={() => setIsPublic(pub)}
                style={{
                  flex: 1, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: isPublic === pub ? 700 : 500,
                  cursor: 'pointer',
                  border: `1px solid ${isPublic === pub ? 'var(--accent)' : 'var(--border)'}`,
                  background: isPublic === pub ? 'var(--accent-bg)' : 'var(--surface)',
                  color: isPublic === pub ? 'var(--text-h)' : 'var(--text-muted)',
                }}
              >
                {pub ? 'Public' : 'Private'}
              </button>
            ))}
          </div>
        </div>

        {/* Captions chip grid */}
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 10 }}>Captions</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {CAPTION_GROUPS.map(group => (
              <div key={group}>
                <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6, letterSpacing: '0.3px' }}>{group}</div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {ALL_CAPTIONS.filter(c => c.group === group).map(c => (
                    <Chip
                      key={c.value}
                      label={c.label}
                      selected={selectedCaptions.has(c.value)}
                      onClick={() => toggleCaption(c.value)}
                    />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Corps per Caption stepper */}
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Corps per Caption</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <button
              type="button"
              onClick={() => setCorpsPerCaption(Math.max(1, corpsPerCaption - 1))}
              style={{
                width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-h)', cursor: 'pointer',
              }}
            >−</button>
            <span style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)', minWidth: 20, textAlign: 'center' }}>{corpsPerCaption}</span>
            <button
              type="button"
              onClick={() => setCorpsPerCaption(Math.min(10, corpsPerCaption + 1))}
              style={{
                width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-h)', cursor: 'pointer',
              }}
            >+</button>
            <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>= {totalPicks} total picks</span>
          </div>
        </div>

        {/* Draft Start */}
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>
            Draft Start <span style={{ color: 'var(--text-faint)', fontWeight: 400, textTransform: 'none' }}>(optional)</span>
          </div>
          <input
            type="datetime-local"
            value={draftStartTime}
            onChange={e => setDraftStartTime(e.target.value)}
            placeholder="Pick a date and time…"
            style={inputStyle}
          />
        </div>

        {error && <div style={{ fontSize: 10, color: '#f87171' }}>{error}</div>}

        <button
          type="submit"
          disabled={submitting}
          style={{
            width: '100%', padding: '10px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
            letterSpacing: '0.5px', textTransform: 'uppercase',
            background: submitting ? 'var(--border)' : 'var(--accent)',
            color: submitting ? 'var(--text-faint)' : '#0d0f14',
            border: 'none', cursor: submitting ? 'not-allowed' : 'pointer',
          }}
        >
          {submitting ? 'Creating…' : 'Create League'}
        </button>
      </form>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/LeagueCreate.tsx
git commit -m "feat: create league page with caption chip grid"
```

---

## Task 9: Admin Page Redesign

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`

- [ ] **Step 1: Replace `DCF.Web/src/pages/Admin.tsx` entirely**

```tsx
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, Season } from '../types/api';

type Tab = 'seasons' | 'corps';

const inputStyle: React.CSSProperties = {
  padding: '7px 10px', borderRadius: 5,
  background: 'var(--bg)', border: '1px solid #3d3f4e',
  color: 'var(--text-h)', fontSize: 11, outline: 'none',
};

const primaryBtn: React.CSSProperties = {
  padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
  background: 'var(--accent)', color: '#0d0f14', border: 'none', cursor: 'pointer',
  letterSpacing: '0.5px',
};

const disabledBtn: React.CSSProperties = {
  ...primaryBtn,
  background: 'var(--border)', color: 'var(--text-faint)', cursor: 'not-allowed',
};

function SeasonBadge({ season }: { season: Season }) {
  if (season.isPublished) {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>PUBLISHED</span>;
  }

  if (season.status === 'Active') {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>ACTIVE</span>;
  }

  if (season.status === 'Completed') {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 600, background: 'var(--surface)', color: 'var(--text-faint)', border: '1px solid var(--border)' }}>COMPLETED</span>;
  }

  return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 600, border: '1px solid var(--border)', color: 'var(--text-muted)' }}>UPCOMING</span>;
}

export function Admin() {
  const [tab, setTab] = useState<Tab>('seasons');
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [newYear, setNewYear] = useState('');
  const [newStartDate, setNewStartDate] = useState('');
  const [newEndDate, setNewEndDate] = useState('');
  const [addingSeason, setAddingSeason] = useState(false);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [addingCorps, setAddingCorps] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setError(null);
    if (tab === 'seasons') {
      api.adminGetSeasons().then(setSeasons).catch(() => setError('Failed to load seasons.'));
    } else {
      api.adminGetCorps().then(setCorps).catch(() => setError('Failed to load corps.'));
    }
  }, [tab]);

  const addSeason = async (e: React.FormEvent) => {
    e.preventDefault();
    if (addingSeason) return;
    setAddingSeason(true);
    setError(null);

    try {
      await api.adminCreateSeason(Number(newYear), newStartDate, newEndDate);
      const updated = await api.adminGetSeasons();
      setSeasons(updated);
      setNewYear('');
      setNewStartDate('');
      setNewEndDate('');
    } catch {
      setError('Failed to add season.');
    } finally {
      setAddingSeason(false);
    }
  };

  const addCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    if (addingCorps) return;
    setAddingCorps(true);
    setError(null);

    try {
      await api.adminCreateCorps(newCorpsName);
      const updated = await api.adminGetCorps();
      setCorps(updated);
      setNewCorpsName('');
    } catch {
      setError('Failed to add corps.');
    } finally {
      setAddingCorps(false);
    }
  };

  const tabs: { key: Tab; label: string }[] = [
    { key: 'seasons', label: 'Seasons' },
    { key: 'corps', label: 'Corps' },
  ];

  return (
    <div>
      <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)', marginBottom: 20 }}>Admin</h2>

      {/* Tab bar */}
      <div style={{
        display: 'flex',
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: '6px 6px 0 0',
      }}>
        {tabs.map(t => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            style={{
              flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600,
              cursor: 'pointer', background: 'transparent', border: 'none',
              color: tab === t.key ? 'var(--accent)' : 'var(--text-muted)',
              borderBottom: tab === t.key ? '2px solid var(--accent)' : '2px solid transparent',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div style={{
        background: 'var(--surface-2)',
        border: '1px solid var(--border)',
        borderTop: 'none',
        borderBottomLeftRadius: 6,
        borderBottomRightRadius: 6,
        padding: 20,
      }}>
        {error && <div style={{ fontSize: 10, color: '#f87171', marginBottom: 12 }}>{error}</div>}

        {tab === 'seasons' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            {/* Seasons table */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {seasons.length === 0 && (
                <div style={{ fontSize: 11, color: 'var(--text-muted)', padding: '12px 0' }}>No seasons yet.</div>
              )}
              {seasons.map(s => (
                <div key={s.id} style={{
                  display: 'flex', alignItems: 'center', gap: 12,
                  padding: '10px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                }}>
                  <span style={{ fontSize: 13, fontWeight: 800, color: 'var(--text-h)', minWidth: 40 }}>{s.year}</span>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)', flex: 1 }}>{s.startDate} – {s.endDate}</span>
                  <SeasonBadge season={s} />
                  <Link to={`/admin/seasons/${s.id}`} style={{ fontSize: 10, color: 'var(--accent)', textDecoration: 'none', fontWeight: 600 }}>
                    Manage →
                  </Link>
                </div>
              ))}
            </div>

            {/* Add Season form */}
            <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
              <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Season</div>
              <form onSubmit={addSeason} style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                <input type="number" value={newYear} onChange={e => setNewYear(e.target.value)} placeholder="Year" required style={{ ...inputStyle, width: 80 }} />
                <input type="date" value={newStartDate} onChange={e => setNewStartDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
                <input type="date" value={newEndDate} onChange={e => setNewEndDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
                <button type="submit" disabled={addingSeason} style={addingSeason ? disabledBtn : primaryBtn}>
                  Add Season
                </button>
              </form>
            </div>
          </div>
        )}

        {tab === 'corps' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            {/* Corps list */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {corps.length === 0 && (
                <div style={{ fontSize: 11, color: 'var(--text-muted)', padding: '12px 0' }}>No corps yet.</div>
              )}
              {corps.map(c => (
                <div key={c.id} style={{
                  padding: '9px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                  fontSize: 11, color: 'var(--text-h)',
                }}>
                  {c.name}
                </div>
              ))}
            </div>

            {/* Add Corps form */}
            <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
              <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Corps</div>
              <form onSubmit={addCorps} style={{ display: 'flex', gap: 8, alignItems: 'flex-end' }}>
                <input value={newCorpsName} onChange={e => setNewCorpsName(e.target.value)} placeholder="Corps name" required style={{ ...inputStyle, flex: 1 }} />
                <button type="submit" disabled={addingCorps} style={addingCorps ? disabledBtn : primaryBtn}>
                  Add Corps
                </button>
              </form>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/Admin.tsx
git commit -m "feat: styled admin page with tabbed panel"
```

---

## Task 10: Season Detail Redesign

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

- [ ] **Step 1: Replace `DCF.Web/src/pages/SeasonDetail.tsx` entirely**

```tsx
import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, SeasonDetail as SeasonDetailType, Show } from '../types/api';

const inputStyle: React.CSSProperties = {
  width: '100%', padding: '7px 10px', borderRadius: 5,
  background: 'var(--bg)', border: '1px solid #3d3f4e',
  color: 'var(--text-h)', fontSize: 11, outline: 'none',
};

function Chip({ label, selected, onClick, disabled }: { label: string; selected: boolean; onClick: () => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      style={{
        padding: '5px 12px', borderRadius: 5, fontSize: 10, fontWeight: selected ? 600 : 500,
        cursor: disabled ? 'not-allowed' : 'pointer',
        border: `1px solid ${selected ? 'var(--green-border)' : 'var(--border)'}`,
        background: selected ? 'var(--green-bg)' : 'var(--surface)',
        color: selected ? 'var(--green)' : 'var(--text-muted)',
        opacity: disabled ? 0.55 : 1,
      }}
    >
      {label}
    </button>
  );
}

export function SeasonDetail() {
  const { id } = useParams<{ id: string }>();
  const [season, setSeason] = useState<SeasonDetailType | null>(null);
  const [allCorps, setAllCorps] = useState<Corps[]>([]);
  const [shows, setShows] = useState<Show[]>([]);
  const [selectedCorpsIds, setSelectedCorpsIds] = useState<Set<string>>(new Set());
  const [savingCorps, setSavingCorps] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const [showName, setShowName] = useState('');
  const [showUrl, setShowUrl] = useState('');
  const [showDate, setShowDate] = useState('');
  const [showScoresTime, setShowScoresTime] = useState('');
  const [showCorpsIds, setShowCorpsIds] = useState<Set<string>>(new Set());
  const [addingShow, setAddingShow] = useState(false);

  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    Promise.all([
      api.adminGetSeason(id),
      api.adminGetCorps(),
      api.adminGetShows(id),
    ]).then(([s, c, sh]) => {
      setSeason(s);
      setAllCorps(c);
      setShows(sh);
      setSelectedCorpsIds(new Set(s.corpsIds));
    }).catch(() => setError('Failed to load season.'));
  }, [id]);

  const toggleCorps = (corpsId: string) => {
    setSelectedCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) next.delete(corpsId); else next.add(corpsId);
      return next;
    });
  };

  const saveCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!id || savingCorps) return;
    setSavingCorps(true);
    setError(null);

    try {
      await api.adminSetSeasonCorps(id, Array.from(selectedCorpsIds));
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
      setSelectedCorpsIds(new Set(updated.corpsIds));
    } catch {
      setError('Failed to save corps.');
    } finally {
      setSavingCorps(false);
    }
  };

  const publish = async () => {
    if (!id || publishing) return;
    setPublishing(true);
    setError(null);

    try {
      await api.adminPublishSeason(id);
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
    } catch {
      setError('Failed to publish season.');
    } finally {
      setPublishing(false);
    }
  };

  const toggleShowCorps = (corpsId: string) => {
    setShowCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) next.delete(corpsId); else next.add(corpsId);
      return next;
    });
  };

  const addShow = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!id || addingShow) return;
    setAddingShow(true);
    setError(null);

    try {
      await api.adminCreateShow(id, showName, showUrl, showDate, new Date(showScoresTime).toISOString(), Array.from(showCorpsIds));
      const updated = await api.adminGetShows(id);
      setShows(updated);
      setShowName('');
      setShowUrl('');
      setShowDate('');
      setShowScoresTime('');
      setShowCorpsIds(new Set());
    } catch {
      setError('Failed to add show.');
    } finally {
      setAddingShow(false);
    }
  };

  if (!season) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error ?? 'Loading…'}</div>;
  }

  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  return (
    <div>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
            <Link to="/admin" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none' }}>← Admin</Link>
          </div>
          <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)', marginBottom: 4 }}>Season {season.year}</h2>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{season.startDate} – {season.endDate} · {season.status}</div>
        </div>
        {!season.isPublished && season.corpsIds.length > 0 && (
          <button
            onClick={publish}
            disabled={publishing}
            style={{
              padding: '7px 16px', borderRadius: 5, fontSize: 11, fontWeight: 800,
              background: publishing ? 'var(--border)' : 'var(--accent)',
              color: publishing ? 'var(--text-faint)' : '#0d0f14',
              border: 'none', cursor: publishing ? 'not-allowed' : 'pointer',
            }}
          >
            {publishing ? 'Publishing…' : 'Publish'}
          </button>
        )}
        {season.isPublished && (
          <span style={{ fontSize: 8, padding: '4px 10px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>PUBLISHED</span>
        )}
      </div>

      {error && <div style={{ fontSize: 10, color: '#f87171', marginBottom: 16 }}>{error}</div>}

      {/* Two-panel body */}
      <div style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
        {/* Left — Corps checklist */}
        <div style={{ flex: '0 0 280px', display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Corps this season</div>
          <form onSubmit={saveCorps}>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 12 }}>
              {allCorps.map(c => (
                <Chip
                  key={c.id}
                  label={c.name}
                  selected={selectedCorpsIds.has(c.id)}
                  onClick={() => toggleCorps(c.id)}
                  disabled={season.isPublished}
                />
              ))}
            </div>
            <button
              type="submit"
              disabled={savingCorps || season.isPublished}
              style={{
                padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
                background: savingCorps || season.isPublished ? 'var(--border)' : 'var(--accent)',
                color: savingCorps || season.isPublished ? 'var(--text-faint)' : '#0d0f14',
                border: 'none', cursor: savingCorps || season.isPublished ? 'not-allowed' : 'pointer',
              }}
            >
              {season.isPublished ? 'Locked (published)' : savingCorps ? 'Saving…' : 'Save Corps'}
            </button>
          </form>
        </div>

        {/* Right — Shows */}
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Shows</div>

          {shows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows yet.</div>
          )}

          {shows.map(s => (
            <div key={s.id} style={{
              padding: '12px 14px', background: 'var(--surface)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-h)', marginBottom: 3 }}>{s.name}</div>
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{s.date}</div>
              <div style={{ fontSize: 9, color: 'var(--text-faint)', marginTop: 2 }}>
                Scores at {new Date(s.scoresAnnouncedTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
              </div>
            </div>
          ))}

          {/* Add Show form */}
          <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Show</div>
            <form onSubmit={addShow} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <input value={showName} onChange={e => setShowName(e.target.value)} placeholder="Show name" required style={inputStyle} />
              <input value={showUrl} onChange={e => setShowUrl(e.target.value)} placeholder="DCI recap URL" required style={inputStyle} />
              <input type="date" value={showDate} onChange={e => setShowDate(e.target.value)} required style={inputStyle} />
              <input type="datetime-local" value={showScoresTime} onChange={e => setShowScoresTime(e.target.value)} required style={inputStyle} />

              <div>
                <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {seasonCorps.map(c => (
                    <Chip
                      key={c.id}
                      label={c.name}
                      selected={showCorpsIds.has(c.id)}
                      onClick={() => toggleShowCorps(c.id)}
                    />
                  ))}
                </div>
              </div>

              <button
                type="submit"
                disabled={addingShow}
                style={{
                  padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                  background: addingShow ? 'var(--border)' : 'var(--accent)',
                  color: addingShow ? 'var(--text-faint)' : '#0d0f14',
                  border: 'none', cursor: addingShow ? 'not-allowed' : 'pointer',
                }}
              >
                {addingShow ? 'Adding…' : 'Add Show'}
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: styled season detail with two-panel layout"
```

---

## Task 11: DraftRoom — Fix Scheduled Lobby State

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

The current redirect guard sends users back to the league page when `draftStatus === 'Scheduled'`. The spec calls for a lobby (green countdown, locked grid) for the `Scheduled` status — the same layout used for `Open`. Only `NotStarted` should redirect away.

- [ ] **Step 1: Fix the redirect guard in `DCF.Web/src/pages/DraftRoom.tsx`**

Find the `useEffect` containing the redirect guard (lines 36–41 in the current file):

```typescript
  // Redirect guard — only allow Open, InProgress, Completed
  useEffect(() => {
    if (!league) return;

    if (league.draftStatus === 'NotStarted' || league.draftStatus === 'Scheduled') {
      navigate(`/leagues/${id}`);
    }
  }, [league, id, navigate]);
```

Replace with:

```typescript
  // Redirect guard — only block NotStarted; Scheduled shows the lobby
  useEffect(() => {
    if (!league) return;

    if (league.draftStatus === 'NotStarted') {
      navigate(`/leagues/${id}`);
    }
  }, [league, id, navigate]);
```

- [ ] **Step 2: Update the countdown timer effect to run for both `Scheduled` and `Open`**

Find the countdown timer effect (lines 44–48):

```typescript
  // Countdown timer — only ticks during Open lobby
  useEffect(() => {
    if (draftState?.status !== 'Open') return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [draftState?.status]);
```

Replace with:

```typescript
  // Countdown timer — ticks during Scheduled and Open lobby
  useEffect(() => {
    if (draftState?.status !== 'Open' && league?.draftStatus !== 'Scheduled') return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [draftState?.status, league?.draftStatus]);
```

- [ ] **Step 3: Update `renderTopBar` to handle `Scheduled` status**

The `renderTopBar` function currently only renders the green countdown bar for `status === 'Open'`. The `draftState` MQTT value may be `null` or have a different status when the league is `Scheduled` but not yet opened. Add a guard that shows the green countdown when `league.draftStatus === 'Scheduled'` and no MQTT state has arrived yet.

Find the beginning of `renderTopBar` (around line 111):

```typescript
  const renderTopBar = () => {
    if (status === 'Open') {
```

Replace that first condition block so it matches both `Open` and the case where the status is still `Scheduled` (no MQTT broadcast yet):

```typescript
  const renderTopBar = () => {
    if (status === 'Open' || (status !== 'InProgress' && status !== 'Completed' && league.draftStatus === 'Scheduled')) {
```

- [ ] **Step 4: Verify build**

Run: `cd DCF.Web && npm run build`
Expected: No TypeScript errors.

- [ ] **Step 5: Lint check**

Run: `cd DCF.Web && npm run lint`
Expected: No lint errors.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: show lobby state for Scheduled draft status in DraftRoom"
```

---

## Self-Review

### Spec Coverage Check

| Spec Section | Covered By |
|---|---|
| Design system tokens | Task 1 |
| Navigation bar (logo, links, avatar, admin badge) | Task 2 |
| Landing split card + Auth0 Lock | Task 3 |
| Leagues featured card + list + empty state | Task 4 |
| League Detail header + 5 tabs | Task 7 |
| Scores tab (spreadsheet layout) | Tasks 5 + 6 |
| Create League caption chips | Task 8 |
| Admin tabbed panel (Seasons + Corps) | Task 9 |
| Season Detail two-panel | Task 10 |
| Draft Room Scheduled lobby | Task 11 |
| Draft Room In-progress / Completed states | Already implemented in existing `DraftRoom.tsx` |

**Out of scope (deferred per spec):** mobile-responsive layouts, profile page design, onboarding page design, animation/transition specs.

### Placeholder Scan

No TBD, TODO, or placeholder patterns found in the plan. Every step contains complete code.

### Type Consistency

- `PlayerScoreBreakdown` defined in Task 5 (types/api.ts), used in Task 6 (LeagueScoresTab) and Task 7 (LeagueDetail)
- `CaptionBreakdown`, `PickScore` defined in Task 5, used in Task 6
- `getScoreBreakdown` method added to `api` client in Task 5, called in Task 7
- `MemberScoreBreakdown` C# record defined in Task 5 (StandingsService), serialised by the controller — the JSON property names will be camelCase by default in ASP.NET Core, matching the TypeScript interface field names (`userId`, `displayName`, `totalScore`, `captions`)
