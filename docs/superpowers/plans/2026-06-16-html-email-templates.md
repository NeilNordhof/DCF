# HTML Email Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace bare `<p>` snippets passed to `IEmailService.SendAsync` with a static `EmailTemplate` class that generates fully-styled dark-themed HTML email documents, add HMAC-signed unsubscribe tokens, and add a React unsubscribe page.

**Architecture:** A static `EmailTemplate` class (one method per email type, all returning `(string subject, string html)`) wraps a private `Layout` helper that builds the full table-based HTML document. An `EmailTokenService` generates and validates HMAC-SHA256 self-contained tokens for unsubscribing. A new `NotificationsController` and a React `/unsubscribe` page complete the flow.

**Tech Stack:** C#/.NET 10, `System.Security.Cryptography.HMACSHA256`, `System.Net.WebUtility.HtmlEncode`, xUnit, React 19, React Router

## Global Constraints

- All C# braces on new lines; no lambdas for methods; one blank line before `return`; one blank line before/after `await` and code blocks; never more than one consecutive blank line.
- All TypeScript: `const` by default; template literals for interpolation; one blank line before `return`; one blank line before/after `await` and blocks.
- Follow primary constructor DI pattern used throughout the project.
- TDD: write and run the failing test before writing implementation.
- Frequent commits: one commit per task.
- Note: the spec listed 5 email types but `LeagueService` contains 7 distinct types — `DraftScheduled` and `DraftUnscheduled` are added here to cover the two callers of `LeagueService.NotifyLeagueMembersAsync` (lines 368 and 374).

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Modify | `DCF.Api/Services/SmtpEmailService.cs` | Add `FrontendUrl`, `UnsubscribeSecret` to `EmailOptions` |
| Modify | `DCF.Api/appsettings.json` | Add new `Email` config values |
| Create | `DCF.Api/Services/EmailTokenService.cs` | HMAC token generation + validation |
| Modify | `DCF.Api/Program.cs` | Register `EmailTokenService` as singleton |
| Create | `DCF.Tests/Services/EmailTokenServiceTests.cs` | Token round-trip, tamper, malformed tests |
| Create | `DCF.Api/Services/EmailTemplate.cs` | Static class with 7 typed email methods + `Layout` |
| Create | `DCF.Tests/Services/EmailTemplateTests.cs` | Per-method subject/HTML content tests |
| Create | `DCF.Api/Models/NotificationRequests.cs` | `UnsubscribeRequest` record |
| Create | `DCF.Api/Controllers/NotificationsController.cs` | `POST /api/notifications/unsubscribe` |
| Create | `DCF.Tests/Services/NotificationsControllerTests.cs` | Valid token, invalid token, idempotent tests |
| Modify | `DCF.Api/Services/DraftSchedulerService.cs` | Constructor + refactor `NotifyLeagueMembersAsync` + 3 call sites |
| Modify | `DCF.Api/Services/LeagueService.cs` | Constructor + refactor `NotifyLeagueMembersAsync` + 3 call sites |
| Modify | `DCF.Api/Services/ScrapeSchedulerService.cs` | Constructor + 1 call site |
| Modify | `DCF.Web/src/api/client.ts` | Add `unsubscribe` API method |
| Create | `DCF.Web/src/pages/Unsubscribe.tsx` | React unsubscribe page |
| Modify | `DCF.Web/src/main.tsx` | Add `/unsubscribe` route |

---

## Task 1: Config — add `FrontendUrl` and `UnsubscribeSecret` to `EmailOptions`

**Files:**
- Modify: `DCF.Api/Services/SmtpEmailService.cs`
- Modify: `DCF.Api/appsettings.json`

**Interfaces:**
- Produces: `EmailOptions.FrontendUrl` and `EmailOptions.UnsubscribeSecret` — used in Tasks 2, 5

- [ ] **Step 1: Add properties to `EmailOptions`**

In `DCF.Api/Services/SmtpEmailService.cs`, the `EmailOptions` class currently ends at `public bool StartTls`. Add two properties immediately after it:

```csharp
public string FrontendUrl { get; set; } = string.Empty;
public string UnsubscribeSecret { get; set; } = string.Empty;
```

The full `EmailOptions` class should look like:

```csharp
public class EmailOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1025;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Drum Corps Fantasy";
    public bool StartTls { get; set; } = false;
    public string FrontendUrl { get; set; } = string.Empty;
    public string UnsubscribeSecret { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Add values to `appsettings.json`**

In `DCF.Api/appsettings.json`, update the `"Email"` section:

```json
"Email": {
  "Host": "",
  "Port": 587,
  "Username": "resend",
  "Password": "",
  "FromAddress": "",
  "FromName": "Drum Corps Fantasy",
  "StartTls": true,
  "FrontendUrl": "http://localhost:5173",
  "UnsubscribeSecret": "dev-unsubscribe-secret-change-in-production"
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DCF.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DCF.Api/Services/SmtpEmailService.cs DCF.Api/appsettings.json
git commit -m "feat: add FrontendUrl and UnsubscribeSecret to EmailOptions"
```

---

## Task 2: Implement `EmailTokenService`

**Files:**
- Create: `DCF.Api/Services/EmailTokenService.cs`
- Modify: `DCF.Api/Program.cs`
- Create: `DCF.Tests/Services/EmailTokenServiceTests.cs`

**Interfaces:**
- Consumes: `EmailOptions.UnsubscribeSecret` from Task 1
- Produces:
  - `EmailTokenService.GenerateToken(Guid userId) → string`
  - `EmailTokenService.ValidateToken(string token) → Guid?`
  - Registered as singleton in DI

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/EmailTokenServiceTests.cs`:

```csharp
using DCF.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace DCF.Tests.Services;

public class EmailTokenServiceTests
{
    private static EmailTokenService Create(string secret = "test-secret-32-chars-minimum-len!")
    {
        var opts = Options.Create(new EmailOptions { UnsubscribeSecret = secret });

        return new EmailTokenService(opts);
    }

    [Fact]
    public void GenerateToken_ValidateToken_RoundTrip()
    {
        var svc = Create();
        var userId = Guid.NewGuid();
        var token = svc.GenerateToken(userId);
        var result = svc.ValidateToken(token);

        Assert.Equal(userId, result);
    }

    [Fact]
    public void ValidateToken_TamperedHmac_ReturnsNull()
    {
        var svc = Create();
        var token = svc.GenerateToken(Guid.NewGuid());
        var id = token[..token.IndexOf(':')];
        var tampered = $"{id}:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.Null(svc.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var svc1 = Create("secret-one-32-chars-minimum-lena");
        var svc2 = Create("secret-two-32-chars-minimum-lenb");
        var token = svc1.GenerateToken(Guid.NewGuid());

        Assert.Null(svc2.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_MalformedToken_ReturnsNull()
    {
        var svc = Create();

        Assert.Null(svc.ValidateToken("not-a-valid-token"));
        Assert.Null(svc.ValidateToken(""));
        Assert.Null(svc.ValidateToken("::::"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTokenServiceTests"`
Expected: Build error — `EmailTokenService` does not exist yet.

- [ ] **Step 3: Create `EmailTokenService.cs`**

Create `DCF.Api/Services/EmailTokenService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DCF.Api.Services;

public class EmailTokenService(IOptions<EmailOptions> options)
{
    public string GenerateToken(Guid userId)
    {
        var id = userId.ToString("N");
        var hmac = ComputeHmac(id, options.Value.UnsubscribeSecret);

        return $"{id}:{hmac}";
    }

    public Guid? ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var colonIndex = token.IndexOf(':');

        if (colonIndex < 0)
        {
            return null;
        }

        var id = token[..colonIndex];
        var providedHmac = token[(colonIndex + 1)..];

        if (!Guid.TryParseExact(id, "N", out var userId))
        {
            return null;
        }

        var expectedHmac = ComputeHmac(id, options.Value.UnsubscribeSecret);

        try
        {
            var expectedBytes = Convert.FromBase64String(PadBase64(expectedHmac));
            var providedBytes = Convert.FromBase64String(PadBase64(providedHmac));

            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }

        return userId;
    }

    private static string ComputeHmac(string data, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hash = HMACSHA256.HashData(keyBytes, dataBytes);

        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string PadBase64(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');

        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }
}
```

- [ ] **Step 4: Register `EmailTokenService` in DI**

In `DCF.Api/Program.cs`, add immediately after the existing `IEmailService` registration (line 62):

```csharp
builder.Services.AddSingleton<EmailTokenService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTokenServiceTests"`
Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/EmailTokenService.cs DCF.Api/Program.cs DCF.Tests/Services/EmailTokenServiceTests.cs
git commit -m "feat: add EmailTokenService for HMAC-signed unsubscribe tokens"
```

---

## Task 3: Implement `EmailTemplate` static class

**Files:**
- Create: `DCF.Api/Services/EmailTemplate.cs`
- Create: `DCF.Tests/Services/EmailTemplateTests.cs`

**Interfaces:**
- Produces (all return `(string subject, string html)`):
  - `EmailTemplate.DraftTomorrow(string leagueName, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.DraftInOneHour(string leagueName, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.DraftRoomOpen(string leagueName, int openLeadMinutes, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.DraftScheduled(string action, string leagueName, string timeStr, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.DraftUnscheduled(string leagueName, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.MemberJoined(string memberName, string leagueName, string ctaUrl, string unsubscribeUrl)`
  - `EmailTemplate.ScoresAvailable(string showName, string ctaUrl, string unsubscribeUrl)`

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/EmailTemplateTests.cs`:

```csharp
using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class EmailTemplateTests
{
    [Fact]
    public void DraftTomorrow_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftTomorrow(
            "Test League", "http://cta.test/draft", "http://unsub.test");

        Assert.Equal("Draft tomorrow — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("http://cta.test/draft", html);
        Assert.Contains("http://unsub.test", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftInOneHour_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftInOneHour(
            "Test League", "http://cta.test/draft", "http://unsub.test");

        Assert.Equal("Draft in 1 hour — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftRoomOpen_SubjectAndHtmlContainLeagueNameAndMinutes()
    {
        var (subject, html) = EmailTemplate.DraftRoomOpen(
            "Test League", 10, "http://cta.test/draft", "http://unsub.test");

        Assert.Equal("Draft room is open — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("10", html);
        Assert.Contains("Go to Draft Room", html);
    }

    [Fact]
    public void DraftScheduled_SubjectAndHtmlContainActionAndTime()
    {
        var (subject, html) = EmailTemplate.DraftScheduled(
            "scheduled", "Test League", "Monday, June 16 at 7:00 PM UTC",
            "http://cta.test", "http://unsub.test");

        Assert.Equal("Draft scheduled — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("Monday, June 16 at 7:00 PM UTC", html);
        Assert.Contains("View League", html);
    }

    [Fact]
    public void DraftUnscheduled_SubjectAndHtmlContainLeagueName()
    {
        var (subject, html) = EmailTemplate.DraftUnscheduled(
            "Test League", "http://cta.test", "http://unsub.test");

        Assert.Equal("Draft unscheduled — Test League", subject);
        Assert.Contains("Test League", html);
        Assert.Contains("View League", html);
    }

    [Fact]
    public void MemberJoined_SubjectAndHtmlContainMemberAndLeagueNames()
    {
        var (subject, html) = EmailTemplate.MemberJoined(
            "Alice", "Test League", "http://cta.test", "http://unsub.test");

        Assert.Equal("Alice joined Test League", subject);
        Assert.Contains("Alice", html);
        Assert.Contains("Test League", html);
        Assert.Contains("View League", html);
    }

    [Fact]
    public void ScoresAvailable_SubjectAndHtmlContainShowName()
    {
        var (subject, html) = EmailTemplate.ScoresAvailable(
            "Drum Corps West", "http://cta.test/leagues", "http://unsub.test");

        Assert.Equal("New show scores available — Drum Corps West", subject);
        Assert.Contains("Drum Corps West", html);
        Assert.Contains("View Standings", html);
    }

    [Fact]
    public void EmailTemplate_HtmlEncodesUserContent()
    {
        var (_, html) = EmailTemplate.DraftTomorrow(
            "<script>alert(1)</script>", "http://cta.test", "http://unsub.test");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTemplateTests"`
Expected: Build error — `EmailTemplate` does not exist yet.

- [ ] **Step 3: Create `EmailTemplate.cs`**

Create `DCF.Api/Services/EmailTemplate.cs`:

```csharp
using System.Net;

namespace DCF.Api.Services;

public static class EmailTemplate
{
    public static (string subject, string html) DraftTomorrow(
        string leagueName,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft tomorrow — {leagueName}",
            Layout(
                heading: $"Draft tomorrow — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft is tomorrow! Make sure you're ready to pick.",
                ctaText: "Go to Draft Room",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) DraftInOneHour(
        string leagueName,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft in 1 hour — {leagueName}",
            Layout(
                heading: $"Draft in 1 hour — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft starts in 1 hour!",
                ctaText: "Go to Draft Room",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) DraftRoomOpen(
        string leagueName,
        int openLeadMinutes,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft room is open — {leagueName}",
            Layout(
                heading: $"Draft room is open — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft room is now open! The draft starts in {openLeadMinutes} minutes.",
                ctaText: "Go to Draft Room",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) DraftScheduled(
        string action,
        string leagueName,
        string timeStr,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safeName = WebUtility.HtmlEncode(leagueName);
        var safeAction = WebUtility.HtmlEncode(action);
        var safeTime = WebUtility.HtmlEncode(timeStr);

        return (
            $"Draft {action} — {leagueName}",
            Layout(
                heading: $"Draft {safeAction} — {safeName}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safeName}</strong> draft has been {safeAction} for <strong style=\"color: #f3f4f6;\">{safeTime}</strong>.",
                ctaText: "View League",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) DraftUnscheduled(
        string leagueName,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safe = WebUtility.HtmlEncode(leagueName);

        return (
            $"Draft unscheduled — {leagueName}",
            Layout(
                heading: $"Draft unscheduled — {safe}",
                body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft has been unscheduled. A new date will be set by the commissioner.",
                ctaText: "View League",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) MemberJoined(
        string memberName,
        string leagueName,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safeMember = WebUtility.HtmlEncode(memberName);
        var safeName = WebUtility.HtmlEncode(leagueName);

        return (
            $"{memberName} joined {leagueName}",
            Layout(
                heading: $"{safeMember} joined {safeName}",
                body: $"<strong style=\"color: #f3f4f6;\">{safeMember}</strong> has joined your league <strong style=\"color: #f3f4f6;\">{safeName}</strong>.",
                ctaText: "View League",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    public static (string subject, string html) ScoresAvailable(
        string showName,
        string ctaUrl,
        string unsubscribeUrl)
    {
        var safe = WebUtility.HtmlEncode(showName);

        return (
            $"New show scores available — {showName}",
            Layout(
                heading: "New scores available",
                body: $"Scores from <strong style=\"color: #f3f4f6;\">{safe}</strong> are now available. Check your standings!",
                ctaText: "View Standings",
                ctaUrl: ctaUrl,
                unsubscribeUrl: unsubscribeUrl));
    }

    private static string Layout(
        string heading,
        string body,
        string ctaText,
        string ctaUrl,
        string unsubscribeUrl)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            </head>
            <body style="margin: 0; padding: 20px; background-color: #0d0f14; font-family: Arial, Helvetica, sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                <tr>
                  <td align="center">
                    <table cellpadding="0" cellspacing="0" role="presentation"
                           style="max-width: 560px; width: 100%; background-color: #161822; border: 1px solid #2a2d3a; border-radius: 8px; overflow: hidden;">
                      <tr>
                        <td style="padding: 24px 32px; border-bottom: 1px solid #2a2d3a;">
                          <p style="margin: 0; font-size: 14px; font-weight: bold; color: #c084fc; letter-spacing: 0.08em; text-transform: uppercase;">
                            Drum Corps Fantasy
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding: 32px;">
                          <h1 style="margin: 0 0 16px; font-size: 20px; font-weight: bold; color: #f3f4f6; line-height: 1.3;">
                            {heading}
                          </h1>
                          <p style="margin: 0 0 28px; font-size: 14px; line-height: 1.6; color: #9ca3af;">
                            {body}
                          </p>
                          <table cellpadding="0" cellspacing="0" role="presentation">
                            <tr>
                              <td style="border-radius: 6px; background-color: #c084fc;">
                                <a href="{ctaUrl}"
                                   style="display: inline-block; padding: 12px 24px; font-size: 14px; font-weight: bold; color: #0d0f14; text-decoration: none; border-radius: 6px;">
                                  {ctaText}
                                </a>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding: 20px 32px; border-top: 1px solid #2a2d3a; text-align: center;">
                          <p style="margin: 0; font-size: 12px; color: #6b7280;">
                            <a href="{unsubscribeUrl}" style="color: #6b7280; text-decoration: underline;">Unsubscribe</a> from email notifications
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTemplateTests"`
Expected: 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Services/EmailTemplate.cs DCF.Tests/Services/EmailTemplateTests.cs
git commit -m "feat: add EmailTemplate static class with 7 typed email methods"
```

---

## Task 4: Add `NotificationsController` (unsubscribe endpoint)

**Files:**
- Create: `DCF.Api/Models/NotificationRequests.cs`
- Create: `DCF.Api/Controllers/NotificationsController.cs`
- Create: `DCF.Tests/Services/NotificationsControllerTests.cs`

**Interfaces:**
- Consumes: `EmailTokenService.ValidateToken` from Task 2; `DcfDbContext`
- Produces: `POST /api/notifications/unsubscribe` — accepts `{ "token": "..." }`, no auth required

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/NotificationsControllerTests.cs`:

```csharp
using DCF.Api.Controllers;
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DCF.Tests.Services;

public class NotificationsControllerTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static EmailTokenService CreateTokenService()
    {
        var opts = Options.Create(new EmailOptions { UnsubscribeSecret = "test-secret-32-chars-minimum-len!" });

        return new EmailTokenService(opts);
    }

    [Fact]
    public async Task Unsubscribe_ValidToken_DisablesNotificationsAndReturnsOk()
    {
        using var db = CreateDb("unsub_valid");
        var userId = Guid.NewGuid();

        db.Users.Add(new UserEntity
        {
            Id = userId,
            Auth0Sub = "auth0|test",
            Email = "user@example.com",
            DisplayName = "Test User",
            EmailNotificationsEnabled = true
        });

        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var token = tokenService.GenerateToken(userId);
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest(token));

        Assert.IsType<OkResult>(result);

        var user = await db.Users.FindAsync(userId);

        Assert.False(user!.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task Unsubscribe_InvalidToken_ReturnsBadRequest()
    {
        using var db = CreateDb("unsub_invalid");
        var tokenService = CreateTokenService();
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest("not-valid"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Unsubscribe_ValidTokenAlreadyUnsubscribed_ReturnsOk()
    {
        using var db = CreateDb("unsub_idempotent");
        var userId = Guid.NewGuid();

        db.Users.Add(new UserEntity
        {
            Id = userId,
            Auth0Sub = "auth0|test2",
            Email = "user2@example.com",
            DisplayName = "Test User 2",
            EmailNotificationsEnabled = false
        });

        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var token = tokenService.GenerateToken(userId);
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest(token));

        Assert.IsType<OkResult>(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~NotificationsControllerTests"`
Expected: Build error — `NotificationsController` and `UnsubscribeRequest` do not exist yet.

- [ ] **Step 3: Create `NotificationRequests.cs`**

Create `DCF.Api/Models/NotificationRequests.cs`:

```csharp
namespace DCF.Api.Models;

public record UnsubscribeRequest(string Token);
```

- [ ] **Step 4: Create `NotificationsController.cs`**

Create `DCF.Api/Controllers/NotificationsController.cs`:

```csharp
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[AllowAnonymous]
public class NotificationsController(
    DcfDbContext db,
    EmailTokenService emailTokenService) : ControllerBase
{
    [HttpPost("unsubscribe")]
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

        return Ok();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~NotificationsControllerTests"`
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Models/NotificationRequests.cs DCF.Api/Controllers/NotificationsController.cs DCF.Tests/Services/NotificationsControllerTests.cs
git commit -m "feat: add unsubscribe endpoint POST /api/notifications/unsubscribe"
```

---

## Task 5: Update backend call sites to use `EmailTemplate`

**Files:**
- Modify: `DCF.Api/Services/DraftSchedulerService.cs`
- Modify: `DCF.Api/Services/LeagueService.cs`
- Modify: `DCF.Api/Services/ScrapeSchedulerService.cs`

**Interfaces:**
- Consumes:
  - `EmailTemplate.*` methods from Task 3 (all returning `(string subject, string html)`)
  - `EmailTokenService.GenerateToken` from Task 2
  - `EmailOptions.FrontendUrl` from Task 1

No new tests — existing tests don't assert email content and the observable behavior (who gets notified, when) is unchanged. Build success is the verification.

- [ ] **Step 1: Update `DraftSchedulerService`**

In `DCF.Api/Services/DraftSchedulerService.cs`:

**1a. Add `using` directives** at the top of the file:

```csharp
using Microsoft.Extensions.Options;
```

**1b. Add two parameters to the primary constructor** (currently `IServiceScopeFactory scopeFactory, ILogger<DraftSchedulerService> logger`):

```csharp
public class DraftSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<DraftSchedulerService> logger,
    IOptions<EmailOptions> emailOptions,
    EmailTokenService emailTokenService) : BackgroundService
```

**1c. Replace the 3 `NotifyLeagueMembersAsync` call sites** (the `await NotifyLeagueMembersAsync(...)` calls inside the `ScheduleNext` task). Replace all 3 with:

```csharp
// 24-hour reminder
await NotifyLeagueMembersAsync(leagueId,
    $"{emailOptions.Value.FrontendUrl}/leagues/{leagueId}/draft",
    EmailTemplate.DraftTomorrow);

// 1-hour reminder
await NotifyLeagueMembersAsync(leagueId,
    $"{emailOptions.Value.FrontendUrl}/leagues/{leagueId}/draft",
    EmailTemplate.DraftInOneHour);

// Room open
await NotifyLeagueMembersAsync(leagueId,
    $"{emailOptions.Value.FrontendUrl}/leagues/{leagueId}/draft",
    (leagueName, ctaUrl, unsubscribeUrl) =>
        EmailTemplate.DraftRoomOpen(leagueName, (int)OpenLeadTime.TotalMinutes, ctaUrl, unsubscribeUrl));
```

**1d. Replace `NotifyLeagueMembersAsync` signature and body** (the private method at the bottom of the file). Change from `Func<string, string>` factories to a single template factory:

```csharp
private async Task NotifyLeagueMembersAsync(
    Guid leagueId,
    string ctaUrl,
    Func<string, string, string, (string subject, string html)> templateFactory)
{
    try
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DcfDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Id == leagueId);

        if (league is null)
        {
            return;
        }

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId && m.User.EmailNotificationsEnabled)
            .Select(m => m.User)
            .ToListAsync();

        foreach (var member in members)
        {
            var unsubscribeUrl = $"{emailOptions.Value.FrontendUrl}/unsubscribe?token={emailTokenService.GenerateToken(member.Id)}";
            var (subject, html) = templateFactory(league.Name, ctaUrl, unsubscribeUrl);

            await emailService.SendAsync(member.Email, member.DisplayName, subject, html);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send notifications for league {LeagueId}", leagueId);
    }
}
```

- [ ] **Step 2: Update `LeagueService`**

In `DCF.Api/Services/LeagueService.cs`:

**2a. Add `using` directive** at the top:

```csharp
using Microsoft.Extensions.Options;
```

**2b. Add two parameters to the primary constructor** (add after `IEmailService emailService`):

```csharp
public class LeagueService(
    DcfDbContext db,
    DraftSchedulerService draftScheduler,
    IStandingsService standingsService,
    IEmailService emailService,
    IOptions<EmailOptions> emailOptions,
    EmailTokenService emailTokenService,
    ILogger<LeagueService> logger) : ILeagueService
```

**2c. Replace the member-joined notification** (around line 193). Replace the `try` block that calls `emailService.SendAsync` with:

```csharp
try
{
    var ctaUrl = $"{emailOptions.Value.FrontendUrl}/leagues/{leagueId}";
    var unsubscribeUrl = $"{emailOptions.Value.FrontendUrl}/unsubscribe?token={emailTokenService.GenerateToken(commissioner.Id)}";
    var (subject, html) = EmailTemplate.MemberJoined(user.DisplayName, league.Name, ctaUrl, unsubscribeUrl);

    await emailService.SendAsync(commissioner.Email, commissioner.DisplayName, subject, html);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Failed to send member-joined notification for league {LeagueId}", leagueId);
}
```

**2d. Replace the two `NotifyLeagueMembersAsync` call sites** (around lines 368 and 374). These build the `timeStr` and `action` variables above them — keep those, and replace the two calls:

```csharp
if (req.DraftStartTime.HasValue)
{
    var timeStr = req.DraftStartTime.Value.ToUniversalTime().ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");
    var action = wasScheduled ? "rescheduled" : "scheduled";
    var leagueUrl = $"{emailOptions.Value.FrontendUrl}/leagues/{league.Id}";

    await NotifyLeagueMembersAsync(league.Id, leagueUrl,
        (ctaUrl, unsubUrl) => EmailTemplate.DraftScheduled(action, league.Name, timeStr, ctaUrl, unsubUrl));
}
else if (wasScheduled)
{
    var leagueUrl = $"{emailOptions.Value.FrontendUrl}/leagues/{league.Id}";

    await NotifyLeagueMembersAsync(league.Id, leagueUrl,
        (ctaUrl, unsubUrl) => EmailTemplate.DraftUnscheduled(league.Name, ctaUrl, unsubUrl));
}
```

**2e. Replace `NotifyLeagueMembersAsync` signature and body** (private method around line 400). Change from `(Guid leagueId, string subject, string html)` to:

```csharp
private async Task NotifyLeagueMembersAsync(
    Guid leagueId,
    string ctaUrl,
    Func<string, string, (string subject, string html)> messageFactory)
{
    try
    {
        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId && m.User.EmailNotificationsEnabled)
            .Select(m => m.User)
            .ToListAsync();

        foreach (var member in members)
        {
            var unsubscribeUrl = $"{emailOptions.Value.FrontendUrl}/unsubscribe?token={emailTokenService.GenerateToken(member.Id)}";
            var (subject, html) = messageFactory(ctaUrl, unsubscribeUrl);

            await emailService.SendAsync(member.Email, member.DisplayName, subject, html);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send notifications for league {LeagueId}", leagueId);
    }
}
```

- [ ] **Step 3: Update `ScrapeSchedulerService`**

In `DCF.Api/Services/ScrapeSchedulerService.cs`:

**3a. Add `using` directive** at the top:

```csharp
using Microsoft.Extensions.Options;
```

**3b. Add two parameters to the primary constructor** (after `IConfiguration config`):

```csharp
public class ScrapeSchedulerService(
    IServiceScopeFactory scopeFactory,
    IMqttService mqtt,
    IConfiguration config,
    IOptions<EmailOptions> emailOptions,
    EmailTokenService emailTokenService,
    ILogger<ScrapeSchedulerService> logger) : BackgroundService
```

**3c. Replace the `emailService.SendAsync` call** (around line 177). The surrounding `foreach (var user in users)` loop stays unchanged; only the body changes:

```csharp
foreach (var user in users)
{
    var ctaUrl = $"{emailOptions.Value.FrontendUrl}/leagues";
    var unsubscribeUrl = $"{emailOptions.Value.FrontendUrl}/unsubscribe?token={emailTokenService.GenerateToken(user.Id)}";
    var (subject, html) = EmailTemplate.ScoresAvailable(showName, ctaUrl, unsubscribeUrl);

    await emailService.SendAsync(user.Email, user.DisplayName, subject, html);
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build DCF.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test DCF.Tests/DCF.Tests.csproj`
Expected: All existing tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/DraftSchedulerService.cs DCF.Api/Services/LeagueService.cs DCF.Api/Services/ScrapeSchedulerService.cs
git commit -m "feat: update email call sites to use EmailTemplate with styled HTML"
```

---

## Task 6: Frontend unsubscribe page

**Files:**
- Modify: `DCF.Web/src/api/client.ts`
- Create: `DCF.Web/src/pages/Unsubscribe.tsx`
- Modify: `DCF.Web/src/main.tsx`

**Interfaces:**
- Consumes: `POST /api/notifications/unsubscribe` from Task 4

- [ ] **Step 1: Add `unsubscribe` to the API client**

In `DCF.Web/src/api/client.ts`, add to the `api` object (e.g., after `upsertUser`):

```typescript
unsubscribe: (token: string) =>
  request<void>('/api/notifications/unsubscribe', { method: 'POST', body: JSON.stringify({ token }) }),
```

- [ ] **Step 2: Create `Unsubscribe.tsx`**

Create `DCF.Web/src/pages/Unsubscribe.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';

export function Unsubscribe() {
  const [searchParams] = useSearchParams();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');

  useEffect(() => {
    const token = searchParams.get('token');

    if (!token) {
      setStatus('error');
      return;
    }

    api.unsubscribe(token)
      .then(() => setStatus('success'))
      .catch(() => setStatus('error'));
  }, [searchParams]);

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100svh', padding: '20px' }}>
      <div style={{ maxWidth: '480px', width: '100%', backgroundColor: 'var(--surface)', border: '1px solid var(--border)', borderRadius: '8px', padding: '32px' }}>
        {status === 'loading' && (
          <p style={{ color: 'var(--text)' }}>Processing...</p>
        )}
        {status === 'success' && (
          <>
            <h2>You&apos;ve been unsubscribed</h2>
            <p style={{ color: 'var(--text)', margin: '12px 0 24px' }}>
              You won&apos;t receive any more email notifications from Drum Corps Fantasy.
            </p>
            <div style={{ display: 'flex', gap: '12px' }}>
              <Link to="/" style={{ color: 'var(--accent)' }}>Go to Home</Link>
              <Link to="/profile" style={{ color: 'var(--accent)' }}>Manage Preferences</Link>
            </div>
          </>
        )}
        {status === 'error' && (
          <>
            <h2>Something went wrong</h2>
            <p style={{ color: 'var(--text)', margin: '12px 0 24px' }}>
              This unsubscribe link may be invalid.
            </p>
            <Link to="/" style={{ color: 'var(--accent)' }}>Go to Home</Link>
          </>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Add the route and import in `main.tsx`**

In `DCF.Web/src/main.tsx`, add the import after the existing page imports:

```tsx
import { Unsubscribe } from './pages/Unsubscribe';
```

Add the route inside the `children` array (this page is public — no `ProtectedRoute` wrapper):

```tsx
{ path: '/unsubscribe', element: <Unsubscribe /> },
```

- [ ] **Step 4: Build to verify**

Run (from `DCF.Web/`): `npm run build`
Expected: Build completed with no errors.

- [ ] **Step 5: Manual smoke test**

With the dev stack running (`docker compose up db mqtt` + `dotnet run` + `npm run dev`):

1. Trigger any email (e.g., join a league as a non-commissioner to send the member-joined notification to the commissioner)
2. Open Mailpit at `http://localhost:8025`
3. Verify the email renders with the dark card layout, purple header, CTA button, and Unsubscribe link
4. Click the Unsubscribe link — should land on `/unsubscribe` with a confirmation message
5. Verify `EmailNotificationsEnabled` is now `false` for that user (check DB or call `/api/auth/me`)

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/api/client.ts DCF.Web/src/pages/Unsubscribe.tsx DCF.Web/src/main.tsx
git commit -m "feat: add React unsubscribe page at /unsubscribe"
```
