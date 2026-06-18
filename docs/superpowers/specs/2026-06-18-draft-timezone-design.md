---
name: draft-timezone-email-formatting
description: Store commissioner's timezone with draft schedule and use it to format times in draft notification emails (Issue #36)
metadata:
  type: project
---

# Draft Timezone Email Formatting

**Issue:** #36 | **Milestone:** Beta Release

## Overview

Draft notification emails currently show times in UTC, which is hard to read. When the commissioner schedules a draft, the frontend captures their browser timezone (`Intl.DateTimeFormat().resolvedOptions().timeZone`) and sends it alongside the `draftStartTime`. It's stored on the league and used to format human-readable times in all draft-related emails.

---

## Data Layer

### `LeagueEntity` (`DCF.Data/Entities/LeagueEntity.cs`)

Add:
```csharp
public string? DraftTimezone { get; set; }
```

Nullable — existing leagues won't have a timezone set. The null case falls back to UTC display.

### EF Core migration

`AddColumn<string>` on the `Leagues` table, nullable, no default value. Generated via `dotnet ef migrations add AddLeagueDraftTimezone`.

### `CreateLeagueRequest` / `UpdateLeagueRequest` (`DCF.Api/Models/LeagueRequests.cs`)

Both records gain `string? DraftTimezone`. `LeagueService.CreateAsync` and `UpdateAsync` assign it to the league entity when saving.

---

## Timezone Formatting Helper

A new static class `DraftTimeFormatter` in `DCF.Api/Services/DraftTimeFormatter.cs`.

**Method signature:**
```csharp
public static string Format(DateTimeOffset utcTime, string? ianaTimezone)
```

**Logic:**
1. If `ianaTimezone` is null or empty → fall back to `utcTime.ToString("dddd, MMMM d 'at' h:mm tt 'UTC'")`
2. Call `TZConvert.GetTimeZoneInfo(ianaTimezone)` (from `TimeZoneConverter` NuGet) to get a `TimeZoneInfo`
3. Convert: `TimeZoneInfo.ConvertTime(utcTime, tz)` → local `DateTimeOffset`
4. Format date: `localTime.ToString("dddd, MMMM d 'at' h:mm tt")`
5. Get abbreviation: check `tz.IsDaylightSavingTime(utcTime)` → take first letter of each word in `tz.DaylightName` or `tz.StandardName` (e.g., "Eastern Daylight Time" → "EDT")
6. Return `"{formattedDate} {abbreviation}"` e.g. `"Monday, June 16 at 7:00 PM EDT"`
7. If `TZConvert.GetTimeZoneInfo` throws (invalid IANA ID) → fall back to UTC format

**Dependencies:** `TimeZoneConverter` NuGet package added to `DCF.Api.csproj`.

---

## Email Template Changes (`DCF.Api/Services/EmailTemplate.cs`)

### `DraftTomorrow`
Add `string timeStr` parameter. Updated body:
> "The **{safe}** draft is tomorrow at **{safeTime}**! Make sure you're ready to pick."

### `DraftInOneHour`
Add `string timeStr` parameter. Updated body:
> "The **{safe}** draft starts at **{safeTime}** — that's in 1 hour!"

### `DraftRoomOpen`
Unchanged (no time string needed).

### CTA button alignment
All templates: center the CTA button by adding `align="center"` to the wrapping `<td>` around the CTA `<table>`. Currently left-aligned.

---

## `LeagueService` Changes (`DCF.Api/Services/LeagueService.cs`)

### `UpdateAsync`

Replace the hardcoded UTC `timeStr`:
```csharp
// Before
var timeStr = req.DraftStartTime.Value.ToUniversalTime().ToString("dddd, MMMM d 'at' h:mm tt 'UTC'");

// After
league.DraftTimezone = req.DraftTimezone;
var timeStr = DraftTimeFormatter.Format(req.DraftStartTime.Value.ToUniversalTime(), league.DraftTimezone);
```

### `CreateAsync`

Add `string? draftTimezone = null` to the method signature (after `draftStartTime`). Assign `league.DraftTimezone = draftTimezone` when building the `LeagueEntity`.

---

## `DraftSchedulerService` Changes (`DCF.Api/Services/DraftSchedulerService.cs`)

`NotifyLeagueMembersAsync` already fetches the `league` entity. Update the two notification calls that need `timeStr`:

```csharp
var timeStr = league.DraftStartTime.HasValue
    ? DraftTimeFormatter.Format(league.DraftStartTime.Value, league.DraftTimezone)
    : string.Empty;
```

Pass `timeStr` into the updated `DraftTomorrow` and `DraftInOneHour` template calls. `DraftRoomOpen` is unchanged.

---

## Frontend Changes

### `DCF.Web/src/types/api.ts`

Add `draftTimezone?: string` to `CreateLeagueRequest` and `UpdateLeagueRequest` interfaces.

### `DCF.Web/src/api/client.ts`

Both `createLeague` and `updateLeague` methods already accept the request objects — no signature changes needed beyond the type update above.

### `DCF.Web/src/pages/LeagueCreate.tsx`

Add `draftTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone` to the `createLeague` call body.

### `DCF.Web/src/pages/LeagueDetail.tsx`

Add `draftTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone` to the `updateLeague` call body.

---

## Constraints

- `DraftTimezone` nullable — null means UTC fallback, preserving behaviour for existing leagues
- `TimeZoneConverter` handles IANA → Windows timezone mapping (required for Windows dev environment)
- `DraftTimeFormatter` is a pure static method — no DI, testable in isolation
- `DraftRoomOpen` template is not changed
- Frontend always sends the browser timezone (it's cheap and harmless to send even when no draft time is set)
- Learning mode applies to TypeScript changes
