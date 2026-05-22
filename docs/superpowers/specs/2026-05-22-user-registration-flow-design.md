# User Registration Flow

**Date:** 2026-05-22
**Status:** Approved

## Problem

Users are not registered in the database until they navigate to the Profile page, because `POST /api/auth/me` is only called there. Any feature that requires a user record (leagues, drafts) can fail silently for a brand-new user who hasn't visited Profile. There is also no opportunity to collect a user-chosen display name before the record is created.

## Goals

- Register a user in the database as early as possible after Auth0 sign-in.
- Let new users choose a display name before their record is created.
- Make the authenticated user's profile available globally so any component can read it.
- Keep the design extensible: future onboarding steps (avatar, timezone, etc.) slot in at the same place without structural changes.

## Backend

### New: `GET /api/auth/me`

Returns the current user's `UserProfile` if they exist in the database, or `404 Not Found` if they don't. Used by the frontend to determine whether to show the onboarding flow.

```
GET /api/auth/me  →  200 { id, email, displayName, isAdmin }
                  →  404 (user not yet registered)
```

**Implementation:** Add `GetAsync(string sub)` to `IUserService` and `UserService`. The controller reads the `sub` claim and calls `GetAsync`; returns `NotFound()` if the result is null.

### Modified: `POST /api/auth/me`

Accepts an optional request body `{ displayName: string }`. The `displayName` field is used only when creating a new user record; it is ignored for existing users.

```
POST /api/auth/me  { displayName: "NeilN" }  →  200 { id, email, displayName, isAdmin }
```

**`UpsertAsync` changes:**
- Accepts a `string? displayName` parameter.
- **New user:** uses `displayName` if provided, otherwise falls back to the JWT `name` claim. Email always comes from the JWT `email` claim.
- **Existing user:** updates `Email` from the JWT claim only. `DisplayName` is no longer overwritten — it stays as the user previously set it.

This is a breaking change to the upsert behaviour: existing users calling the old endpoint will no longer have their display name reset to their Auth0 name on each login.

## Frontend

### `UserContext`

A React context that holds `UserProfile | null` and a `setUser` setter. Provided at the root so every component can read the current user without prop-drilling.

```ts
const { user, setUser } = useUser();
```

`UserProvider` wraps the entire app in `main.tsx` (outside `App`).

### Router restructure

`BrowserRouter` moves from `App.tsx` to `main.tsx` so that `App.tsx` sits inside the router and can use `useNavigate`. `UserProvider` also lives in `main.tsx`, wrapping `BrowserRouter`.

```
main.tsx
  └─ UserProvider
       └─ Auth0Provider
            └─ BrowserRouter
                 └─ App (sets token getter, auth sync effect, routes)
```

### Auth sync effect (`App.tsx`)

When `isAuthenticated` transitions to `true`, the effect:
1. Calls `api.getUser()` (new `GET /api/auth/me` call).
2. **200:** stores the profile in context. User proceeds normally.
3. **404:** redirects to `/onboarding` via `useNavigate`.

The existing `setTokenGetter` effect remains and runs first (effects fire in declaration order), so the token is available before the sync effect fires.

**Note on email sync for existing users:** In the new flow, `POST /api/auth/me` is never called for existing users — only `GET` is used. This means a user who changes their email in Auth0 will not have their `Email` field updated in the database automatically. This is an accepted trade-off for now; a dedicated update-email mechanism can be added later if needed.

### `/onboarding` page

Shown only to new users (those who got a 404 from `GET /api/auth/me`).

- Renders a display name input pre-filled with the Auth0 `name` claim (accessible via `useAuth0().user?.name`).
- On submit: calls `api.upsertUser(displayName)` (`POST /api/auth/me` with body).
- On success: stores the returned profile in `UserContext`, redirects to `/`.
- The route is accessible without being registered but still requires `isAuthenticated`. An already-registered user navigating to `/onboarding` directly is redirected to `/` (guarded by checking `user` in context).

### `api` client changes

- Add `getUser(): Promise<UserProfile | null>` — calls `GET /api/auth/me`, returns `null` on 404.
- Update `upsertUser(displayName: string): Promise<UserProfile>` — passes `{ displayName }` as the JSON body.

### Profile page

Remove the `upsertUser` call from `Profile.tsx`. It reads the user from `UserContext` instead — the profile is already populated by the time any authenticated page renders.

## Data flow summary

```
Auth0 sign-in complete
        ↓
App.tsx effect: isAuthenticated = true
        ↓
GET /api/auth/me
   ├─ 200 → store in UserContext → normal app
   └─ 404 → navigate to /onboarding
                ↓
        user enters display name
                ↓
        POST /api/auth/me { displayName }
                ↓
        store in UserContext → navigate to /
```

## Out of scope

- UI styling of the onboarding page (layout/design to be addressed separately).
- Preserving the originally intended destination URL through onboarding (post-onboarding always redirects to `/`).
- Allowing users to change their display name after registration (separate feature).
