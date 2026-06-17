---
name: profile-redesign-email-toggle
description: Profile page dark redesign + email notification toggle (Issue #35)
metadata:
  type: project
---

# Profile Page Redesign + Email Notification Toggle

**Issue:** #35 | **Milestone:** Beta Release

## Overview

The Profile page is currently an unstyled stub. This spec covers three things:
1. Applying the site dark theme to the Profile page
2. Adding an `EmailNotificationsEnabled` toggle backed by a new PATCH endpoint
3. Updating the "Manage Preferences" label on the Unsubscribe page

---

## Backend

### Extend `GET /api/auth/me` and `POST /api/auth/me`

Both actions on `AuthController` return an anonymous object. Add `EmailNotificationsEnabled` to each:

```csharp
return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin, profile.EmailNotificationsEnabled });
```

### New endpoint: `PATCH /api/notifications/preferences`

- **Controller:** `NotificationsController`
- **Auth:** Requires JWT (`[Authorize]`). Move `[AllowAnonymous]` from the class level down to the `Unsubscribe` action only.
- **Request body:** `{ emailNotificationsEnabled: bool }`
- **Response:** `204 No Content`
- **Logic:** Look up the user by the `sub` claim, set `EmailNotificationsEnabled`, save.

**New request model** in `NotificationRequests.cs`:

```csharp
public record UpdateNotificationPreferencesRequest(bool EmailNotificationsEnabled);
```

---

## Frontend

### `UserProfile` type (`src/types/api.ts`)

Add `emailNotificationsEnabled: boolean` to the `UserProfile` interface. No other type changes needed — `UserContext` and `setUser` carry the field automatically once the API returns it.

### API client (`src/api/client.ts`)

Add one method to the `api` object:

```ts
updateNotificationPreferences: (emailNotificationsEnabled: boolean) =>
  request<void>('/api/notifications/preferences', {
    method: 'PATCH',
    body: JSON.stringify({ emailNotificationsEnabled }),
  }),
```

### Profile page (`src/pages/Profile.tsx`) — written by user, reviewed by Claude

Layout inside `AuthenticatedLayout` (Nav + 1200px content div):

- **Page heading**: "Profile", `var(--text-heading)`, 18px bold
- **Account card**: `var(--surface)` bg, `var(--border)` border, 8px radius, 20px padding
  - Display name (label + value)
  - Email (label + value)
  - Admin badge if `user.isAdmin`
  - Sign Out button at card bottom
- **Notifications card**: same card styling
  - Row: "Email Notifications" label (left) + toggle switch (right)
  - Subtext: "Receive email reminders about upcoming drafts."
  - Toggle reflects `user.emailNotificationsEnabled` from `UserContext`
  - On change: optimistic update via `setUser`, call `api.updateNotificationPreferences`, revert + show inline error on failure
  - Toggle disabled during in-flight request

**Toggle styling**: CSS-only pill switch via `<input type="checkbox">` — no external library. Checked state uses `var(--accent)`.

### Unsubscribe page (`src/pages/Unsubscribe.tsx`) — handled by Claude

Both the `success` and `error` states have a link to `/profile` labelled "Manage Preferences". Change both to "Manage Email Preferences".

---

## Constraints

- No new pages or routes
- No external component libraries for the toggle
- `UserContext` shape changes propagate automatically — no consumers need updating beyond the Profile page itself
