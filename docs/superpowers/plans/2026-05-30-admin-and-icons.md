# Admin Updates + Corps Icons — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add nav admin link, corps rename/delete, corps icon upload, season date editing, show start-time + timezone overhaul, expandable show cards, collapsible panels, a publish warning, a LeagueCreate season guard, and corps icons in the draft board and scores tab.

**Architecture:** Backend gains PATCH/DELETE endpoints for corps, seasons, and shows; `ShowEntity` gets a nullable `StartTime` column; `CorpsEntity` gets a nullable `IconPath` column; the API serves uploaded icons as static files at `/uploads/`; `CorpsSummary` and `PickScore` gain `IconUrl` (root-relative path). A shared `CorpsIcon` React component handles image-or-initials rendering across admin, draft board, and scores tab. Timezone offsets for DCI shows (always summer DST) are hardcoded: PT=−07, MT=−06, CT=−05, ET=−04.

**Tech Stack:** ASP.NET Core 10, EF Core / Npgsql, xUnit (InMemory), React 19, TypeScript, Vite

**Supersedes:** `docs/superpowers/plans/2026-05-29-admin-updates.md` and `docs/superpowers/plans/2026-05-30-corps-icons.md`

---

## File Map

**Backend:**
- Modify: `DCF.Data/Entities/ShowEntity.cs` — add `StartTime: DateTimeOffset?`
- Modify: `DCF.Data/Entities/CorpsEntity.cs` — add `IconPath: string?`
- Create: migrations (two, generated)
- Modify: `DCF.Api/Models/AdminRequests.cs` — add `RenameCorpsRequest`, `UpdateSeasonDatesRequest`; update show request records
- Modify: `DCF.Api/Services/IAdminService.cs` — new method signatures
- Modify: `DCF.Api/Services/AdminService.cs` — `CorpsSummary` record extended; new method implementations; `ShowSummary` extended
- Modify: `DCF.Api/Services/StandingsService.cs` — `PickScore` extended with `IconUrl`
- Modify: `DCF.Api/Controllers/AdminController.cs` — new endpoints; `IWebHostEnvironment` injection
- Modify: `DCF.Api/Program.cs` — static file serving; uploads directory
- Modify: `DCF.Tests/Services/AdminServiceTests.cs` — new test methods

**Frontend:**
- Modify: `DCF.Web/src/types/api.ts` — `Show.startTime`, `Corps.iconUrl`, `PickScore.iconUrl`
- Modify: `DCF.Web/src/api/client.ts` — all new admin methods + icon upload
- Create: `DCF.Web/src/components/CorpsIcon.tsx` — shared icon/initials component
- Modify: `DCF.Web/src/components/Nav.tsx` — admin link
- Modify: `DCF.Web/src/pages/Admin.tsx` — collapsible seasons panel; corps rename/delete + icon upload (merged)
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx` — editable dates; publish warning; redesigned show form; expandable show cards
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx` — no-active-season guard
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` — remove name column; icon cells
- Modify: `DCF.Web/src/components/LeagueScoresTab.tsx` — icon instead of corps name

---

### Task 1: Nav admin link

**Files:**
- Modify: `DCF.Web/src/components/Nav.tsx`

- [ ] **Step 1: Add admin link**

In `Nav.tsx`, inside the links `<div>` (the one containing LEAGUES and PROFILE links), add an ADMIN link before LEAGUES, visible only to admins:

```tsx
{user?.isAdmin && (
  <Link to="/admin" style={linkStyle('/admin')}>ADMIN</Link>
)}
```

- [ ] **Step 2: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/components/Nav.tsx
git commit -m "feat: show admin nav link for admin users"
```

---

### Task 2: Backend — corps rename and delete

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add request model**

In `DCF.Api/Models/AdminRequests.cs`, add:

```csharp
public record RenameCorpsRequest(string Name);
```

- [ ] **Step 2: Add interface methods**

In `DCF.Api/Services/IAdminService.cs`, add:

```csharp
Task<CorpsSummary?> RenameCorpsAsync(Guid id, string name);
Task<(bool Found, bool Deletable)> DeleteCorpsAsync(Guid id);
```

- [ ] **Step 3: Write failing tests**

Add to `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
[Fact]
public async Task RenameCorps_UpdatesName()
{
    using var db = CreateDb("corps_rename");
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Old Name" };
    db.Corps.Add(corps);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var result = await svc.RenameCorpsAsync(corps.Id, "New Name");

    Assert.NotNull(result);
    Assert.Equal("New Name", result!.Name);
    Assert.Equal("New Name", db.Corps.Single(c => c.Id == corps.Id).Name);
}

[Fact]
public async Task RenameCorps_MissingId_ReturnsNull()
{
    using var db = CreateDb("corps_rename_missing");
    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

    var result = await svc.RenameCorpsAsync(Guid.NewGuid(), "Anything");

    Assert.Null(result);
}

[Fact]
public async Task DeleteCorps_NotInPublishedSeason_DeletesAndReturnsTrue()
{
    using var db = CreateDb("corps_delete_ok");
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
    db.Corps.Add(corps);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var (found, deletable) = await svc.DeleteCorpsAsync(corps.Id);

    Assert.True(found);
    Assert.True(deletable);
    Assert.False(db.Corps.Any(c => c.Id == corps.Id));
}

[Fact]
public async Task DeleteCorps_InPublishedSeason_ReturnsDeletableFalse()
{
    using var db = CreateDb("corps_delete_blocked");
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
        IsPublished = true
    };
    db.Corps.Add(corps);
    db.Seasons.Add(season);
    db.SeasonCorps.Add(new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps.Id });
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var (found, deletable) = await svc.DeleteCorpsAsync(corps.Id);

    Assert.True(found);
    Assert.False(deletable);
    Assert.True(db.Corps.Any(c => c.Id == corps.Id));
}
```

- [ ] **Step 4: Run tests — verify fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "RenameCorps|DeleteCorps" -v n
```

Expected: FAIL (methods not implemented).

- [ ] **Step 5: Implement in AdminService.cs**

Add these methods to `AdminService`:

```csharp
public async Task<CorpsSummary?> RenameCorpsAsync(Guid id, string name)
{
    var corps = await db.Corps.FindAsync(id);

    if (corps is null)
    {
        return null;
    }

    corps.Name = name;

    await db.SaveChangesAsync();

    return new CorpsSummary(corps.Id, corps.Name);
}

public async Task<(bool Found, bool Deletable)> DeleteCorpsAsync(Guid id)
{
    var corps = await db.Corps.FindAsync(id);

    if (corps is null)
    {
        return (false, false);
    }

    var seasonIds = await db.SeasonCorps
        .Where(sc => sc.CorpsId == id)
        .Select(sc => sc.SeasonId)
        .ToListAsync();

    var inPublishedSeason = await db.Seasons
        .AnyAsync(s => seasonIds.Contains(s.Id) && s.IsPublished);

    if (inPublishedSeason)
    {
        return (true, false);
    }

    db.Corps.Remove(corps);

    await db.SaveChangesAsync();

    return (true, true);
}
```

- [ ] **Step 6: Run tests — verify pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "RenameCorps|DeleteCorps" -v n
```

Expected: PASS.

- [ ] **Step 7: Add controller endpoints**

In `AdminController.cs`, add after the existing `CreateCorps` action:

```csharp
[HttpPatch("corps/{id}")]
public async Task<IActionResult> RenameCorps(Guid id, RenameCorpsRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    var result = await adminService.RenameCorpsAsync(id, req.Name);

    if (result is null)
    {
        return NotFound();
    }

    return Ok(result);
}

[HttpDelete("corps/{id}")]
public async Task<IActionResult> DeleteCorps(Guid id)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    var (found, deletable) = await adminService.DeleteCorpsAsync(id);

    if (!found)
    {
        return NotFound();
    }

    if (!deletable)
    {
        return Conflict(new { error = "Corps belongs to a published season and cannot be deleted." });
    }

    return NoContent();
}
```

- [ ] **Step 8: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: corps rename and delete endpoints"
```

---

### Task 3: Backend — CorpsEntity.IconPath + CorpsSummary.IconUrl + SetCorpsIconAsync

**Files:**
- Modify: `DCF.Data/Entities/CorpsEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddCorpsIconPath.cs` (generated)
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add IconPath to CorpsEntity**

Replace `DCF.Data/Entities/CorpsEntity.cs`:

```csharp
namespace DCF.Data.Entities;

public class CorpsEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconPath { get; set; }

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<SeasonCorpsEntity> SeasonCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
}
```

- [ ] **Step 2: Create and apply migration**

```bash
dotnet ef migrations add AddCorpsIconPath --project DCF.Data --startup-project DCF.Api
dotnet ef database update --project DCF.Data --startup-project DCF.Api
```

Expected: migration file created, database updated.

- [ ] **Step 3: Extend CorpsSummary record to include IconUrl**

In `DCF.Api/Services/AdminService.cs`, change the `CorpsSummary` record at the top of the file:

```csharp
public record CorpsSummary(Guid Id, string Name, string? IconUrl);
```

- [ ] **Step 4: Update GetCorpsAsync**

Replace `GetCorpsAsync` in `AdminService.cs`:

```csharp
public async Task<IReadOnlyList<CorpsSummary>> GetCorpsAsync()
{
    var corps = await db.Corps
        .OrderBy(c => c.Name)
        .Select(c => new { c.Id, c.Name, c.IconPath })
        .ToListAsync();

    return corps
        .Select(c => new CorpsSummary(c.Id, c.Name, c.IconPath != null ? $"/uploads/{c.IconPath}" : null))
        .ToList();
}
```

- [ ] **Step 5: Update CreateCorpsAsync**

Replace `CreateCorpsAsync` in `AdminService.cs`:

```csharp
public async Task<CorpsSummary> CreateCorpsAsync(string name)
{
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = name };
    db.Corps.Add(corps);

    await db.SaveChangesAsync();

    return new CorpsSummary(corps.Id, corps.Name, null);
}
```

- [ ] **Step 6: Update RenameCorpsAsync to return 3-param CorpsSummary**

In `AdminService.cs`, update the return statement in `RenameCorpsAsync` (added in Task 2):

```csharp
return new CorpsSummary(corps.Id, corps.Name, corps.IconPath != null ? $"/uploads/{corps.IconPath}" : null);
```

- [ ] **Step 7: Write failing tests for SetCorpsIconAsync**

Add to `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
[Fact]
public async Task SetCorpsIconAsync_ExistingCorps_UpdatesPathAndReturnsOldPath()
{
    using var db = CreateDb("corps_icon_update");
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils", IconPath = "corps-icons/old.png" };
    db.Corps.Add(corps);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var (found, oldPath) = await svc.SetCorpsIconAsync(corps.Id, "corps-icons/new.jpg");

    Assert.True(found);
    Assert.Equal("corps-icons/old.png", oldPath);
    Assert.Equal("corps-icons/new.jpg", db.Corps.Single(c => c.Id == corps.Id).IconPath);
}

[Fact]
public async Task SetCorpsIconAsync_NoExistingIcon_ReturnsNullOldPath()
{
    using var db = CreateDb("corps_icon_no_existing");
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Cavaliers" };
    db.Corps.Add(corps);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var (found, oldPath) = await svc.SetCorpsIconAsync(corps.Id, "corps-icons/cav.png");

    Assert.True(found);
    Assert.Null(oldPath);
    Assert.Equal("corps-icons/cav.png", db.Corps.Single(c => c.Id == corps.Id).IconPath);
}

[Fact]
public async Task SetCorpsIconAsync_MissingCorps_ReturnsFalse()
{
    using var db = CreateDb("corps_icon_missing");
    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

    var (found, oldPath) = await svc.SetCorpsIconAsync(Guid.NewGuid(), "corps-icons/x.png");

    Assert.False(found);
    Assert.Null(oldPath);
}
```

- [ ] **Step 8: Run tests — verify fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetCorpsIconAsync" -v n
```

Expected: FAIL.

- [ ] **Step 9: Add SetCorpsIconAsync to interface**

In `DCF.Api/Services/IAdminService.cs`, add:

```csharp
Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath);
```

- [ ] **Step 10: Implement SetCorpsIconAsync in AdminService**

Add to `AdminService.cs`:

```csharp
public async Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath)
{
    var corps = await db.Corps.FindAsync(id);

    if (corps is null)
    {
        return (false, null);
    }

    var oldPath = corps.IconPath;
    corps.IconPath = iconPath;

    await db.SaveChangesAsync();

    return (true, oldPath);
}
```

- [ ] **Step 11: Run tests — verify pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetCorpsIconAsync" -v n
```

Expected: PASS (3 tests).

- [ ] **Step 12: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 13: Commit**

```bash
git add DCF.Data/Entities/CorpsEntity.cs DCF.Data/Migrations/ DCF.Api/Services/AdminService.cs DCF.Api/Services/IAdminService.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: CorpsEntity.IconPath, CorpsSummary.IconUrl, SetCorpsIconAsync"
```

---

### Task 4: Backend — PickScore.IconUrl in StandingsService

**Files:**
- Modify: `DCF.Api/Services/StandingsService.cs`

- [ ] **Step 1: Extend PickScore record**

In `DCF.Api/Services/StandingsService.cs`, change the `PickScore` record:

```csharp
public record PickScore(string CorpsName, double? Score, string? IconUrl);
```

- [ ] **Step 2: Build corps icon map alongside corps names in both public methods**

In both `GetStandingsAsync` and `GetScoreBreakdownAsync`, replace the single corps dictionary load:

```csharp
var corpsNames = await db.Corps.ToDictionaryAsync(c => c.Id, c => c.Name);
```

With:

```csharp
var corpsList = await db.Corps
    .Select(c => new { c.Id, c.Name, c.IconPath })
    .ToListAsync();
var corpsNames = corpsList.ToDictionary(c => c.Id, c => c.Name);
var corpsIcons = corpsList
    .Where(c => c.IconPath != null)
    .ToDictionary(c => c.Id, c => $"/uploads/{c.IconPath!}");
```

Then update both calls to `ComputeMemberScoreAsync` to pass `corpsIcons`:

```csharp
var (totalScore, captions) = await ComputeMemberScoreAsync(
    leagueId, member.UserId, league, latestByCorps, corpsNames, corpsIcons);
```

- [ ] **Step 3: Update ComputeMemberScoreAsync signature and PickScore construction**

Replace the `ComputeMemberScoreAsync` method:

```csharp
private async Task<(double TotalScore, Dictionary<ComputedCaption, CaptionBreakdown> Captions)>
    ComputeMemberScoreAsync(
        Guid leagueId,
        Guid userId,
        LeagueEntity league,
        Dictionary<Guid, ComputedScoreEntity> latestByCorps,
        Dictionary<Guid, string> corpsNames,
        Dictionary<Guid, string> corpsIcons)
{
    double totalScore = 0;
    var captions = new Dictionary<ComputedCaption, CaptionBreakdown>();

    foreach (var caption in league.DraftableCaptions)
    {
        var picks = await db.DraftPicks
            .Where(p => p.LeagueId == leagueId &&
                        p.UserId == userId &&
                        p.Caption == caption)
            .ToListAsync();

        var pickScores = new List<PickScore>();
        var captionScores = new List<double>();

        foreach (var pick in picks)
        {
            var corpsName = corpsNames.GetValueOrDefault(pick.CorpsId, "Unknown");
            corpsIcons.TryGetValue(pick.CorpsId, out var iconUrl);

            if (latestByCorps.TryGetValue(pick.CorpsId, out var cs))
            {
                var score = GetCaptionValue(cs, caption);
                pickScores.Add(new PickScore(corpsName, score, iconUrl));
                captionScores.Add(score);
            }
            else
            {
                pickScores.Add(new PickScore(corpsName, null, iconUrl));
            }
        }

        var avg = captionScores.Count > 0 ? captionScores.Average() : 0;
        var weight = GetWeight(caption, league.DraftableCaptions);
        var weighted = avg * weight;
        totalScore += weighted;

        captions[caption] = new CaptionBreakdown(weighted, pickScores);
    }

    return (totalScore, captions);
}
```

- [ ] **Step 4: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Services/StandingsService.cs
git commit -m "feat: add IconUrl to PickScore via StandingsService"
```

---

### Task 5: Backend — season date update + show delete

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add request model**

In `AdminRequests.cs`, add:

```csharp
public record UpdateSeasonDatesRequest(DateOnly StartDate, DateOnly EndDate);
```

- [ ] **Step 2: Add interface methods**

In `IAdminService.cs`, add:

```csharp
Task<bool> UpdateSeasonDatesAsync(Guid id, DateOnly startDate, DateOnly endDate);
Task<bool> DeleteShowAsync(Guid id);
```

- [ ] **Step 3: Write failing tests**

Add to `AdminServiceTests.cs`:

```csharp
[Fact]
public async Task UpdateSeasonDates_WhenNotPublished_UpdatesDates()
{
    using var db = CreateDb("season_update_dates");
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
    };
    db.Seasons.Add(season);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var result = await svc.UpdateSeasonDatesAsync(season.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 1));

    Assert.True(result);
    var updated = db.Seasons.Single(s => s.Id == season.Id);
    Assert.Equal(new DateOnly(2026, 6, 15), updated.StartDate);
    Assert.Equal(new DateOnly(2026, 9, 1), updated.EndDate);
}

[Fact]
public async Task UpdateSeasonDates_WhenPublished_ReturnsFalse()
{
    using var db = CreateDb("season_update_dates_published");
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
        IsPublished = true
    };
    db.Seasons.Add(season);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var result = await svc.UpdateSeasonDatesAsync(season.Id, new DateOnly(2026, 6, 15), new DateOnly(2026, 9, 1));

    Assert.False(result);
    Assert.Equal(new DateOnly(2026, 6, 1), db.Seasons.Single(s => s.Id == season.Id).StartDate);
}

[Fact]
public async Task DeleteShow_ExistingShow_DeletesAndReturnsTrue()
{
    using var db = CreateDb("show_delete");
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
    };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Regionals", Url = "https://x",
        Date = new DateOnly(2026, 7, 1),
        ScoresAnnouncedTime = DateTimeOffset.UtcNow.AddDays(30),
        SeasonId = season.Id
    };
    db.Seasons.Add(season);
    db.Shows.Add(show);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var result = await svc.DeleteShowAsync(show.Id);

    Assert.True(result);
    Assert.False(db.Shows.Any(s => s.Id == show.Id));
}
```

- [ ] **Step 4: Run tests — verify fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "UpdateSeasonDates|DeleteShow" -v n
```

Expected: FAIL.

- [ ] **Step 5: Implement in AdminService.cs**

Add to `AdminService.cs`:

```csharp
public async Task<bool> UpdateSeasonDatesAsync(Guid id, DateOnly startDate, DateOnly endDate)
{
    var season = await db.Seasons.FindAsync(id);

    if (season is null || season.IsPublished)
    {
        return false;
    }

    season.StartDate = startDate;
    season.EndDate = endDate;

    await db.SaveChangesAsync();

    seasonStatus.ScheduleSeason(season);

    return true;
}

public async Task<bool> DeleteShowAsync(Guid id)
{
    var show = await db.Shows.FindAsync(id);

    if (show is null)
    {
        return false;
    }

    var showCorps = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
    db.ShowCorps.RemoveRange(showCorps);
    db.Shows.Remove(show);

    await db.SaveChangesAsync();

    return true;
}
```

- [ ] **Step 6: Run tests — verify pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "UpdateSeasonDates|DeleteShow" -v n
```

Expected: PASS.

- [ ] **Step 7: Add controller endpoints**

In `AdminController.cs`:

```csharp
[HttpPatch("seasons/{id}/dates")]
public async Task<IActionResult> UpdateSeasonDates(Guid id, UpdateSeasonDatesRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    return await adminService.UpdateSeasonDatesAsync(id, req.StartDate, req.EndDate)
        ? NoContent()
        : NotFound();
}

[HttpDelete("shows/{id}")]
public async Task<IActionResult> DeleteShow(Guid id)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    return await adminService.DeleteShowAsync(id) ? NoContent() : NotFound();
}
```

- [ ] **Step 8: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: season date update and show delete endpoints"
```

---

### Task 6: Backend — ShowEntity.StartTime + migration + updated show endpoints

**Files:**
- Modify: `DCF.Data/Entities/ShowEntity.cs`
- Create: migration (generated)
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`

- [ ] **Step 1: Add StartTime to ShowEntity**

Replace `DCF.Data/Entities/ShowEntity.cs`:

```csharp
namespace DCF.Data.Entities;

public class ShowEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset ScoresAnnouncedTime { get; set; }
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;

    public List<ShowCorpsEntity> ShowCorps { get; set; } = [];
    public List<ScoreEntity> Scores { get; set; } = [];
}
```

- [ ] **Step 2: Create and apply migration**

```bash
dotnet ef migrations add AddShowStartTime --project DCF.Data --startup-project DCF.Api
dotnet ef database update --project DCF.Data --startup-project DCF.Api
```

Expected: migration created, database updated.

- [ ] **Step 3: Update ShowSummary record**

In `AdminService.cs`, update the `ShowSummary` record:

```csharp
public record ShowSummary(Guid Id, string Name, string Url, DateOnly Date, DateTimeOffset? StartTime, DateTimeOffset ScoresAnnouncedTime, IEnumerable<Guid> CorpsIds);
```

Update the `GetShowsAsync` projection to include `StartTime`:

```csharp
.Select(s => new ShowSummary(s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime,
    s.ShowCorps.Select(sc => sc.CorpsId)))
```

- [ ] **Step 4: Update show request models**

In `AdminRequests.cs`, replace `CreateShowRequest` and `UpdateShowRequest`:

```csharp
public record CreateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);

public record UpdateShowRequest(
    string Name,
    string Url,
    DateOnly Date,
    DateTimeOffset? StartTime,
    DateTimeOffset ScoresAnnouncedTime,
    List<Guid> CorpsIds);
```

- [ ] **Step 5: Update IAdminService show signatures**

In `IAdminService.cs`, replace the two show method signatures:

```csharp
Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url, DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
Task<bool> UpdateShowAsync(Guid id, string name, string url, DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds);
```

- [ ] **Step 6: Update AdminService show methods**

Replace `CreateShowAsync` in `AdminService.cs`:

```csharp
public async Task<ShowBrief> CreateShowAsync(Guid seasonId, string name, string url,
    DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
{
    var season = await db.Seasons.FindAsync(seasonId)
        ?? throw new InvalidOperationException("Season not found.");

    if (date < season.StartDate || date > season.EndDate)
    {
        throw new InvalidOperationException($"Show date must be within the season range ({season.StartDate}–{season.EndDate}).");
    }

    if (date < DateOnly.FromDateTime(DateTime.UtcNow))
    {
        throw new InvalidOperationException("Show date cannot be in the past.");
    }

    var show = new ShowEntity
    {
        Id = Guid.NewGuid(),
        Name = name,
        Url = url,
        Date = date,
        StartTime = startTime,
        ScoresAnnouncedTime = scoresAnnouncedTime,
        SeasonId = seasonId
    };
    db.Shows.Add(show);
    db.ShowCorps.AddRange(corpsIds.Select(cId =>
        new ShowCorpsEntity { ShowId = show.Id, CorpsId = cId }));

    await db.SaveChangesAsync();

    scrapeScheduler.ScheduleScrape(show);

    return new ShowBrief(show.Id, show.Name);
}
```

Replace `UpdateShowAsync` in `AdminService.cs`:

```csharp
public async Task<bool> UpdateShowAsync(Guid id, string name, string url,
    DateOnly date, DateTimeOffset? startTime, DateTimeOffset scoresAnnouncedTime, List<Guid> corpsIds)
{
    var show = await db.Shows.FindAsync(id);

    if (show is null)
    {
        return false;
    }

    if (show.StartTime.HasValue && show.StartTime.Value <= DateTimeOffset.UtcNow)
    {
        return false;
    }

    show.Name = name;
    show.Url = url;
    show.Date = date;
    show.StartTime = startTime;
    show.ScoresAnnouncedTime = scoresAnnouncedTime;

    var existing = await db.ShowCorps.Where(sc => sc.ShowId == id).ToListAsync();
    db.ShowCorps.RemoveRange(existing);
    db.ShowCorps.AddRange(corpsIds.Select(cId =>
        new ShowCorpsEntity { ShowId = id, CorpsId = cId }));

    await db.SaveChangesAsync();

    var updatedShow = await db.Shows.Include(s => s.ShowCorps).FirstAsync(s => s.Id == id);
    scrapeScheduler.ScheduleScrape(updatedShow);

    return true;
}
```

- [ ] **Step 7: Update AdminController show endpoints**

Replace the `CreateShow` and `UpdateShow` actions in `AdminController.cs`:

```csharp
[HttpPost("seasons/{seasonId}/shows")]
public async Task<IActionResult> CreateShow(Guid seasonId, CreateShowRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    try
    {
        var result = await adminService.CreateShowAsync(seasonId, req.Name, req.Url,
            req.Date, req.StartTime, req.ScoresAnnouncedTime, req.CorpsIds);

        return Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

[HttpPut("shows/{id}")]
public async Task<IActionResult> UpdateShow(Guid id, UpdateShowRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    return await adminService.UpdateShowAsync(id, req.Name, req.Url,
        req.Date, req.StartTime, req.ScoresAnnouncedTime, req.CorpsIds) ? NoContent() : NotFound();
}
```

- [ ] **Step 8: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors (frontend will have type errors in SeasonDetail.tsx until Task 9 updates the client).

- [ ] **Step 9: Commit**

```bash
git add DCF.Data/Entities/ShowEntity.cs DCF.Data/Migrations/ DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs
git commit -m "feat: ShowEntity.StartTime with migration, validation, and updated show endpoints"
```

---

### Task 7: Backend — icon upload endpoint + static file serving

**Files:**
- Modify: `DCF.Api/Program.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`

- [ ] **Step 1: Configure static files and create uploads directory**

In `DCF.Api/Program.cs`, add `using Microsoft.Extensions.FileProviders;` at the top.

After `var app = builder.Build();` and before `app.UseCors()`, add:

```csharp
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsPath, "corps-icons"));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
```

- [ ] **Step 2: Inject IWebHostEnvironment into AdminController**

Update the constructor in `AdminController.cs`:

```csharp
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IAdminService adminService, IWebHostEnvironment env) : ControllerBase
```

- [ ] **Step 3: Add the icon upload endpoint**

Add this action to `AdminController.cs` after `DeleteCorps` and before the seasons section:

```csharp
[HttpPost("corps/{id}/icon")]
public async Task<IActionResult> UploadCorpsIcon(Guid id, IFormFile file)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp", "image/svg+xml" };

    if (!allowedTypes.Contains(file.ContentType))
    {
        return BadRequest(new { error = "File must be PNG, JPEG, WebP, or SVG." });
    }

    if (file.Length > 2 * 1024 * 1024)
    {
        return BadRequest(new { error = "File must be 2 MB or smaller." });
    }

    var ext = file.ContentType switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        _ => "png"
    };

    var relativePath = $"corps-icons/{id}.{ext}";
    var uploadsDir = Path.Combine(env.ContentRootPath, "uploads", "corps-icons");
    Directory.CreateDirectory(uploadsDir);
    var filePath = Path.Combine(uploadsDir, $"{id}.{ext}");

    await using (var stream = System.IO.File.Create(filePath))
    {
        await file.CopyToAsync(stream);
    }

    var (found, oldIconPath) = await adminService.SetCorpsIconAsync(id, relativePath);

    if (!found)
    {
        System.IO.File.Delete(filePath);

        return NotFound();
    }

    if (oldIconPath != null && oldIconPath != relativePath)
    {
        var oldFilePath = Path.Combine(env.ContentRootPath, "uploads", oldIconPath);

        if (System.IO.File.Exists(oldFilePath))
        {
            System.IO.File.Delete(oldFilePath);
        }
    }

    return Ok(new { iconUrl = $"/uploads/{relativePath}" });
}
```

- [ ] **Step 4: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Api/Program.cs DCF.Api/Controllers/AdminController.cs
git commit -m "feat: corps icon upload endpoint and static file serving"
```

---

### Task 8: Frontend — api.ts types

**Files:**
- Modify: `DCF.Web/src/types/api.ts`

- [ ] **Step 1: Add iconUrl to Corps**

```typescript
export interface Corps {
  id: string;
  name: string;
  iconUrl?: string;
}
```

- [ ] **Step 2: Add startTime to Show**

```typescript
export interface Show {
  id: string;
  name: string;
  url: string;
  date: string;
  startTime?: string;
  scoresAnnouncedTime: string;
  corpsIds: string[];
}
```

- [ ] **Step 3: Add iconUrl to PickScore**

```typescript
export interface PickScore {
  corpsName: string;
  score: number | null;
  iconUrl?: string;
}
```

- [ ] **Step 4: Build**

```bash
cd DCF.Web && npm run build
```

Expected: type errors in `SeasonDetail.tsx` from the `adminCreateShow` call site (not yet updated) — all other files clean.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/types/api.ts
git commit -m "feat: add iconUrl to Corps/PickScore and startTime to Show types"
```

---

### Task 9: Frontend — API client

**Files:**
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Add corps management methods**

In `client.ts`, add to the `api` object after `adminCreateCorps`:

```typescript
adminRenameCorps: (id: string, name: string) =>
  request<Corps>(`/api/admin/corps/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) }),
adminDeleteCorps: (id: string) =>
  request<void>(`/api/admin/corps/${id}`, { method: 'DELETE' }),
```

- [ ] **Step 2: Add icon upload method**

File uploads use `FormData` — do NOT use the `request` helper since it adds `Content-Type: application/json`. Add directly to the `api` object:

```typescript
adminUploadCorpsIcon: async (id: string, file: File): Promise<{ iconUrl: string }> => {
  const token = getToken ? await getToken() : null;
  const form = new FormData();
  form.append('file', file);
  const res = await fetch(`${API_URL}/api/admin/corps/${id}/icon`, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json() as Promise<{ iconUrl: string }>;
},
```

- [ ] **Step 3: Update adminCreateShow signature**

Replace the existing `adminCreateShow` entry:

```typescript
adminCreateShow: (
  seasonId: string,
  name: string,
  url: string,
  date: string,
  startTime: string | null,
  scoresAnnouncedTime: string,
  corpsIds: string[]
) =>
  request<{ id: string; name: string }>(`/api/admin/seasons/${seasonId}/shows`, {
    method: 'POST',
    body: JSON.stringify({ name, url, date, startTime, scoresAnnouncedTime, corpsIds }),
  }),
```

- [ ] **Step 4: Add season dates, show update, and show delete methods**

Add to the `api` object:

```typescript
adminUpdateSeasonDates: (id: string, startDate: string, endDate: string) =>
  request<void>(`/api/admin/seasons/${id}/dates`, { method: 'PATCH', body: JSON.stringify({ startDate, endDate }) }),
adminUpdateShow: (id: string, body: {
  name: string;
  url: string;
  date: string;
  startTime: string | null;
  scoresAnnouncedTime: string;
  corpsIds: string[];
}) =>
  request<void>(`/api/admin/shows/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
adminDeleteShow: (id: string) =>
  request<void>(`/api/admin/shows/${id}`, { method: 'DELETE' }),
```

- [ ] **Step 5: Build**

```bash
cd DCF.Web && npm run build
```

Expected: one type error in `SeasonDetail.tsx` from the updated `adminCreateShow` signature (call site not yet updated). All other files clean.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/api/client.ts
git commit -m "feat: add admin API client methods for corps, icons, seasons, and shows"
```

---

### Task 10: Frontend — CorpsIcon component

**Files:**
- Create: `DCF.Web/src/components/CorpsIcon.tsx`

- [ ] **Step 1: Create CorpsIcon.tsx**

```tsx
import type { CSSProperties } from 'react';

const API_URL = import.meta.env.VITE_API_URL as string;

function getInitials(name: string): string {
  return name
    .replace(/^the\s+/i, '')
    .split(/\s+/)
    .map(w => w[0] ?? '')
    .join('')
    .toUpperCase()
    .slice(0, 3);
}

interface Props {
  name: string;
  iconUrl?: string | null;
  size: number;
  style?: CSSProperties;
}

export function CorpsIcon({ name, iconUrl, size, style }: Props) {
  const base: CSSProperties = {
    width: size,
    height: size,
    borderRadius: 3,
    flexShrink: 0,
    ...style,
  };

  if (iconUrl) {
    return (
      <img
        src={`${API_URL}${iconUrl}`}
        alt={name}
        title={name}
        style={{ ...base, objectFit: 'contain', display: 'block' }}
      />
    );
  }

  return (
    <div
      title={name}
      style={{
        ...base,
        background: '#3a3a4a',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: Math.max(6, Math.floor(size * 0.35)),
        fontWeight: 900,
        color: '#fff',
        letterSpacing: '-0.5px',
        userSelect: 'none',
      }}
    >
      {getInitials(name)}
    </div>
  );
}
```

- [ ] **Step 2: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors (other than the existing SeasonDetail type error from Task 9).

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/components/CorpsIcon.tsx
git commit -m "feat: CorpsIcon component with initials fallback"
```

---

### Task 11: Frontend — Admin.tsx corps tab (rename/delete + icon upload, merged)

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`

- [ ] **Step 1: Update imports**

Replace the existing React import and add `CorpsIcon`:

```typescript
import { useEffect, useRef, useState } from 'react';
import type { ChangeEvent, CSSProperties, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { CorpsIcon } from '../components/CorpsIcon';
import type { Corps, Season } from '../types/api';
```

- [ ] **Step 2: Add all new state**

Inside the `Admin` component, after the existing state declarations, add:

```typescript
const [editingCorpsId, setEditingCorpsId] = useState<string | null>(null);
const [editingCorpsName, setEditingCorpsName] = useState('');
const [savingCorpsEdit, setSavingCorpsEdit] = useState(false);
const [deletingCorpsId, setDeletingCorpsId] = useState<string | null>(null);
const [uploadingIconId, setUploadingIconId] = useState<string | null>(null);
const iconInputRefs = useRef<Record<string, HTMLInputElement | null>>({});
```

- [ ] **Step 3: Add all handlers**

Add these functions inside the `Admin` component:

```typescript
const saveCorpsRename = async (id: string) => {
  if (savingCorpsEdit) return;
  setSavingCorpsEdit(true);
  setError(null);

  try {
    await api.adminRenameCorps(id, editingCorpsName);
    const updated = await api.adminGetCorps();
    setCorps(updated);
    setEditingCorpsId(null);
  } catch {
    setError('Failed to rename corps.');
  } finally {
    setSavingCorpsEdit(false);
  }
};

const deleteCorps = async (id: string) => {
  if (deletingCorpsId) return;
  setDeletingCorpsId(id);
  setError(null);

  try {
    await api.adminDeleteCorps(id);
    const updated = await api.adminGetCorps();
    setCorps(updated);
  } catch {
    setError('Cannot delete: corps belongs to a published season.');
  } finally {
    setDeletingCorpsId(null);
  }
};

const triggerIconUpload = (id: string) => {
  iconInputRefs.current[id]?.click();
};

const handleIconFileChange = async (id: string, e: ChangeEvent<HTMLInputElement>) => {
  const file = e.target.files?.[0];
  e.target.value = '';

  if (!file || uploadingIconId) return;

  setUploadingIconId(id);
  setError(null);

  try {
    await api.adminUploadCorpsIcon(id, file);
    const updated = await api.adminGetCorps();
    setCorps(updated);
  } catch {
    setError('Failed to upload icon.');
  } finally {
    setUploadingIconId(null);
  }
};
```

- [ ] **Step 4: Replace corps list mapping**

In the `{tab === 'corps' && ...}` block, replace the corps list mapping with:

```tsx
{corps.map(c => (
  <div key={c.id} style={{
    display: 'flex', alignItems: 'center', gap: 8,
    padding: '7px 14px', background: 'var(--surface)',
    border: '1px solid var(--border)', borderRadius: 5,
  }}>
    <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={28} />
    {editingCorpsId === c.id ? (
      <>
        <input
          value={editingCorpsName}
          onChange={e => setEditingCorpsName(e.target.value)}
          onKeyDown={e => {
            if (e.key === 'Enter') { saveCorpsRename(c.id); }
            if (e.key === 'Escape') { setEditingCorpsId(null); }
          }}
          autoFocus
          style={{ ...inputStyle, flex: 1 }}
        />
        <button
          onClick={() => saveCorpsRename(c.id)}
          disabled={savingCorpsEdit || !editingCorpsName.trim()}
          style={savingCorpsEdit ? disabledBtn : primaryBtn}
        >
          Save
        </button>
        <button
          onClick={() => setEditingCorpsId(null)}
          style={{ ...primaryBtn, background: 'transparent', color: 'var(--text-muted)', border: '1px solid var(--border)' }}
        >
          Cancel
        </button>
      </>
    ) : (
      <>
        <span style={{ fontSize: 11, color: 'var(--text-heading)', flex: 1 }}>{c.name}</span>
        <button
          onClick={() => triggerIconUpload(c.id)}
          disabled={uploadingIconId === c.id}
          style={{
            fontSize: 9, background: 'transparent', border: '1px solid var(--border)',
            color: 'var(--text-muted)', cursor: uploadingIconId === c.id ? 'not-allowed' : 'pointer',
            padding: '3px 8px', borderRadius: 3, opacity: uploadingIconId === c.id ? 0.5 : 1,
          }}
        >
          {uploadingIconId === c.id ? 'Uploading…' : c.iconUrl ? 'Replace Icon' : 'Upload Icon'}
        </button>
        <button
          onClick={() => { setEditingCorpsId(c.id); setEditingCorpsName(c.name); }}
          style={{ fontSize: 10, background: 'transparent', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', padding: '4px 8px' }}
        >
          Rename
        </button>
        <button
          onClick={() => deleteCorps(c.id)}
          disabled={!!deletingCorpsId}
          style={{ fontSize: 10, background: 'transparent', border: 'none', color: 'var(--red)', cursor: 'pointer', padding: '4px 8px', opacity: deletingCorpsId === c.id ? 0.5 : 1 }}
        >
          Delete
        </button>
      </>
    )}
    <input
      ref={el => { iconInputRefs.current[c.id] = el; }}
      type="file"
      accept="image/png,image/jpeg,image/webp,image/svg+xml"
      style={{ display: 'none' }}
      onChange={e => handleIconFileChange(c.id, e)}
    />
  </div>
))}
```

- [ ] **Step 5: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors (other than the existing SeasonDetail type error).

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/Admin.tsx
git commit -m "feat: corps tab with rename, delete, and icon upload"
```

---

### Task 12: Frontend — Admin.tsx collapsible seasons panel + SeasonDetail publish warning

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

- [ ] **Step 1: Add addSeasonOpen state**

In `Admin.tsx`, add state:

```typescript
const [addSeasonOpen, setAddSeasonOpen] = useState(false);
```

In the `addSeason` handler, add `setAddSeasonOpen(false)` after successfully refreshing the list:

```typescript
const updated = await api.adminGetSeasons();
setSeasons(updated);
setNewYear('');
setNewStartDate('');
setNewEndDate('');
setAddSeasonOpen(false);  // add this line
```

- [ ] **Step 2: Replace seasons tab JSX**

Replace the entire `{tab === 'seasons' && ...}` block:

```tsx
{tab === 'seasons' && (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>

    {/* Add Season — collapsible, at top */}
    <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5 }}>
      <button
        type="button"
        onClick={() => setAddSeasonOpen(o => !o)}
        style={{
          width: '100%', display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          padding: '10px 16px', background: 'transparent', border: 'none', cursor: 'pointer',
          fontSize: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.5px',
          color: 'var(--text-faint)',
        }}
      >
        <span>Add Season</span>
        <span style={{ fontSize: 10 }}>{addSeasonOpen ? '▲' : '▼'}</span>
      </button>
      {addSeasonOpen && (
        <div style={{ padding: '0 16px 16px' }}>
          <form onSubmit={addSeason} style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
            <input type="number" value={newYear} onChange={e => setNewYear(e.target.value)} placeholder="Year" required style={{ ...inputStyle, width: 80 }} />
            <input type="date" value={newStartDate} onChange={e => setNewStartDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
            <input type="date" value={newEndDate} onChange={e => setNewEndDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
            <button type="submit" disabled={addingSeason} style={addingSeason ? disabledBtn : primaryBtn}>
              Add Season
            </button>
          </form>
        </div>
      )}
    </div>

    {/* Season list */}
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
          <span style={{ fontSize: 13, fontWeight: 800, color: 'var(--text-heading)', minWidth: 40 }}>{s.year}</span>
          <span style={{ fontSize: 10, color: 'var(--text-muted)', flex: 1 }}>{s.startDate} – {s.endDate}</span>
          <SeasonBadge season={s} />
          <Link to={`/admin/seasons/${s.id}`} style={{ fontSize: 10, color: 'var(--accent)', textDecoration: 'none', fontWeight: 600 }}>
            Manage →
          </Link>
        </div>
      ))}
    </div>
  </div>
)}
```

- [ ] **Step 3: Add publish confirmation to SeasonDetail.tsx**

Add state to `SeasonDetail.tsx`:

```typescript
const [showPublishConfirm, setShowPublishConfirm] = useState(false);
```

Change the Publish button's onClick from `onClick={publish}` to:

```tsx
onClick={() => setShowPublishConfirm(true)}
```

Add the confirmation modal inside the outermost `<div>` of the returned JSX (before the header):

```tsx
{showPublishConfirm && (
  <div style={{
    position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100,
  }}>
    <div style={{
      background: 'var(--surface)', border: '1px solid var(--border)',
      borderRadius: 8, padding: 28, maxWidth: 360, width: '90%',
    }}>
      <h2 style={{ fontSize: 14, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 10 }}>
        Publish Season {season.year}?
      </h2>
      <p style={{ fontSize: 11, color: 'var(--text-muted)', marginBottom: 20 }}>
        Once published, the corps roster is locked and cannot be changed.
      </p>
      <div style={{ display: 'flex', gap: 10 }}>
        <button
          onClick={() => setShowPublishConfirm(false)}
          style={{
            flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
            background: 'var(--surface)', border: '1px solid var(--border)',
            color: 'var(--text-heading)', cursor: 'pointer',
          }}
        >
          Cancel
        </button>
        <button
          onClick={() => { setShowPublishConfirm(false); publish(); }}
          style={{
            flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
            background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
          }}
        >
          Publish
        </button>
      </div>
    </div>
  </div>
)}
```

- [ ] **Step 4: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors (other than existing SeasonDetail type error).

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/pages/Admin.tsx DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: collapsible add season panel and publish confirmation dialog"
```

---

### Task 13: Frontend — SeasonDetail editable dates + redesigned show form

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

- [ ] **Step 1: Add helpers and new state**

At the top of `SeasonDetail.tsx`, before the component, add:

```typescript
const TZ_OFFSETS: Record<string, string> = { PT: '-07:00', MT: '-06:00', CT: '-05:00', ET: '-04:00' };

function buildDateTime(date: string, time: string, tz: string): string {
  return `${date}T${time}:00${TZ_OFFSETS[tz]}`;
}
```

Add a `labelStyle` constant alongside the existing `inputStyle`:

```typescript
const labelStyle: CSSProperties = {
  fontSize: 9, fontWeight: 700, color: 'var(--text-faint)',
  textTransform: 'uppercase', letterSpacing: '0.5px',
  minWidth: 48, textAlign: 'right', flexShrink: 0,
};
```

Remove the existing `showScoresTime` state and replace the show-form state block with:

```typescript
const [showTz, setShowTz] = useState('ET');
const [showStartTime, setShowStartTime] = useState('');
const [showScoresTime, setShowScoresTime] = useState('');
const [addShowOpen, setAddShowOpen] = useState(false);
```

Add date-edit state:

```typescript
const [editingDates, setEditingDates] = useState(false);
const [editStartDate, setEditStartDate] = useState('');
const [editEndDate, setEditEndDate] = useState('');
const [savingDates, setSavingDates] = useState(false);
```

- [ ] **Step 2: Update addShow handler**

Replace `addShow` with:

```typescript
const addShow = async (e: FormEvent) => {
  e.preventDefault();
  if (!id || addingShow) return;
  if (showCorpsIds.size === 0) { setError('Select at least one corps.'); return; }
  setAddingShow(true);
  setError(null);

  try {
    const startTimeIso = showStartTime ? buildDateTime(showDate, showStartTime, showTz) : null;
    const scoresTimeIso = buildDateTime(showDate, showScoresTime, showTz);

    await api.adminCreateShow(id, showName, showUrl, showDate, startTimeIso, scoresTimeIso, Array.from(showCorpsIds));
    const updated = await api.adminGetShows(id);
    setShows(updated);
    setShowName('');
    setShowUrl('');
    setShowDate('');
    setShowStartTime('');
    setShowScoresTime('');
    setShowCorpsIds(new Set());
    setAddShowOpen(false);
  } catch {
    setError('Failed to add show.');
  } finally {
    setAddingShow(false);
  }
};
```

- [ ] **Step 3: Add saveDates handler**

```typescript
const saveDates = async () => {
  if (!id || savingDates) return;
  setSavingDates(true);
  setError(null);

  try {
    await api.adminUpdateSeasonDates(id, editStartDate, editEndDate);
    const updated = await api.adminGetSeason(id);
    setSeason(updated);
    setEditingDates(false);
  } catch {
    setError('Failed to update season dates.');
  } finally {
    setSavingDates(false);
  }
};
```

- [ ] **Step 4: Update season header — editable dates**

Replace the dates display line:

```tsx
<div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{season.startDate} – {season.endDate} · {season.status}</div>
```

With:

```tsx
{editingDates ? (
  <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 4 }}>
    <input type="date" value={editStartDate} onChange={e => setEditStartDate(e.target.value)} style={{ ...inputStyle, width: 130 }} />
    <span style={{ color: 'var(--text-muted)', fontSize: 10 }}>–</span>
    <input type="date" value={editEndDate} onChange={e => setEditEndDate(e.target.value)} style={{ ...inputStyle, width: 130 }} />
    <button onClick={saveDates} disabled={savingDates} style={{ padding: '5px 10px', borderRadius: 4, fontSize: 10, fontWeight: 700, background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer' }}>
      Save
    </button>
    <button onClick={() => setEditingDates(false)} style={{ padding: '5px 10px', borderRadius: 4, fontSize: 10, background: 'transparent', border: '1px solid var(--border)', color: 'var(--text-muted)', cursor: 'pointer' }}>
      Cancel
    </button>
  </div>
) : (
  <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 10, color: 'var(--text-muted)', marginTop: 4 }}>
    <span>{season.startDate} – {season.endDate} · {season.status}</span>
    {!season.isPublished && (
      <button
        onClick={() => { setEditingDates(true); setEditStartDate(season.startDate); setEditEndDate(season.endDate); }}
        style={{ fontSize: 9, background: 'transparent', border: 'none', color: 'var(--accent)', cursor: 'pointer', padding: '2px 4px' }}
      >
        Edit
      </button>
    )}
  </div>
)}
```

- [ ] **Step 5: Replace Add Show form with redesigned collapsible form**

Remove the existing Add Show `<div>` block and place this at the **top** of the shows column, before the shows list:

```tsx
{/* Add Show — collapsible */}
<div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5 }}>
  <button
    type="button"
    onClick={() => setAddShowOpen(o => !o)}
    style={{
      width: '100%', display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      padding: '10px 14px', background: 'transparent', border: 'none', cursor: 'pointer',
      fontSize: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.5px',
      color: 'var(--text-faint)',
    }}
  >
    <span>Add Show</span>
    <span style={{ fontSize: 10 }}>{addShowOpen ? '▲' : '▼'}</span>
  </button>

  {addShowOpen && (
    <form onSubmit={addShow} style={{ padding: '0 14px 14px', display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <label style={labelStyle}>Name</label>
        <input value={showName} onChange={e => setShowName(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <label style={labelStyle}>URL</label>
        <input value={showUrl} onChange={e => setShowUrl(e.target.value)} placeholder="DCI recap URL" required style={{ ...inputStyle, flex: 1 }} />
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <label style={labelStyle}>Date</label>
        <input type="date" value={showDate} onChange={e => setShowDate(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
        <label style={{ ...labelStyle, marginLeft: 8 }}>TZ</label>
        <select value={showTz} onChange={e => setShowTz(e.target.value)} style={{ ...inputStyle, width: 62 }}>
          {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
        </select>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <label style={labelStyle}>Start</label>
        <input type="time" value={showStartTime} onChange={e => setShowStartTime(e.target.value)} style={{ ...inputStyle, flex: 1 }} />
        <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
        <input type="time" value={showScoresTime} onChange={e => setShowScoresTime(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
      </div>
      <div>
        <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {seasonCorps.map(c => (
            <Chip key={c.id} label={c.name} selected={showCorpsIds.has(c.id)} onClick={() => toggleShowCorps(c.id)} />
          ))}
        </div>
      </div>
      <button type="submit" disabled={addingShow} style={{
        padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
        background: addingShow ? 'var(--border)' : 'var(--accent)',
        color: addingShow ? 'var(--text-faint)' : 'var(--bg)',
        border: 'none', cursor: addingShow ? 'not-allowed' : 'pointer',
      }}>
        {addingShow ? 'Adding…' : 'Add Show'}
      </button>
    </form>
  )}
</div>
```

- [ ] **Step 6: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: editable season dates and redesigned show form with timezone pickers"
```

---

### Task 14: Frontend — Expandable show cards + LeagueCreate season guard

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

- [ ] **Step 1: Add expanded-show state**

Add to `SeasonDetail.tsx`:

```typescript
const [expandedShowId, setExpandedShowId] = useState<string | null>(null);
const [editShow, setEditShow] = useState<{
  name: string; url: string; date: string;
  startTime: string; scoresTime: string; tz: string;
  corpsIds: Set<string>;
} | null>(null);
const [savingShowEdit, setSavingShowEdit] = useState(false);
const [deletingShowId, setDeletingShowId] = useState<string | null>(null);
```

Add helper above the component alongside `buildDateTime`:

```typescript
function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}
```

- [ ] **Step 2: Add show expand, save, and delete handlers**

```typescript
function expandShow(show: Show) {
  if (expandedShowId === show.id) {
    setExpandedShowId(null);
    setEditShow(null);
    return;
  }
  const toHHMM = (iso: string) => new Date(iso).toISOString().slice(11, 16);
  setExpandedShowId(show.id);
  setEditShow({
    name: show.name,
    url: show.url,
    date: show.date,
    startTime: show.startTime ? toHHMM(show.startTime) : '',
    scoresTime: toHHMM(show.scoresAnnouncedTime),
    tz: 'ET',
    corpsIds: new Set(show.corpsIds),
  });
}

const saveShowEdit = async (showId: string) => {
  if (!editShow || savingShowEdit) return;
  setSavingShowEdit(true);
  setError(null);

  try {
    const startTimeIso = editShow.startTime
      ? buildDateTime(editShow.date, editShow.startTime, editShow.tz)
      : null;
    const scoresTimeIso = buildDateTime(editShow.date, editShow.scoresTime, editShow.tz);

    await api.adminUpdateShow(showId, {
      name: editShow.name,
      url: editShow.url,
      date: editShow.date,
      startTime: startTimeIso,
      scoresAnnouncedTime: scoresTimeIso,
      corpsIds: Array.from(editShow.corpsIds),
    });

    const updated = await api.adminGetShows(id!);
    setShows(updated);
    setExpandedShowId(null);
    setEditShow(null);
  } catch {
    setError('Failed to save show.');
  } finally {
    setSavingShowEdit(false);
  }
};

const deleteShow = async (showId: string) => {
  if (deletingShowId) return;
  setDeletingShowId(showId);
  setError(null);

  try {
    await api.adminDeleteShow(showId);
    const updated = await api.adminGetShows(id!);
    setShows(updated);
    setExpandedShowId(null);
    setEditShow(null);
  } catch {
    setError('Failed to delete show.');
  } finally {
    setDeletingShowId(null);
  }
};
```

- [ ] **Step 3: Replace show card JSX**

Replace the entire `{shows.map(s => (...))}` block:

```tsx
{shows.map(s => {
  const expanded = expandedShowId === s.id;
  const started = hasStarted(s);

  return (
    <div key={s.id} style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, overflow: 'hidden' }}>
      <div
        onClick={() => expandShow(s)}
        style={{ display: 'flex', alignItems: 'center', padding: '10px 14px', cursor: 'pointer', gap: 8 }}
      >
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-heading)' }}>{s.name}</div>
          <div style={{ fontSize: 9, color: 'var(--text-muted)' }}>
            {s.date}
            {s.startTime && ` · starts ${new Date(s.startTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`}
            {started && <span style={{ color: 'var(--accent)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>STARTED</span>}
          </div>
        </div>
        <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{expanded ? '▲' : '▼'}</span>
      </div>

      {expanded && editShow && (
        <div style={{ padding: '0 14px 14px', borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: 10 }}>
          {!started ? (
            <>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 10 }}>
                <label style={labelStyle}>Name</label>
                <input value={editShow.name} onChange={e => setEditShow(p => p && ({ ...p, name: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <label style={labelStyle}>URL</label>
                <input value={editShow.url} onChange={e => setEditShow(p => p && ({ ...p, url: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <label style={labelStyle}>Date</label>
                <input type="date" value={editShow.date} onChange={e => setEditShow(p => p && ({ ...p, date: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                <label style={{ ...labelStyle, marginLeft: 8 }}>TZ</label>
                <select value={editShow.tz} onChange={e => setEditShow(p => p && ({ ...p, tz: e.target.value }))} style={{ ...inputStyle, width: 62 }}>
                  {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
                </select>
              </div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <label style={labelStyle}>Start</label>
                <input type="time" value={editShow.startTime} onChange={e => setEditShow(p => p && ({ ...p, startTime: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                <input type="time" value={editShow.scoresTime} onChange={e => setEditShow(p => p && ({ ...p, scoresTime: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
              </div>
              <div>
                <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {seasonCorps.map(c => (
                    <Chip
                      key={c.id}
                      label={c.name}
                      selected={editShow.corpsIds.has(c.id)}
                      onClick={() => setEditShow(p => {
                        if (!p) return p;
                        const next = new Set(p.corpsIds);
                        if (next.has(c.id)) next.delete(c.id); else next.add(c.id);
                        return { ...p, corpsIds: next };
                      })}
                    />
                  ))}
                </div>
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  type="button"
                  onClick={() => deleteShow(s.id)}
                  disabled={!!deletingShowId}
                  style={{
                    flex: 1, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
                    background: 'transparent', border: '1px solid var(--red)', color: 'var(--red)',
                    cursor: deletingShowId ? 'not-allowed' : 'pointer',
                    opacity: deletingShowId === s.id ? 0.5 : 1,
                  }}
                >
                  {deletingShowId === s.id ? 'Deleting…' : 'Delete Show'}
                </button>
                <button
                  type="button"
                  onClick={() => saveShowEdit(s.id)}
                  disabled={savingShowEdit}
                  style={{
                    flex: 2, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                    background: savingShowEdit ? 'var(--border)' : 'var(--accent)',
                    color: savingShowEdit ? 'var(--text-faint)' : 'var(--bg)',
                    border: 'none', cursor: savingShowEdit ? 'not-allowed' : 'pointer',
                  }}
                >
                  {savingShowEdit ? 'Saving…' : 'Save'}
                </button>
              </div>
            </>
          ) : (
            <div style={{ marginTop: 10 }}>
              <button
                type="button"
                onClick={() => {
                  api.adminTriggerScrape(s.id)
                    .then(() => setError(null))
                    .catch(() => setError('Scrape trigger failed.'));
                }}
                style={{
                  width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                  background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                }}
              >
                Trigger Score Scrape
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
})}
```

- [ ] **Step 4: LeagueCreate no-active-season guard**

In `LeagueCreate.tsx`, replace:

```typescript
const [corpsCount, setCorpsCount] = useState<number | null>(null);

useEffect(() => {
  api.getActiveSeason().then(s => setCorpsCount(s.corpsCount)).catch(() => {});
}, []);
```

With:

```typescript
const [corpsCount, setCorpsCount] = useState<number | null>(null);
const [seasonLoaded, setSeasonLoaded] = useState(false);
const [hasActiveSeason, setHasActiveSeason] = useState(false);

useEffect(() => {
  api.getActiveSeason()
    .then(s => { setCorpsCount(s.corpsCount); setHasActiveSeason(true); })
    .catch(() => { setHasActiveSeason(false); })
    .finally(() => setSeasonLoaded(true));
}, []);
```

Add these early returns in the component's JSX (after all hooks, before the main form):

```tsx
if (!seasonLoaded) {
  return <div style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading…</div>;
}

if (!hasActiveSeason) {
  return (
    <div style={{
      background: 'var(--surface)', border: '1px solid var(--border)',
      borderRadius: 8, padding: 32, textAlign: 'center',
    }}>
      <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)', marginBottom: 8 }}>
        No active season
      </div>
      <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
        Leagues can only be created during an active season.
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx DCF.Web/src/pages/LeagueCreate.tsx
git commit -m "feat: expandable show cards with edit/delete/scrape and LeagueCreate season guard"
```

---

### Task 15: Frontend — Draft board icon rendering

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Import CorpsIcon**

Add to the imports at the top of `DraftRoom.tsx`:

```typescript
import { CorpsIcon } from '../components/CorpsIcon';
```

- [ ] **Step 2: Remove the header blank th**

In `renderGrid`, remove the blank `<th style={{ width: 80 }} />` from the `<thead>` row so only the caption headers remain:

```tsx
<thead>
  <tr>
    {captions.map(cap => (
      <th key={cap} style={{ width: cellWidth, fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-muted)', paddingBottom: 6, textAlign: 'center', fontWeight: 600 }}>
        {cap}
      </th>
    ))}
  </tr>
</thead>
```

- [ ] **Step 3: Remove left-column td and replace cell content with icons**

Replace the entire `{corps.map(c => (` block in `renderGrid`:

```tsx
{corps.map(c => (
  <tr key={c.id}>
    {captions.map(cap => {
      const taken = isTaken(c.id, cap);
      const selected = !gridLocked && selectedCell?.corpsId === c.id && selectedCell?.caption === cap;
      const previewed = !taken && !selected && validPreview?.corpsId === c.id && validPreview?.caption === cap;
      const isLobby = status === 'Open';

      let bg = 'var(--green-bg)';
      let border = '1px solid var(--green-border)';
      let boxShadow = 'none';
      const cursor = gridLocked || taken ? 'not-allowed' : 'pointer';
      let content: ReactNode;

      if (taken) {
        bg = '#12141a';
        border = '1px solid var(--border-subtle)';
        content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
      }
      else if (selected) {
        bg = 'var(--accent-bg)';
        border = '2px solid var(--accent)';
        boxShadow = '0 0 10px var(--accent-bg)';
        content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={34} style={{ outline: '1px solid var(--accent)', outlineOffset: 2 }} />;
      }
      else if (previewed) {
        const drafter = draftState.members.find(m => m.userId === validPreview!.userId);
        bg = '#1e1430';
        border = '1px dashed var(--accent-border)';
        content = (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
            <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={26} />
            <span style={{ color: 'var(--text-muted)', fontSize: 7, lineHeight: 1 }}>
              {drafter?.displayName.split(' ')[0] ?? ''}
            </span>
          </div>
        );
      }
      else {
        content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} />;
      }

      return (
        <td key={cap}>
          <div
            onClick={() => handleCellClick(c.id, cap)}
            style={{
              width: cellWidth,
              height: 44,
              background: bg,
              border,
              borderRadius: 4,
              boxShadow,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              cursor,
              opacity: isLobby ? 0.45 : 1,
              userSelect: 'none',
              transition: 'background 0.1s',
              pointerEvents: gridLocked ? 'none' : 'auto',
            }}
          >
            {content}
          </div>
        </td>
      );
    })}
  </tr>
))}
```

- [ ] **Step 4: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: replace corps name column with icon cells in draft board"
```

---

### Task 16: Frontend — League scores tab icon rendering

**Files:**
- Modify: `DCF.Web/src/components/LeagueScoresTab.tsx`

- [ ] **Step 1: Import CorpsIcon**

Add to the imports at the top of `LeagueScoresTab.tsx`:

```typescript
import { CorpsIcon } from './CorpsIcon';
```

- [ ] **Step 2: Replace corps name text with icon**

In the `{captions.flatMap(cap => [...])}` block, replace the corps `<td>`:

```tsx
<td key={`${cap}-corps`} style={{
  padding: '4px 8px',
  borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
}}>
  {pick
    ? <CorpsIcon name={pick.corpsName} iconUrl={pick.iconUrl} size={22} />
    : ''}
</td>
```

- [ ] **Step 3: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 4: Run all tests**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/components/LeagueScoresTab.tsx
git commit -m "feat: replace corps name text with icon in league scores tab"
```
