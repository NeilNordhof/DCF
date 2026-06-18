# Draft Timezone Email Formatting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture the commissioner's browser timezone when scheduling a draft, store it on the league, and use it to format human-readable times in draft notification emails.

**Architecture:** `DraftTimezone` (nullable string) is added to `LeagueEntity` and both request models. A new `DraftTimeFormatter` static helper converts UTC times to localized strings using `TimeZoneConverter`. `LeagueService` and `DraftSchedulerService` use the formatter when building email bodies. The frontend sends `Intl.DateTimeFormat().resolvedOptions().timeZone` with every schedule/update call.

**Tech Stack:** .NET 10 / EF Core / `TimeZoneConverter` NuGet, React 19 / TypeScript

## Global Constraints

- C#: curly brackets always on new line; no lambdas for methods; wrap one-line blocks with braces; 1 blank line before return; 1 blank line before/after code blocks and awaits; never more than 1 blank line in a row
- TS: `const` by default; 1 blank line before return; 1 blank line before/after blocks and awaits; never more than 1 blank line in a row
- `DraftTimezone` is nullable — null means fall back to UTC display (existing leagues)
- Learning mode: Task 5 (TypeScript) is pair-programming — user writes, Claude reviews
- All C# tasks Claude handles directly

---

### Task 1: Setup — NuGet, entity, migration, request models

**Files:**
- Modify: `DCF.Api/DCF.Api.csproj`
- Modify: `DCF.Data/Entities/LeagueEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddLeagueDraftTimezone.cs` (generated)
- Modify: `DCF.Api/Models/LeagueRequests.cs`

**Interfaces:**
- Produces: `LeagueEntity.DraftTimezone: string?` consumed by Tasks 3, 4
- Produces: `CreateLeagueRequest.DraftTimezone: string?` and `UpdateLeagueRequest.DraftTimezone: string?` consumed by Task 4

- [ ] **Step 1: Add `TimeZoneConverter` NuGet to `DCF.Api.csproj`**

Add inside the existing `<ItemGroup>` with other `<PackageReference>` entries:

```xml
<PackageReference Include="TimeZoneConverter" Version="6.1.0" />
```

- [ ] **Step 2: Add `DraftTimezone` to `LeagueEntity`**

In `DCF.Data/Entities/LeagueEntity.cs`, add after `DraftStartTime`:

```csharp
public string? DraftTimezone { get; set; }
```

- [ ] **Step 3: Add `DraftTimezone` to both request records**

Replace the entire `DCF.Api/Models/LeagueRequests.cs`:

```csharp
using DCF.Data.Models;

namespace DCF.Api.Models;

public record CreateLeagueRequest(
    string Name,
    bool IsPublic,
    int CorpsPerCaption,
    int MaxPlayers,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime,
    string? DraftTimezone);

public record JoinLeagueRequest(string? InviteCode);

public record UpdateLeagueRequest(
    int CorpsPerCaption,
    ComputedCaption[] DraftableCaptions,
    DateTimeOffset? DraftStartTime,
    string? DraftTimezone);

public record SubmitPickRequest(Guid CorpsId, ComputedCaption Caption);
```

- [ ] **Step 4: Generate the EF Core migration**

```
dotnet ef migrations add AddLeagueDraftTimezone --project DCF.Data --startup-project DCF.Api
```

Expected: a new migration file created under `DCF.Data/Migrations/` with `AddColumn<string>` for `DraftTimezone` on `Leagues`, nullable.

- [ ] **Step 5: Verify the build**

```
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 6: Commit**

```
git add DCF.Api/DCF.Api.csproj DCF.Data/Entities/LeagueEntity.cs DCF.Api/Models/LeagueRequests.cs DCF.Data/Migrations/
git commit -m "feat: add DraftTimezone to LeagueEntity, request models, and EF migration"
```

---

### Task 2: DraftTimeFormatter (TDD)

**Files:**
- Create: `DCF.Api/Services/DraftTimeFormatter.cs`
- Create: `DCF.Tests/Services/DraftTimeFormatterTests.cs`

**Interfaces:**
- Produces: `DraftTimeFormatter.Format(DateTimeOffset utcTime, string? ianaTimezone): string` — consumed by Tasks 3 and 4

- [ ] **Step 1: Write failing tests**

Create `DCF.Tests/Services/DraftTimeFormatterTests.cs`:

```csharp
using DCF.Api.Services;
using Xunit;

namespace DCF.Tests.Services;

public class DraftTimeFormatterTests
{
    [Fact]
    public void Format_NullTimezone_ReturnsUtcString()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, null);

        Assert.Equal("Monday, June 16 at 11:00 PM UTC", result);
    }

    [Fact]
    public void Format_EmptyTimezone_ReturnsUtcString()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "");

        Assert.Equal("Monday, June 16 at 11:00 PM UTC", result);
    }

    [Fact]
    public void Format_EasternInSummer_ReturnsDaylightAbbreviation()
    {
        // 2026-06-16 23:00 UTC = 2026-06-16 19:00 EDT (UTC-4, DST active)
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "America/New_York");

        Assert.Equal("Monday, June 16 at 7:00 PM EDT", result);
    }

    [Fact]
    public void Format_EasternInWinter_ReturnsStandardAbbreviation()
    {
        // 2026-01-16 23:00 UTC = 2026-01-16 18:00 EST (UTC-5, no DST)
        var utcTime = new DateTimeOffset(2026, 1, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "America/New_York");

        Assert.Equal("Friday, January 16 at 6:00 PM EST", result);
    }

    [Fact]
    public void Format_InvalidTimezone_ReturnsUtcFallback()
    {
        var utcTime = new DateTimeOffset(2026, 6, 16, 23, 0, 0, TimeSpan.Zero);

        var result = DraftTimeFormatter.Format(utcTime, "Not/A/Real/Zone");

        Assert.Equal("Monday, June 16 at 11:00 PM UTC", result);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftTimeFormatterTests"
```

Expected: build error — `DraftTimeFormatter` does not exist.

- [ ] **Step 3: Implement `DraftTimeFormatter`**

Create `DCF.Api/Services/DraftTimeFormatter.cs`:

```csharp
using TimeZoneConverter;

namespace DCF.Api.Services;

public static class DraftTimeFormatter
{
    public static string Format(DateTimeOffset utcTime, string? ianaTimezone)
    {
        if (string.IsNullOrEmpty(ianaTimezone))
        {

            return utcTime.ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");
        }

        try
        {
            var tz = TZConvert.GetTimeZoneInfo(ianaTimezone);
            var localTime = TimeZoneInfo.ConvertTime(utcTime, tz);
            var formatted = localTime.ToString("dddd, MMMM d 'at' h:mm tt");
            var abbr = GetAbbreviation(tz, utcTime);

            return $"{formatted} {abbr}";
        }
        catch
        {

            return utcTime.ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");
        }
    }

    private static string GetAbbreviation(TimeZoneInfo tz, DateTimeOffset utcTime)
    {
        var name = tz.IsDaylightSavingTime(utcTime) ? tz.DaylightName : tz.StandardName;

        return string.Concat(name.Split(' ').Select(w => w[0]));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~DraftTimeFormatterTests"
```

Expected: 5 tests pass.

- [ ] **Step 5: Run full suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```
git add DCF.Api/Services/DraftTimeFormatter.cs DCF.Tests/Services/DraftTimeFormatterTests.cs
git commit -m "feat: add DraftTimeFormatter with IANA timezone support and UTC fallback"
```

---

### Task 3: EmailTemplate changes

**Files:**
- Modify: `DCF.Api/Services/EmailTemplate.cs`
- Modify: `DCF.Tests/Services/EmailTemplateTests.cs`

**Interfaces:**
- Consumes: nothing new — pure string changes
- Produces: `EmailTemplate.DraftTomorrow(leagueName, timeStr, leagueId, frontendUrl, token)` — consumed by Task 4
- Produces: `EmailTemplate.DraftInOneHour(leagueName, timeStr, leagueId, frontendUrl, token)` — consumed by Task 4

- [ ] **Step 1: Update `DraftTomorrow` and `DraftInOneHour` signatures and bodies**

In `DCF.Api/Services/EmailTemplate.cs`, replace the `DraftTomorrow` method:

```csharp
public static (string subject, string html) DraftTomorrow(
    string leagueName,
    string timeStr,
    Guid leagueId,
    string frontendUrl,
    string unsubscribeToken)
{
    var safe = WebUtility.HtmlEncode(leagueName);
    var safeTime = WebUtility.HtmlEncode(timeStr);

    return (
        $"Draft tomorrow — {leagueName}",
        Layout(
            heading: $"Draft tomorrow — {safe}",
            body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft is tomorrow at <strong style=\"color: #f3f4f6;\">{safeTime}</strong>! Make sure you're ready to pick.",
            ctaText: "Go to Draft Room",
            ctaUrl: $"{frontendUrl}/leagues/{leagueId}/draft",
            unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
}
```

Replace the `DraftInOneHour` method:

```csharp
public static (string subject, string html) DraftInOneHour(
    string leagueName,
    string timeStr,
    Guid leagueId,
    string frontendUrl,
    string unsubscribeToken)
{
    var safe = WebUtility.HtmlEncode(leagueName);
    var safeTime = WebUtility.HtmlEncode(timeStr);

    return (
        $"Draft in 1 hour — {leagueName}",
        Layout(
            heading: $"Draft in 1 hour — {safe}",
            body: $"The <strong style=\"color: #f3f4f6;\">{safe}</strong> draft starts at <strong style=\"color: #f3f4f6;\">{safeTime}</strong> — that's in 1 hour!",
            ctaText: "Go to Draft Room",
            ctaUrl: $"{frontendUrl}/leagues/{leagueId}/draft",
            unsubscribeUrl: $"{frontendUrl}/unsubscribe?token={unsubscribeToken}"));
}
```

- [ ] **Step 2: Center the CTA button in the `Layout` method**

In the private `Layout` method, find the CTA table block (around line 174) and replace:

```csharp
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
```

With:

```csharp
                          <table width="100%" cellpadding="0" cellspacing="0" role="presentation">
                            <tr>
                              <td align="center">
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
                          </table>
```

- [ ] **Step 3: Update `EmailTemplateTests.cs` to pass `timeStr` to the changed methods**

In `DCF.Tests/Services/EmailTemplateTests.cs`, update the two affected tests:

```csharp
[Fact]
public void DraftTomorrow_SubjectAndHtmlContainLeagueName()
{
    var (subject, html) = EmailTemplate.DraftTomorrow(
        "Test League", "Monday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

    Assert.Equal("Draft tomorrow — Test League", subject);
    Assert.Contains("Test League", html);
    Assert.Contains("Monday, June 16 at 7:00 PM EDT", html);
    Assert.Contains($"/leagues/{TestLeagueId}/draft", html);
    Assert.Contains($"/unsubscribe?token={Token}", html);
    Assert.Contains("Go to Draft Room", html);
}

[Fact]
public void DraftInOneHour_SubjectAndHtmlContainLeagueName()
{
    var (subject, html) = EmailTemplate.DraftInOneHour(
        "Test League", "Monday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

    Assert.Equal("Draft in 1 hour — Test League", subject);
    Assert.Contains("Test League", html);
    Assert.Contains("Monday, June 16 at 7:00 PM EDT", html);
    Assert.Contains("Go to Draft Room", html);
}
```

Also update the HTML encoding test (it calls `DraftTomorrow`):

```csharp
[Fact]
public void EmailTemplate_HtmlEncodesUserContent()
{
    var (_, html) = EmailTemplate.DraftTomorrow(
        "<script>alert(1)</script>", "Monday, June 16 at 7:00 PM EDT", TestLeagueId, FrontendUrl, Token);

    Assert.DoesNotContain("<script>", html);
    Assert.Contains("&lt;script&gt;", html);
}
```

- [ ] **Step 4: Run tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~EmailTemplateTests"
```

Expected: all 8 tests pass.

- [ ] **Step 5: Commit**

```
git add DCF.Api/Services/EmailTemplate.cs DCF.Tests/Services/EmailTemplateTests.cs
git commit -m "feat: add timeStr to DraftTomorrow and DraftInOneHour templates, center CTA button"
```

---

### Task 4: Service wiring

**Files:**
- Modify: `DCF.Api/Services/ILeagueService.cs`
- Modify: `DCF.Api/Services/LeagueService.cs`
- Modify: `DCF.Api/Controllers/LeaguesController.cs`
- Modify: `DCF.Api/Services/DraftSchedulerService.cs`

**Interfaces:**
- Consumes: `DraftTimeFormatter.Format(DateTimeOffset, string?): string` from Task 2
- Consumes: `EmailTemplate.DraftTomorrow(leagueName, timeStr, leagueId, frontendUrl, token)` from Task 3
- Consumes: `EmailTemplate.DraftInOneHour(leagueName, timeStr, leagueId, frontendUrl, token)` from Task 3
- Consumes: `LeagueEntity.DraftTimezone: string?` from Task 1
- Consumes: `CreateLeagueRequest.DraftTimezone: string?` and `UpdateLeagueRequest.DraftTimezone: string?` from Task 1

- [ ] **Step 1: Update `ILeagueService.CreateAsync` signature**

In `DCF.Api/Services/ILeagueService.cs`, update the `CreateAsync` line:

```csharp
Task<LeagueEntity> CreateAsync(string name, bool isPublic, int corpsPerCaption, int maxPlayers, List<ComputedCaption> captions, string userSub, DateTimeOffset? draftStartTime = null, string? draftTimezone = null);
```

- [ ] **Step 2: Update `LeagueService.CreateAsync`**

In `DCF.Api/Services/LeagueService.cs`, update the `CreateAsync` signature:

```csharp
public async Task<LeagueEntity> CreateAsync(
    string name,
    bool isPublic,
    int corpsPerCaption,
    int maxPlayers,
    List<ComputedCaption> captions,
    string userSub,
    DateTimeOffset? draftStartTime = null,
    string? draftTimezone = null)
```

In the `new LeagueEntity { ... }` initialiser, add after `DraftStartTime`:

```csharp
DraftTimezone = draftTimezone
```

- [ ] **Step 3: Update `LeagueService.UpdateAsync`**

In `DCF.Api/Services/LeagueService.cs`, in the `UpdateAsync` method, add `league.DraftTimezone = req.DraftTimezone;` alongside the other league property assignments (after `league.IssueMessages = [];`):

```csharp
league.CorpsPerCaption = req.CorpsPerCaption;
league.DraftableCaptions = req.DraftableCaptions;
league.IssueMessages = [];
league.DraftTimezone = req.DraftTimezone;
```

Then replace the hardcoded `timeStr` line:

```csharp
// Before
var timeStr = req.DraftStartTime.Value.ToUniversalTime().ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");

// After
var timeStr = DraftTimeFormatter.Format(req.DraftStartTime.Value.ToUniversalTime(), league.DraftTimezone);
```

- [ ] **Step 4: Update `LeaguesController.cs` to pass `DraftTimezone` to `CreateAsync`**

In `DCF.Api/Controllers/LeaguesController.cs`, find the `CreateAsync` call (around line 61) and add `req.DraftTimezone`:

```csharp
var league = await leagueService.CreateAsync(
    req.Name, req.IsPublic, req.CorpsPerCaption, req.MaxPlayers,
    req.DraftableCaptions.ToList(), userSub, req.DraftStartTime, req.DraftTimezone);
```

- [ ] **Step 5: Update `DraftSchedulerService` — change `NotifyLeagueMembersAsync` signature and callers**

In `DCF.Api/Services/DraftSchedulerService.cs`, change the `NotifyLeagueMembersAsync` method signature from:

```csharp
private async Task NotifyLeagueMembersAsync(
    Guid leagueId,
    Func<string, string, (string subject, string html)> templateFactory)
```

To:

```csharp
private async Task NotifyLeagueMembersAsync(
    Guid leagueId,
    Func<string, string, string, (string subject, string html)> templateFactory)
```

Inside `NotifyLeagueMembersAsync`, after the league is fetched, add `timeStr` computation before the member loop:

```csharp
if (league is null)
{
    return;
}

var timeStr = league.DraftStartTime.HasValue
    ? DraftTimeFormatter.Format(league.DraftStartTime.Value, league.DraftTimezone)
    : string.Empty;
```

Change the `templateFactory` call from:
```csharp
var (subject, html) = templateFactory(league.Name, token);
```
To:
```csharp
var (subject, html) = templateFactory(league.Name, timeStr, token);
```

- [ ] **Step 6: Update the three `NotifyLeagueMembersAsync` call sites in `ScheduleNext`**

Find the three calls in `ScheduleNext` and update their lambdas:

**24h notification** (first call):
```csharp
await NotifyLeagueMembersAsync(leagueId,
    (leagueName, timeStr, token) => EmailTemplate.DraftTomorrow(leagueName, timeStr, leagueId, frontendUrl, token));
```

**1h notification** (second call):
```csharp
await NotifyLeagueMembersAsync(leagueId,
    (leagueName, timeStr, token) => EmailTemplate.DraftInOneHour(leagueName, timeStr, leagueId, frontendUrl, token));
```

**Room open notification** (third call — `timeStr` is not used by this template):
```csharp
await NotifyLeagueMembersAsync(leagueId,
    (leagueName, _, token) => EmailTemplate.DraftRoomOpen(leagueName, (int)OpenLeadTime.TotalMinutes, leagueId, frontendUrl, token));
```

- [ ] **Step 7: Run full test suite**

```
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```
git add DCF.Api/Services/ILeagueService.cs DCF.Api/Services/LeagueService.cs DCF.Api/Controllers/LeaguesController.cs DCF.Api/Services/DraftSchedulerService.cs
git commit -m "feat: wire DraftTimezone through LeagueService and DraftSchedulerService for localised email times"
```

---

### Task 5: Frontend — send browser timezone (pair programming)

> **Learning mode:** You write these changes; Claude will review.

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`
- Modify: `DCF.Web/src/pages/LeagueDetail.tsx`

**Interfaces:**
- Consumes: `PATCH /api/leagues/:id` and `POST /api/leagues` from Task 4 (now accept `draftTimezone`)
- Produces: nothing — frontend-only change

- [ ] **Step 1: Add `draftTimezone` to the TypeScript request types**

In `DCF.Web/src/types/api.ts`, update both request interfaces:

```ts
export interface CreateLeagueRequest {
  name: string;
  isPublic: boolean;
  corpsPerCaption: number;
  maxPlayers: number;
  draftableCaptions: ComputedCaption[];
  draftStartTime?: string | null;
  draftTimezone?: string;
}

export interface UpdateLeagueRequest {
  corpsPerCaption: number;
  draftableCaptions: ComputedCaption[];
  draftStartTime: string | null;
  draftTimezone?: string;
}
```

- [ ] **Step 2: Send timezone in `LeagueCreate.tsx`**

In `DCF.Web/src/pages/LeagueCreate.tsx`, find the `api.createLeague({ ... })` call and add:

```ts
draftTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
```

- [ ] **Step 3: Send timezone in `LeagueDetail.tsx`**

In `DCF.Web/src/pages/LeagueDetail.tsx`, find the `api.updateLeague(id!, { ... })` call and add:

```ts
draftTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
```

- [ ] **Step 4: Verify TypeScript compiles**

```
cd DCF.Web && npm run build
```

Expected: no type errors.

- [ ] **Step 5: Commit**

```
git add DCF.Web/src/types/api.ts DCF.Web/src/pages/LeagueCreate.tsx DCF.Web/src/pages/LeagueDetail.tsx
git commit -m "feat: send browser timezone with draft schedule requests"
```
