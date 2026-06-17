# Profile Page Redesign + Email Notification Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the Profile page with the site's dark theme, add an email notification toggle backed by a new authenticated PATCH endpoint, and update the Unsubscribe page's "Manage Preferences" link label.

**Architecture:** `EmailNotificationsEnabled` is already a bool on `UserEntity`. We extend `GET /api/auth/me` to surface it, add `PATCH /api/notifications/preferences` for authenticated toggling, thread the field through `UserProfile` and `UserContext`, then rewrite the Profile page as a dark-themed two-card layout.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core InMemory (tests), React 19 / TypeScript / Vite

## Global Constraints

- C#: curly brackets always on a new line; no lambdas for methods; wrap one-line blocks with curly braces; 1 blank line before return; 1 blank line before/after code blocks and awaits; never more than 1 blank line in a row
- TS/JS: `const` by default; template literals for interpolation; destructure when intent is clearer; 1 blank line before return; 1 blank line before/after blocks and awaits; never more than 1 blank line in a row
- No external component libraries for the toggle — CSS-only pill switch
- Learning mode: Tasks 2–3 are pair-programming (user writes TypeScript, Claude reviews). Task 1 and Task 4 are Claude-handled (C# and a two-line label fix).

---

### Task 1: Backend — preferences endpoint + extend auth responses

**Files:**
- Modify: `DCF.Api/Models/NotificationRequests.cs`
- Modify: `DCF.Api/Controllers/NotificationsController.cs`
- Modify: `DCF.Api/Controllers/AuthController.cs`
- Modify: `DCF.Tests/Services/NotificationsControllerTests.cs`

**Interfaces:**
- Produces: `PATCH /api/notifications/preferences` (authenticated, body `{ emailNotificationsEnabled: bool }`, returns 204)
- Produces: `GET /api/auth/me` and `POST /api/auth/me` now include `emailNotificationsEnabled: bool` in their JSON response

- [ ] **Step 1: Write two failing tests for `UpdatePreferences`**

Add to `DCF.Tests/Services/NotificationsControllerTests.cs`, after the existing three tests:

```csharp
private static NotificationsController CreateControllerWithSub(DcfDbContext db, EmailTokenService tokenService, string sub)
{
    var claims = new List<System.Security.Claims.Claim>
    {
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, sub)
    };

    var identity = new System.Security.Claims.ClaimsIdentity(claims);
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    var controller = new NotificationsController(db, tokenService);

    controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
    {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal }
    };

    return controller;
}

[Fact]
public async Task UpdatePreferences_ValidUser_UpdatesFieldAndReturnsNoContent()
{
    using var db = CreateDb("pref_update_valid");
    const string sub = "auth0|pref-user";

    db.Users.Add(new UserEntity
    {
        Id = Guid.NewGuid(),
        Auth0Sub = sub,
        Email = "pref@example.com",
        DisplayName = "Pref User",
        EmailNotificationsEnabled = false
    });

    await db.SaveChangesAsync();

    var tokenService = CreateTokenService();
    var controller = CreateControllerWithSub(db, tokenService, sub);
    var request = new UpdateNotificationPreferencesRequest(true);

    var result = await controller.UpdatePreferences(request);

    Assert.IsType<NoContentResult>(result);

    var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

    Assert.True(user!.EmailNotificationsEnabled);
}

[Fact]
public async Task UpdatePreferences_UserNotFound_ReturnsNotFound()
{
    using var db = CreateDb("pref_update_notfound");
    var tokenService = CreateTokenService();
    var controller = CreateControllerWithSub(db, tokenService, "auth0|ghost");
    var request = new UpdateNotificationPreferencesRequest(true);

    var result = await controller.UpdatePreferences(request);

    Assert.IsType<NotFoundResult>(result);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~NotificationsControllerTests"
```

Expected: two failures — `UpdateNotificationPreferencesRequest` and `UpdatePreferences` do not exist yet.

- [ ] **Step 3: Add the request record to `NotificationRequests.cs`**

```csharp
namespace DCF.Api.Models;

public record UnsubscribeRequest(string Token);

public record UpdateNotificationPreferencesRequest(bool EmailNotificationsEnabled);
```

- [ ] **Step 4: Implement `UpdatePreferences` in `NotificationsController.cs`**

Replace the entire file:

```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(
    DcfDbContext db,
    EmailTokenService emailTokenService) : ControllerBase
{
    [HttpPost("unsubscribe")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        var userId = emailTokenService.ValidateToken(request.Token);

        if (userId is null)
        {

            return BadRequest("Invalid token.");
        }

        var user = await db.Users.FindAsync(userId.Value);

        if (user is null)
        {

            return BadRequest("User not found.");
        }

        user.EmailNotificationsEnabled = false;

        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("preferences")]
    [Authorize]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {

            return NotFound();
        }

        user.EmailNotificationsEnabled = request.EmailNotificationsEnabled;

        await db.SaveChangesAsync();

        return NoContent();
    }
}
```

- [ ] **Step 5: Extend `AuthController` responses to include `EmailNotificationsEnabled`**

In `DCF.Api/Controllers/AuthController.cs`, update both `Ok(new { ... })` calls:

`GetUser` (line 28):
```csharp
return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin, profile.EmailNotificationsEnabled });
```

`UpsertUser` (line 42):
```csharp
return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin, profile.EmailNotificationsEnabled });
```

- [ ] **Step 6: Run all tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Models/NotificationRequests.cs DCF.Api/Controllers/NotificationsController.cs DCF.Api/Controllers/AuthController.cs DCF.Tests/Services/NotificationsControllerTests.cs
git commit -m "feat: add PATCH /api/notifications/preferences endpoint and surface EmailNotificationsEnabled in auth responses"
```

---

### Task 2: Frontend types + API client

> **Learning mode:** You write these changes; Claude will review.

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

**Interfaces:**
- Consumes: `PATCH /api/notifications/preferences` from Task 1
- Produces: `UserProfile.emailNotificationsEnabled: boolean` consumed by Task 3
- Produces: `api.updateNotificationPreferences(enabled: boolean): Promise<void>` consumed by Task 3

- [ ] **Step 1: Add `emailNotificationsEnabled` to `UserProfile`**

In `DCF.Web/src/types/api.ts`, find the `UserProfile` interface (currently lines 108–113) and add the new field:

```ts
export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  emailNotificationsEnabled: boolean;
}
```

- [ ] **Step 2: Add `updateNotificationPreferences` to the API client**

In `DCF.Web/src/api/client.ts`, add after the `unsubscribe` entry (currently line 42):

```ts
updateNotificationPreferences: (emailNotificationsEnabled: boolean) =>
  request<void>('/api/notifications/preferences', { method: 'PATCH', body: JSON.stringify({ emailNotificationsEnabled }) }),
```

- [ ] **Step 3: Verify TypeScript compiles**

```
cd DCF.Web && npm run build
```

Expected: no type errors. (The build may warn about the Profile page still using the old stub — that's fine, it compiles.)

- [ ] **Step 4: Commit**

```
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: add emailNotificationsEnabled to UserProfile type and API client"
```

---

### Task 3: Profile page

> **Learning mode:** You write this file; Claude will review.

**Files:**
- Modify: `DCF.Web/src/pages/Profile.tsx` (full rewrite)
- Modify: `DCF.Web/src/index.css` (add toggle switch styles)

**Interfaces:**
- Consumes: `useUser()` → `user.emailNotificationsEnabled: boolean`, `user.displayName`, `user.email`, `user.isAdmin`
- Consumes: `useDevAuth()` → `logout()`
- Consumes: `api.updateNotificationPreferences(enabled: boolean): Promise<void>`
- Consumes: `setUser(user: UserProfile)` from `useUser()` for optimistic update

**Toggle CSS — add to `DCF.Web/src/index.css`:**

```css
.toggle {
  appearance: none;
  -webkit-appearance: none;
  width: 36px;
  height: 20px;
  background: var(--surface-elevated);
  border-radius: 10px;
  cursor: pointer;
  position: relative;
  transition: background 0.15s;
  flex-shrink: 0;
}

.toggle::after {
  content: '';
  position: absolute;
  width: 14px;
  height: 14px;
  background: #fff;
  border-radius: 50%;
  top: 3px;
  left: 3px;
  transition: transform 0.15s;
}

.toggle:checked {
  background: var(--accent);
}

.toggle:checked::after {
  transform: translateX(16px);
}

.toggle:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
```

**Profile page requirements:**

- `loading` state: render `<div>Loading...</div>` (same as before)
- Two section cards (`var(--surface)` bg, `1px solid var(--border)` border, `8px` border-radius, `20px` padding, `max-width: 480px`, `width: 100%`)
- Cards stacked vertically with `16px` gap
- **Account card:**
  - Card header: "Account" label (`var(--text-muted)`, `10px`, uppercase, `0.5px` letter-spacing, `12px` margin-bottom)
  - Row: "Display name" label (`var(--text-muted)`, `11px`) + value (`var(--text-heading)`, `11px`, `font-weight: 600`)
  - Row: "Email" label + value (same styling)
  - Admin badge if `user.isAdmin`: small pill with `var(--accent-bg)` bg, `var(--accent)` text, `var(--accent-border)` border, `8px 10px` radius — text "Admin"
  - `16px` top margin + Sign Out button: `var(--surface-2)` bg, `var(--border)` border, `4px` radius, `8px 14px` padding, `11px`, `font-weight: 600`, `var(--text-heading)` color
- **Notifications card:**
  - Card header: "Notifications" (same header style as Account card)
  - Row: left side — "Email Notifications" (`var(--text-heading)`, `11px`, `font-weight: 600`) with subtext below "Receive email reminders about upcoming drafts." (`var(--text-muted)`, `10px`); right side — `.toggle` checkbox
  - `saving` local state (`boolean`) — disables the toggle and dims it while the PATCH is in-flight
  - `error` local state (`string | null`) — shown as inline error below the row in `var(--red)`, `10px`
  - On toggle change: set `saving = true`, clear error, call `setUser({ ...user, emailNotificationsEnabled: checked })` (optimistic), call `api.updateNotificationPreferences(checked)`, on success set `saving = false`, on failure revert to old value + set error "Failed to save — try again" + set `saving = false`

- [ ] **Step 1: Add the toggle CSS to `index.css`**

Append the `.toggle` block (shown above) to the end of `DCF.Web/src/index.css`.

- [ ] **Step 2: Write the Profile page**

Rewrite `DCF.Web/src/pages/Profile.tsx` to meet the requirements above.

- [ ] **Step 3: Verify it renders correctly**

```
cd DCF.Web && npm run dev
```

Navigate to `http://localhost:5173/profile`. Check:
- Page title "Profile" is visible
- Both cards render with dark styling
- Email toggle reflects the current `emailNotificationsEnabled` value
- Toggling updates state (the toggle flips) and calls the API (check Network tab)
- Sign Out button works

- [ ] **Step 4: Run lint**

```
cd DCF.Web && npm run lint
```

Expected: no errors.

- [ ] **Step 5: Commit**

```
git add DCF.Web/src/pages/Profile.tsx DCF.Web/src/index.css
git commit -m "feat: rewrite Profile page with dark theme and email notification toggle"
```

---

### Task 4: Unsubscribe label update

> **Claude handles this.**

**Files:**
- Modify: `DCF.Web/src/pages/Unsubscribe.tsx`

**Interfaces:**
- No interface changes — pure copy update.

- [ ] **Step 1: Update both label instances**

In `DCF.Web/src/pages/Unsubscribe.tsx`, change both occurrences of `Manage Preferences` (lines 121 and 169) to `Manage Email Preferences`.

- [ ] **Step 2: Verify**

```
cd DCF.Web && npm run lint
```

Expected: no errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/pages/Unsubscribe.tsx
git commit -m "fix: update 'Manage Preferences' label to 'Manage Email Preferences' on unsubscribe page"
```
