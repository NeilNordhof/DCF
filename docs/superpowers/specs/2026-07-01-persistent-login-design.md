# Persistent Login (30-Day Remember Me) — Design Spec

**Date:** 2026-07-01
**Branch:** feat/persistent-login

## Overview

Production login (`Auth0LockPasswordless`) uses the implicit flow, which Auth0 hard-caps at 24 hours for access tokens — confirmed directly in the Auth0 dashboard ("Implicit / Hybrid Flow Access Token Lifetime" rejects any value above 86400 seconds). This is a separate, non-configurable limit from the API's general 30-day "Token Expiration" setting, which only applies to Authorization Code/PKCE flows this app doesn't use. So there is no Auth0 dashboard change that gets a 30-day session for this login flow.

Instead, DCF's own backend issues and manages a second, longer-lived credential. The window is **rolling**: any return visit extends it back out to 30 days; 30 days with no return visit (or an explicit logout) ends it. Each device/browser gets its own independent credential — logging in on a new device doesn't affect others.

Dev-mode login (`DevAuthContext`) is unaffected. It already persists indefinitely via `localStorage` with no expiry check at all, so it already exceeds this requirement.

---

## Data Model

### New `RememberMeTokenEntity`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `UserId` | `Guid` | FK → `UserEntity` |
| `TokenHash` | `string` | SHA-256 hash of the raw token. The raw value is returned to the client once at issuance and never stored or retrievable again. |
| `ExpiresAt` | `DateTimeOffset` | Set to `now + 30d` at issuance; bumped back to `now + 30d` on any authenticated activity (see Rolling Extension below) |
| `CreatedAt` | `DateTimeOffset` | Audit only |

One-to-many per user — each successful Lock login creates a new row. This is a deliberate departure from `EmailTokenService`'s stateless HMAC pattern: that pattern proves "this token was issued for this userId" without a database, but it can't be individually revoked (killing one token means rotating the shared secret, which invalidates all of them). A remember-me credential is exactly the case where individual revocation matters — hence a DB-backed opaque token instead. The random token's own entropy (256-bit, base64url-encoded) is what makes it unforgeable; no HMAC secret is needed the way `EmailTokenService` needs one to bind a token to a guessable `userId`.

---

## Backend Components

### Token issuance — `POST /api/auth/remember-me`

- `[Authorize]` — requires a valid Auth0-issued JWT. Called by the frontend immediately after a Lock login succeeds, alongside the existing `/api/auth/me` upsert call.
- Generates a cryptographically random 256-bit token, base64url-encodes it, stores only its SHA-256 hash plus `UserId` and `ExpiresAt = now + 30d`.
- Returns the raw token once in the response body.

### Token validation — second auth path

- Alongside the existing Auth0 JWT Bearer scheme, the API accepts a remember-me token as the bearer credential: hash the presented value, look it up, confirm `ExpiresAt > now`, resolve the associated user.
- Implemented as an additional `AuthenticationScheme` composed via `AddPolicyScheme`, selecting between the Auth0 JWT Bearer handler and this new handler based on whether the presented bearer value parses as a JWT. This mirrors the existing `DevAuthHandler` custom-scheme pattern already registered in `Program.cs`, so it's consistent with how this codebase already swaps auth handlers.

### Rolling extension

- `/api/auth/me` (already called by the frontend on every page load, per `App.tsx`) additionally bumps the caller's `RememberMeTokenEntity.ExpiresAt` to `now + 30d`, if a row exists for the token currently in use.
- This is what makes the window rolling instead of absolute: activity under *either* credential (a still-valid Auth0 token, or the remember-me token once the Auth0 token has lapsed) resets the 30-day clock.

### Revocation — logout

- A new (or extended) logout endpoint deletes the `RememberMeTokenEntity` row matching whichever token the calling device presents.
- Scoped to that one device — other devices' rows are untouched. This is a real improvement over today: `AuthContext.tsx`'s current `logout()` only clears `localStorage` client-side, so a previously-issued Auth0 token would otherwise remain valid until its own natural expiry regardless of "logging out."

---

## Frontend Components (`DCF.Web/src/context/AuthContext.tsx`)

- On Lock's `authenticated` event: after storing the Auth0 token as today, also call `POST /api/auth/remember-me` and store the returned raw token as `dcf_remember_token` in `localStorage`.
- Token-resolution logic (used by `getAccessTokenSilently`): if the stored Auth0 token is still valid, use it — unchanged, cheapest path. If it's expired or missing but `dcf_remember_token` is present, use that as the bearer value instead of rejecting.
- Only when neither credential is present/valid does the app fall back to showing Lock.
- **`isAuthenticated`/`user` state must be updated too, not just the token-getter.** Today, `ProductionLockProvider`'s initial state derives `isAuthenticated` and `user` directly from `storedTokenValid()` (the Auth0 token's own expiry) — that check needs to become "Auth0 token valid OR a `dcf_remember_token` is present," otherwise the app would render as logged out on page load even when the remember-me fallback would have kept the session usable. `dcf_user` itself doesn't need new logic — it's captured once at login and stays valid to display regardless of which credential is currently active.
- `logout()` calls the new revoke endpoint (passing the current remember-me token, if any) before clearing `localStorage`.

---

## Data Flow

1. User logs in via Lock (unchanged) → Auth0 access token + expiry stored, as today.
2. Frontend calls `POST /api/auth/remember-me` → new row created (`ExpiresAt = now + 30d`) → raw token stored client-side.
3. For the next ~24h, the Auth0 token is used as-is. Each page load's `/api/auth/me` call also extends the remember-me row back to `now + 30d`.
4. Once the Auth0 token expires, the frontend sends the remember-me token as the bearer instead. The API validates it via the new auth path; `/api/auth/me` still fires and still extends `ExpiresAt`.
5. If the user is inactive for a full 30 days, or explicitly logs out, the row is gone (expired or deleted) and the app shows Lock again on the next visit.

---

## Multi-Device Behavior

Each login (phone, laptop, etc.) creates an independent `RememberMeTokenEntity` row scoped to that browser's `localStorage`. Logging in on a new device does not affect other devices' sessions. Logging out on one device deletes only that device's row.

---

## Error Handling

Remember-me token invalid, expired, or revoked at validation time → `401` → frontend clears `localStorage` and falls through to showing Lock. Same user-facing behavior as today's "session expired" path, just reached one layer later.

## Out of Scope

Considered and deliberately cut, given this app's low-stakes fantasy-league use case (a compromised login exposes draft picks and league membership, not payment data or sensitive PII):

- **Rotating the token's secret value on each use.** Mitigates a stolen-but-not-yet-used token, but introduces concurrent-request race conditions (two in-flight requests racing a single-use rotation) for real implementation cost.
- **Reuse-detection / mass-revocation** on suspected token theft.
- **A background cleanup job for expired rows.** Dead rows are harmless; lookups filter with `WHERE ExpiresAt > now()`.
- **"View active sessions" / "log out all devices" UI.** The one-to-many data model supports adding this later, but it isn't being built now.

---

## Testing

- **Backend (xUnit),** following the existing `EmailTokenServiceTests.cs` pattern: issuance, validation (valid / expired / revoked / unknown-hash), rolling extension on activity, and logout scoped to a single device's row (other rows unaffected).
- **Frontend (Vitest):** `AuthContext` fallback branching — valid Auth0 token uses it; expired Auth0 token with a valid remember-me token uses the latter; neither valid shows Lock.
