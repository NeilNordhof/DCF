# HTML Email Templates — Design Spec

**Date:** 2026-06-16
**Status:** Approved

## Overview

Replace the inline `<p>` HTML snippets currently passed to `IEmailService.SendAsync` with a static `EmailTemplate` class that generates fully-styled HTML email documents. Emails will match the app's dark theme. A self-contained HMAC-signed unsubscribe token mechanism and a dedicated React unsubscribe page are included.

---

## 1. `EmailTemplate` Static Class

**Location:** `DCF.Api/Services/EmailTemplate.cs`

A static class with one public method per email type. Each method accepts only the variable data it needs and returns a complete HTML email string.

### Public methods

| Method | Parameters | Subject line | CTA |
|---|---|---|---|
| `DraftTomorrow` | `leagueName, leagueId, frontendUrl, unsubscribeToken` | `"Draft tomorrow — {leagueName}"` | "Go to Draft Room" → `/leagues/{leagueId}/draft` |
| `DraftInOneHour` | `leagueName, leagueId, frontendUrl, unsubscribeToken` | `"Draft in 1 hour — {leagueName}"` | "Go to Draft Room" → `/leagues/{leagueId}/draft` |
| `DraftRoomOpen` | `leagueName, openLeadMinutes, leagueId, frontendUrl, unsubscribeToken` | `"Draft room is open — {leagueName}"` | "Go to Draft Room" → `/leagues/{leagueId}/draft` |
| `DraftScheduled` | `action, leagueName, timeStr, leagueId, frontendUrl, unsubscribeToken` | `"Draft {action} — {leagueName}"` | "View League" → `/leagues/{leagueId}` |
| `DraftUnscheduled` | `leagueName, leagueId, frontendUrl, unsubscribeToken` | `"Draft unscheduled — {leagueName}"` | "View League" → `/leagues/{leagueId}` |
| `MemberJoined` | `memberName, leagueName, leagueId, frontendUrl, unsubscribeToken` | `"{memberName} joined {leagueName}"` | "View League" → `/leagues/{leagueId}` |
| `ScoresAvailable` | `showName, frontendUrl, unsubscribeToken` | `"New show scores available — {showName}"` | "View Standings" → `/leagues` |

Each method builds its own CTA URL and unsubscribe URL from `leagueId`, `frontendUrl`, and `unsubscribeToken` — call sites never construct URL strings. Each method returns a `(string subject, string html)` tuple. Note: `DraftScheduled` and `DraftUnscheduled` were added during planning; `LeagueService` has two callers of `NotifyLeagueMembersAsync` that require them.

### Private `Layout` helper

```
Layout(heading, bodyText, ctaText, ctaUrl, unsubscribeUrl) → string
```

All public methods delegate to `Layout`, which produces the full HTML document.

---

## 2. Email Layout & Visual Design

Table-based HTML with fully inlined CSS (required for Outlook/webmail compatibility).

### Structure

```
[outer wrapper — bg: #0d0f14, full-width]
  [centered card — bg: #161822, border: 1px solid #2a2d3a, max-width: 560px, border-radius: 8px]
    [header — "Drum Corps Fantasy" in #c084fc (accent), centered]
    [body]
      [heading — #f3f4f6, ~18px]
      [paragraph — #9ca3af, 14px, line-height 1.6]
    [CTA button — bg: #c084fc, text: #0d0f14, bold, centered, border-radius: 6px]
    [footer — "Unsubscribe" link in #6b7280, centered, small text]
```

### Typography

Font stack: `Arial, Helvetica, sans-serif` (web-safe; system-ui won't load in most email clients).

Base size: 14px body, 18px heading, 12px footer.

### Colors (from `index.css`)

| Role | Value |
|---|---|
| Page background | `#0d0f14` |
| Card background | `#161822` |
| Card border | `#2a2d3a` |
| Heading text | `#f3f4f6` |
| Body text | `#9ca3af` |
| Muted / footer text | `#6b7280` |
| Accent (purple) | `#c084fc` |
| CTA button text | `#0d0f14` |

---

## 3. Unsubscribe Token (`EmailTokenService`)

**Location:** `DCF.Api/Services/EmailTokenService.cs`

A scoped service that generates and validates HMAC-SHA256 tokens. No database storage — the token is self-contained.

### Token format

```
{userId}:{base64url(HMAC-SHA256(userId, secret))}
```

- `userId` is the user's `Guid` as a lowercase string
- The HMAC is computed over the raw `userId` string using the shared secret from config
- The HMAC bytes are base64url-encoded (URL-safe alphabet, no padding) and appended after a `:` separator
- The resulting token is safe to embed in a URL query parameter as-is — no outer encoding needed

### Methods

```csharp
string GenerateToken(Guid userId)
Guid? ValidateToken(string token)   // returns null if tampered or malformed
```

### Config

New field on `EmailOptions`:

```json
"Email": {
  "UnsubscribeSecret": "<random string, min 32 chars>"
}
```

This value must be set in production. A placeholder can be provided for local dev.

### Unsubscribe URL in email

```
{FrontendUrl}/unsubscribe?token={token}
```

`FrontendUrl` is a new field on `EmailOptions` (e.g. `http://localhost:5173` for dev, the production domain in prod).

---

## 4. API Endpoint

**`POST /api/notifications/unsubscribe`**

Request body:
```json
{ "token": "<token string>" }
```

- Validates the token via `EmailTokenService`
- Looks up the user by the decoded `userId`
- Sets `EmailNotificationsEnabled = false` and saves
- Returns `200 OK` on success, `400 Bad Request` if the token is invalid
- No authentication required (the token is the proof of identity)

**Controller:** `NotificationsController` (new), in `DCF.Api/Controllers/`.

---

## 5. Frontend Unsubscribe Page

**Route:** `/unsubscribe`

**File:** `DCF.Web/src/pages/Unsubscribe.tsx`

### Behaviour

1. On mount, reads `?token=...` from the URL and calls `POST /api/notifications/unsubscribe`
2. While pending: shows a loading spinner
3. On success: shows "You've been unsubscribed" confirmation
4. On error (invalid/expired token): shows an error message

### Layout

A centered card in the app's standard dark style (matching other pages). No nav bar required — this page is accessed without being logged in.

Buttons shown after success or error:

- **"Go to Home"** → `/`
- **"Manage Preferences"** → `/profile` (user will need to log in from there if not already)

---

## 6. Call-Site Changes

There are 7 distinct email types across 4 `SendAsync` call sites. All need updating:

| Location | Change |
|---|---|
| `DraftSchedulerService` — draft tomorrow | `EmailTemplate.DraftTomorrow(leagueName, leagueId, frontendUrl, token)` |
| `DraftSchedulerService` — draft in 1 hour | `EmailTemplate.DraftInOneHour(leagueName, leagueId, frontendUrl, token)` |
| `DraftSchedulerService` — draft room open | `EmailTemplate.DraftRoomOpen(leagueName, openLeadMinutes, leagueId, frontendUrl, token)` |
| `LeagueService` — draft scheduled/rescheduled | `EmailTemplate.DraftScheduled(action, leagueName, timeStr, leagueId, frontendUrl, token)` |
| `LeagueService` — draft unscheduled | `EmailTemplate.DraftUnscheduled(leagueName, leagueId, frontendUrl, token)` |
| `LeagueService` — member joined | `EmailTemplate.MemberJoined(memberName, leagueName, leagueId, frontendUrl, token)` |
| `ScrapeSchedulerService` — scores available | `EmailTemplate.ScoresAvailable(showName, frontendUrl, token)` |

Each call site:
1. Injects `EmailTokenService` and `IOptions<EmailOptions>` (both singleton — injected directly into constructor)
2. Generates a per-recipient token via `emailTokenService.GenerateToken(userId)` inside the per-member loop
3. Passes `token`, `leagueId`, and `frontendUrl` to the `EmailTemplate.*` method — no URL strings constructed at the call site
4. Passes the returned `(subject, html)` tuple to `emailService.SendAsync`

The private `NotifyLeagueMembersAsync` helpers are refactored to accept a factory delegate that receives `(leagueName, token)` (DraftSchedulerService) or just `token` (LeagueService), with `leagueId` and `frontendUrl` captured in the closure at the call site.

---

## 7. Config Summary

All new fields added to `EmailOptions` (existing class in `SmtpEmailService.cs`):

```json
"Email": {
  "FrontendUrl": "http://localhost:5173",
  "UnsubscribeSecret": "<random string>"
}
```

`FrontendUrl` replaces the need for a separate `ApiBaseUrl` — the unsubscribe URL points to the frontend route, not the API directly.

---

## Out of Scope

- Re-subscribe flow (users can re-enable via their profile settings)
- Email open/click tracking
- Per-notification-type preferences (it's a single on/off toggle)
- Token expiry (the token is valid indefinitely; the worst case is a stale unsubscribe for a deleted user, which is a no-op)
