# User Registration Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register users in the database immediately after Auth0 sign-in, collecting a user-chosen display name for new users via a dedicated onboarding page, and expose the profile globally via React context.

**Architecture:** A new `GET /api/auth/me` endpoint checks whether the user exists; the frontend calls it on every sign-in and routes new users to `/onboarding` before calling `POST /api/auth/me` with their chosen display name. A `UserContext` at the root of the React tree holds the resolved `UserProfile` so any component can read it without re-fetching.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core InMemory (tests), xUnit, React 19, TypeScript, React Router v6, Auth0 React SDK.

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `DCF.Api/Services/IUserService.cs` | Modify | Add `GetAsync` signature; add `displayName?` to `UpsertAsync` |
| `DCF.Api/Services/UserService.cs` | Modify | Implement `GetAsync`; stop overwriting `DisplayName` for existing users |
| `DCF.Api/Controllers/AuthController.cs` | Modify | Add `GET /api/auth/me`; accept body `{ DisplayName }` on POST |
| `DCF.Tests/Services/UserServiceTests.cs` | Create | Tests for `GetAsync` and changed `UpsertAsync` behaviour |
| `DCF.Web/src/context/UserContext.tsx` | Create | React context holding `UserProfile \| null` + setter |
| `DCF.Web/src/main.tsx` | Modify | Add `UserProvider` + `BrowserRouter`; remove them from App |
| `DCF.Web/src/App.tsx` | Modify | Remove `BrowserRouter`; add auth sync effect + `/onboarding` route |
| `DCF.Web/src/api/client.ts` | Modify | Add `getUser`; update `upsertUser(displayName)` |
| `DCF.Web/src/pages/Onboarding.tsx` | Create | Display name form; calls upsertUser then navigates to `/` |
| `DCF.Web/src/pages/Profile.tsx` | Modify | Remove upsertUser call; read from `UserContext` |

---

## Task 1: Add `GetAsync` to `IUserService` and `UserService`

**Files:**
- Modify: `DCF.Api/Services/IUserService.cs`
- Modify: `DCF.Api/Services/UserService.cs`
- Create: `DCF.Tests/Services/UserServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/UserServiceTests.cs`:

```csharp
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class UserServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task GetAsync_ExistingUser_ReturnsProfile()
    {
        using var db = CreateDb("get_existing");
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = "auth0|123",
            Email = "test@example.com",
            DisplayName = "TestUser",
            IsAdmin = false
        });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.GetAsync("auth0|123");

        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.Email);
        Assert.Equal("TestUser", result.DisplayName);
        Assert.False(result.IsAdmin);
    }

    [Fact]
    public async Task GetAsync_NonExistentUser_ReturnsNull()
    {
        using var db = CreateDb("get_missing");

        var svc = new UserService(db);
        var result = await svc.GetAsync("auth0|does-not-exist");

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~UserServiceTests" -v n
```

Expected: compilation error — `UserService` has no `GetAsync` method.

- [ ] **Step 3: Add `GetAsync` to `IUserService`**

Replace the full contents of `DCF.Api/Services/IUserService.cs`:

```csharp
namespace DCF.Api.Services;

public interface IUserService
{
    Task<UserProfile?> GetAsync(string sub);
    Task<UserProfile> UpsertAsync(string sub, string email, string name);
}
```

- [ ] **Step 4: Implement `GetAsync` in `UserService`**

Add this method to `DCF.Api/Services/UserService.cs` before the closing brace of the class (after `UpsertAsync`):

```csharp
    public async Task<UserProfile?> GetAsync(string sub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            return null;
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin);
    }
```

- [ ] **Step 5: Run tests to confirm they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~UserServiceTests" -v n
```

Expected: both tests PASS.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/IUserService.cs DCF.Api/Services/UserService.cs DCF.Tests/Services/UserServiceTests.cs
git commit -m "feat: add GetAsync to UserService"
```

---

## Task 2: Modify `UpsertAsync` — accept `displayName`, stop overwriting for existing users

**Files:**
- Modify: `DCF.Api/Services/IUserService.cs`
- Modify: `DCF.Api/Services/UserService.cs`
- Modify: `DCF.Tests/Services/UserServiceTests.cs`

- [ ] **Step 1: Add failing tests**

Append these three test methods to the `UserServiceTests` class in `DCF.Tests/Services/UserServiceTests.cs`:

```csharp
    [Fact]
    public async Task UpsertAsync_NewUser_UsesProvidedDisplayName()
    {
        using var db = CreateDb("upsert_new_displayname");

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|new", "new@example.com", "Auth0 Name", "ChosenName");

        Assert.Equal("ChosenName", result.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_NewUser_FallsBackToJwtName_WhenDisplayNameNull()
    {
        using var db = CreateDb("upsert_new_fallback");

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|new2", "new2@example.com", "Auth0 Name", null);

        Assert.Equal("Auth0 Name", result.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_ExistingUser_DoesNotOverwriteDisplayName()
    {
        using var db = CreateDb("upsert_existing_no_overwrite");
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = "auth0|existing",
            Email = "old@example.com",
            DisplayName = "OriginalName"
        });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|existing", "updated@example.com", "New JWT Name", "AttemptedOverwrite");

        Assert.Equal("OriginalName", result.DisplayName);
        Assert.Equal("updated@example.com", result.Email);
    }
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~UserServiceTests" -v n
```

Expected: compilation errors — `UpsertAsync` doesn't accept a 4th argument yet.

- [ ] **Step 3: Update `IUserService` signature**

Replace the full contents of `DCF.Api/Services/IUserService.cs`:

```csharp
namespace DCF.Api.Services;

public interface IUserService
{
    Task<UserProfile?> GetAsync(string sub);
    Task<UserProfile> UpsertAsync(string sub, string email, string name, string? displayName = null);
}
```

- [ ] **Step 4: Update `UpsertAsync` in `UserService`**

Replace the entire `UpsertAsync` method body in `DCF.Api/Services/UserService.cs`. The full updated file should read:

```csharp
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record UserProfile(Guid Id, string Email, string DisplayName, bool IsAdmin);

public class UserService(DcfDbContext db) : IUserService
{
    public async Task<UserProfile> UpsertAsync(string sub, string email, string name, string? displayName = null)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Auth0Sub = sub,
                Email = email,
                DisplayName = displayName ?? name
            };
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (!await db.Users.AnyAsync(u => u.Auth0Sub == sub))
                {
                    throw;
                }

                db.ChangeTracker.Clear();

                user = await db.Users.FirstAsync(u => u.Auth0Sub == sub);
            }
        }
        else
        {
            user.Email = email;

            await db.SaveChangesAsync();
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin);
    }

    public async Task<UserProfile?> GetAsync(string sub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            return null;
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin);
    }
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~UserServiceTests" -v n
```

Expected: all 5 tests PASS.

- [ ] **Step 6: Run full test suite to check for regressions**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Services/IUserService.cs DCF.Api/Services/UserService.cs DCF.Tests/Services/UserServiceTests.cs
git commit -m "feat: modify UpsertAsync to accept displayName and preserve existing user's display name"
```

---

## Task 3: Update `AuthController` — add GET endpoint, accept body on POST

**Files:**
- Modify: `DCF.Api/Controllers/AuthController.cs`

- [ ] **Step 1: Replace the full contents of `AuthController.cs`**

```csharp
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(IUserService userService) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");

        var profile = await userService.GetAsync(sub);

        if (profile is null)
        {
            return NotFound();
        }

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin });
    }

    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser([FromBody] UpsertUserRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue("name") ?? email;

        var profile = await userService.UpsertAsync(sub, email, name, request.DisplayName);

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin });
    }
}

public record UpsertUserRequest(string? DisplayName);
```

- [ ] **Step 2: Build to confirm no compilation errors**

```
dotnet build DCF.slnx
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```
git add DCF.Api/Controllers/AuthController.cs
git commit -m "feat: add GET /api/auth/me and accept displayName body on POST"
```

---

## Task 4: Create `UserContext`

**Files:**
- Create: `DCF.Web/src/context/UserContext.tsx`

- [ ] **Step 1: Create the file**

Create `DCF.Web/src/context/UserContext.tsx`:

```tsx
import { createContext, useContext, useState } from 'react';
import type { UserProfile } from '../types/api';

interface UserContextValue {
  user: UserProfile | null;
  setUser: (user: UserProfile) => void;
}

const UserContext = createContext<UserContextValue | null>(null);

export function UserProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);

  return (
    <UserContext.Provider value={{ user, setUser }}>
      {children}
    </UserContext.Provider>
  );
}

export function useUser(): UserContextValue {
  const ctx = useContext(UserContext);
  if (!ctx) throw new Error('useUser must be used inside UserProvider');
  return ctx;
}
```

- [ ] **Step 2: Confirm it type-checks**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/context/UserContext.tsx
git commit -m "feat: add UserContext for global user profile state"
```

---

## Task 5: Move `BrowserRouter` to `main.tsx` and add `UserProvider`

**Files:**
- Modify: `DCF.Web/src/main.tsx`

- [ ] **Step 1: Replace the full contents of `main.tsx`**

```tsx
import { Auth0Provider } from '@auth0/auth0-react';
import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import { UserProvider } from './context/UserContext';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <UserProvider>
      <Auth0Provider
        domain={import.meta.env.VITE_AUTH0_DOMAIN}
        clientId={import.meta.env.VITE_AUTH0_CLIENT_ID}
        authorizationParams={{
          redirect_uri: window.location.origin,
          audience: import.meta.env.VITE_AUTH0_AUDIENCE,
        }}
      >
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </Auth0Provider>
    </UserProvider>
  </React.StrictMode>
);
```

- [ ] **Step 2: Confirm it type-checks**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: no errors (App.tsx still has BrowserRouter — that will cause a duplicate, but no type error yet; it gets removed in Task 6).

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/main.tsx
git commit -m "refactor: move BrowserRouter to main.tsx and add UserProvider"
```

---

## Task 6: Update `api/client.ts` — add `getUser`, update `upsertUser`

**Files:**
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Replace the full contents of `client.ts`**

```ts
import type { Corps, CreateLeagueRequest, League, Standing, UserProfile } from '../types/api';

const API_URL = import.meta.env.VITE_API_URL as string;

let getToken: (() => Promise<string>) | null = null;

export function setTokenGetter(fn: () => Promise<string>) {
  getToken = fn;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const token = getToken ? await getToken() : null;
  const res = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options?.headers,
    },
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json() as Promise<T>;
}

export const api = {
  getUser: async (): Promise<UserProfile | null> => {
    const token = getToken ? await getToken() : null;
    const res = await fetch(`${API_URL}/api/auth/me`, {
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(await res.text());
    return res.json() as UserProfile;
  },
  upsertUser: (displayName: string) =>
    request<UserProfile>('/api/auth/me', { method: 'POST', body: JSON.stringify({ displayName }) }),
  getLeagues: () => request<League[]>('/api/leagues'),
  getLeague: (id: string) => request<League>(`/api/leagues/${id}`),
  createLeague: (body: CreateLeagueRequest) => request<{ id: string; name: string; inviteCode: string }>('/api/leagues', { method: 'POST', body: JSON.stringify(body) }),
  joinLeague: (id: string, inviteCode?: string) =>
    request<void>(`/api/leagues/${id}/join`, { method: 'POST', body: JSON.stringify({ inviteCode }) }),
  getStandings: (id: string) => request<Standing[]>(`/api/leagues/${id}/standings`),
  startDraft: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/start`, { method: 'POST' }),
  submitPick: (leagueId: string, corpsId: string, caption: string) =>
    request<{ id: string; pickNumber: number }>(`/api/leagues/${leagueId}/draft/pick`, {
      method: 'POST', body: JSON.stringify({ corpsId, caption }),
    }),
  skipPick: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/skip`, { method: 'POST' }),
  adminGetCorps: () => request<Corps[]>('/api/admin/corps'),
  adminCreateCorps: (name: string) =>
    request<Corps>('/api/admin/corps', { method: 'POST', body: JSON.stringify({ name }) }),
  adminTriggerScrape: (showId: string) =>
    request<void>(`/api/admin/shows/${showId}/scrape`, { method: 'POST' }),
};
```

- [ ] **Step 2: Confirm it type-checks**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/api/client.ts
git commit -m "feat: add getUser and update upsertUser signature in api client"
```

---

## Task 7: Update `App.tsx` — remove `BrowserRouter`, add auth sync effect

**Files:**
- Modify: `DCF.Web/src/App.tsx`

- [ ] **Step 1: Replace the full contents of `App.tsx`**

```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useEffect } from 'react';
import { Route, Routes, useNavigate } from 'react-router-dom';
import { api, setTokenGetter } from './api/client';
import { AdminRoute } from './components/AdminRoute';
import { ProtectedRoute } from './components/ProtectedRoute';
import { useUser } from './context/UserContext';
import { Admin } from './pages/Admin';
import { DraftRoom } from './pages/DraftRoom';
import { Home } from './pages/Home';
import { LeagueCreate } from './pages/LeagueCreate';
import { LeagueDetail } from './pages/LeagueDetail';
import { Leagues } from './pages/Leagues';
import { Profile } from './pages/Profile';

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
    });
  }, [isAuthenticated, navigate, setUser]);

  return (
    <Routes>
      <Route path="/" element={<Home />} />
      <Route path="/leagues" element={<ProtectedRoute><Leagues /></ProtectedRoute>} />
      <Route path="/leagues/create" element={<ProtectedRoute><LeagueCreate /></ProtectedRoute>} />
      <Route path="/leagues/:id" element={<ProtectedRoute><LeagueDetail /></ProtectedRoute>} />
      <Route path="/leagues/:id/draft" element={<ProtectedRoute><DraftRoom /></ProtectedRoute>} />
      <Route path="/admin" element={<AdminRoute><Admin /></AdminRoute>} />
      <Route path="/profile" element={<ProtectedRoute><Profile /></ProtectedRoute>} />
    </Routes>
  );
}
```

- [ ] **Step 2: Confirm it type-checks**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: no errors. (`/onboarding` route is added in Task 8 once the page exists.)

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/App.tsx
git commit -m "refactor: remove BrowserRouter from App and add auth sync effect"
```

---

## Task 8: Create `Onboarding` page and wire up its route

**Files:**
- Create: `DCF.Web/src/pages/Onboarding.tsx`
- Modify: `DCF.Web/src/App.tsx`

- [ ] **Step 1: Create `DCF.Web/src/pages/Onboarding.tsx`**

```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useUser } from '../context/UserContext';

export function Onboarding() {
  const { user: auth0User } = useAuth0();
  const { user, setUser } = useUser();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState(auth0User?.name ?? '');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (user) {
    return <Navigate to="/" replace />;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const profile = await api.upsertUser(displayName);
      setUser(profile);
      navigate('/');
    } catch {
      setError('Failed to create profile. Please try again.');
      setSubmitting(false);
    }
  }

  return (
    <div>
      <h1>Welcome to DCF Fantasy!</h1>
      <form onSubmit={handleSubmit}>
        <label>
          Display name
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
            minLength={1}
          />
        </label>
        {error && <div>{error}</div>}
        <button type="submit" disabled={submitting}>
          {submitting ? 'Creating...' : 'Continue'}
        </button>
      </form>
    </div>
  );
}
```

- [ ] **Step 2: Add the `/onboarding` route to `App.tsx`**

Add the import after the existing page imports in `DCF.Web/src/App.tsx`:

```tsx
import { Onboarding } from './pages/Onboarding';
```

Add the route inside `<Routes>` as the second entry (after `<Route path="/" ...>`):

```tsx
<Route path="/onboarding" element={<ProtectedRoute><Onboarding /></ProtectedRoute>} />
```

- [ ] **Step 3: Confirm it type-checks**

```
cd DCF.Web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/Onboarding.tsx DCF.Web/src/App.tsx
git commit -m "feat: add Onboarding page and /onboarding route"
```

---

## Task 9: Update `Profile.tsx` — read from `UserContext`

**Files:**
- Modify: `DCF.Web/src/pages/Profile.tsx`

- [ ] **Step 1: Replace the full contents of `Profile.tsx`**

```tsx
import { useAuth0 } from '@auth0/auth0-react';
import { useUser } from '../context/UserContext';

export function Profile() {
  const { logout } = useAuth0();
  const { user } = useUser();

  if (!user) return <div>Loading...</div>;

  return (
    <div>
      <h2>Profile</h2>
      <p>Display name: {user.displayName}</p>
      <p>Email: {user.email}</p>
      {user.isAdmin && <p>✓ Admin</p>}
      <button onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}>
        Sign Out
      </button>
    </div>
  );
}
```

- [ ] **Step 2: Run full lint and type-check**

```
cd DCF.Web && npx tsc --noEmit && npm run lint
```

Expected: no errors or warnings.

- [ ] **Step 3: Run full backend test suite one final time**

```
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests PASS.

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/pages/Profile.tsx
git commit -m "refactor: Profile reads user from UserContext instead of calling upsertUser"
```
