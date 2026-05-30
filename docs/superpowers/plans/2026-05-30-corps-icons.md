# Corps Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-corps icon support — admins upload PNG/JPG/WebP/SVG images that replace text names in the draft board and league scores table, with a grey initials fallback when no icon is uploaded.

**Architecture:** `CorpsEntity` gains a nullable `IconPath` column (relative path on disk); the API serves uploaded files as static files at `/uploads/`; `CorpsSummary` and `PickScore` gain `IconUrl` (root-relative path the frontend resolves against `VITE_API_URL`). A shared `CorpsIcon` React component handles image-or-initials rendering across all three surfaces (admin, draft board, scores tab).

**Tech Stack:** ASP.NET Core 10, EF Core / Npgsql, xUnit (InMemory), React 19, TypeScript, Vite

**Note on parallel plans:** This plan may be executed before or after `docs/superpowers/plans/2026-05-29-admin-updates.md`. Both plans modify `AdminService.cs`, `AdminController.cs`, `IAdminService.cs`, and `Admin.tsx`. When applying changes to those files, merge carefully with any changes already present from the admin-updates plan.

---

## File Map

**Backend:**
- Modify: `DCF.Data/Entities/CorpsEntity.cs` — add `IconPath`
- Create: `DCF.Data/Migrations/<timestamp>_AddCorpsIconPath.cs` — generated
- Modify: `DCF.Api/Services/AdminService.cs` — update `CorpsSummary` record, `GetCorpsAsync`, `CreateCorpsAsync`; add `SetCorpsIconAsync`
- Modify: `DCF.Api/Services/IAdminService.cs` — add `SetCorpsIconAsync`
- Modify: `DCF.Api/Services/StandingsService.cs` — update `PickScore` record, pass icon URL into picks
- Modify: `DCF.Api/Controllers/AdminController.cs` — inject `IWebHostEnvironment`, add upload endpoint
- Modify: `DCF.Api/Program.cs` — create uploads directory, add `UseStaticFiles`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs` — tests for `SetCorpsIconAsync`

**Frontend:**
- Modify: `DCF.Web/src/types/api.ts` — add `iconUrl?` to `Corps` and `PickScore`
- Modify: `DCF.Web/src/api/client.ts` — add `adminUploadCorpsIcon`
- Create: `DCF.Web/src/components/CorpsIcon.tsx` — shared icon/initials component
- Modify: `DCF.Web/src/pages/Admin.tsx` — icon preview + Upload/Replace button per corps row
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` — remove left-column names, icon in each cell
- Modify: `DCF.Web/src/components/LeagueScoresTab.tsx` — icon instead of corps name text

---

### Task 1: Backend — CorpsEntity.IconPath + CorpsSummary.IconUrl + SetCorpsIconAsync

**Files:**
- Modify: `DCF.Data/Entities/CorpsEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddCorpsIconPath.cs` (generated)
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add IconPath to CorpsEntity**

In `DCF.Data/Entities/CorpsEntity.cs`, add the nullable property:

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

- [ ] **Step 3: Update CorpsSummary record**

In `DCF.Api/Services/AdminService.cs`, change the `CorpsSummary` record at the top of the file:

```csharp
public record CorpsSummary(Guid Id, string Name, string? IconUrl);
```

- [ ] **Step 4: Update GetCorpsAsync to project IconUrl**

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

- [ ] **Step 5: Update CreateCorpsAsync to pass null IconUrl**

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

- [ ] **Step 6: Write failing tests for SetCorpsIconAsync**

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

- [ ] **Step 7: Run tests — verify fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetCorpsIconAsync" -v n
```

Expected: FAIL (method not yet defined).

- [ ] **Step 8: Add SetCorpsIconAsync to interface**

In `DCF.Api/Services/IAdminService.cs`, add:

```csharp
Task<(bool Found, string? OldIconPath)> SetCorpsIconAsync(Guid id, string iconPath);
```

- [ ] **Step 9: Implement SetCorpsIconAsync in AdminService**

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

- [ ] **Step 10: Run tests — verify pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetCorpsIconAsync" -v n
```

Expected: PASS (3 tests).

- [ ] **Step 11: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 12: Commit**

```bash
git add DCF.Data/Entities/CorpsEntity.cs DCF.Data/Migrations/ DCF.Api/Services/AdminService.cs DCF.Api/Services/IAdminService.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: add CorpsEntity.IconPath, CorpsSummary.IconUrl, SetCorpsIconAsync"
```

---

### Task 2: Backend — PickScore with IconUrl

**Files:**
- Modify: `DCF.Api/Services/StandingsService.cs`

- [ ] **Step 1: Update PickScore record**

In `DCF.Api/Services/StandingsService.cs`, change the `PickScore` record at the top of the file:

```csharp
public record PickScore(string CorpsName, double? Score, string? IconUrl);
```

- [ ] **Step 2: Update ComputeMemberScoreAsync to accept and use corps icon map**

Replace `ComputeMemberScoreAsync` signature and both callers.

First, update both `GetStandingsAsync` and `GetScoreBreakdownAsync` to load the icon map alongside `corpsNames`. In both methods, replace the single corps query with:

```csharp
var corpsList = await db.Corps
    .Select(c => new { c.Id, c.Name, c.IconPath })
    .ToListAsync();
var corpsNames = corpsList.ToDictionary(c => c.Id, c => c.Name);
var corpsIcons = corpsList
    .Where(c => c.IconPath != null)
    .ToDictionary(c => c.Id, c => $"/uploads/{c.IconPath!}");
```

Then pass `corpsIcons` to `ComputeMemberScoreAsync` in both methods:

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
git commit -m "feat: add IconUrl to PickScore in standings service"
```

---

### Task 3: Backend — Icon upload endpoint + static file serving

**Files:**
- Modify: `DCF.Api/Program.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`

- [ ] **Step 1: Configure static files and create uploads directory in Program.cs**

In `DCF.Api/Program.cs`, add after `var app = builder.Build();` and before `app.UseCors()`:

```csharp
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(Path.Combine(uploadsPath, "corps-icons"));

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
```

Also add the using at the top of `Program.cs` if not already present:

```csharp
using Microsoft.Extensions.FileProviders;
```

- [ ] **Step 2: Inject IWebHostEnvironment into AdminController**

In `DCF.Api/Controllers/AdminController.cs`, update the constructor to inject `IWebHostEnvironment`:

```csharp
[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController(IAdminService adminService, IWebHostEnvironment env) : ControllerBase
```

Add the using at the top if not already present:

```csharp
using Microsoft.AspNetCore.Hosting;
```

- [ ] **Step 3: Add the icon upload endpoint**

Add this action to `AdminController.cs` after the existing `CreateCorps` and before the seasons section:

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
git commit -m "feat: icon upload endpoint and static file serving for corps icons"
```

---

### Task 4: Frontend — Types + API client

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Add iconUrl to Corps type**

In `DCF.Web/src/types/api.ts`, update the `Corps` interface:

```typescript
export interface Corps {
  id: string;
  name: string;
  iconUrl?: string;
}
```

- [ ] **Step 2: Add iconUrl to PickScore type**

In `DCF.Web/src/types/api.ts`, update the `PickScore` interface:

```typescript
export interface PickScore {
  corpsName: string;
  score: number | null;
  iconUrl?: string;
}
```

- [ ] **Step 3: Add adminUploadCorpsIcon to client**

In `DCF.Web/src/api/client.ts`, add to the `api` object (file uploads use `FormData` — do NOT use the `request` helper since it sets `Content-Type: application/json`):

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

- [ ] **Step 4: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: add iconUrl to Corps and PickScore types, add adminUploadCorpsIcon client method"
```

---

### Task 5: Frontend — CorpsIcon component

**Files:**
- Create: `DCF.Web/src/components/CorpsIcon.tsx`

- [ ] **Step 1: Create CorpsIcon.tsx**

Create `DCF.Web/src/components/CorpsIcon.tsx`:

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

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Web/src/components/CorpsIcon.tsx
git commit -m "feat: add CorpsIcon component with initials fallback"
```

---

### Task 6: Frontend — Admin.tsx icon upload UI

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`

- [ ] **Step 1: Import CorpsIcon and useRef**

At the top of `Admin.tsx`, add to the existing React import and add the CorpsIcon import:

```typescript
import { useEffect, useRef, useState } from 'react';
import type { ChangeEvent, CSSProperties, FormEvent } from 'react';
```

```typescript
import { CorpsIcon } from '../components/CorpsIcon';
```

- [ ] **Step 2: Add icon upload state and refs**

Inside the `Admin` component, after the existing state declarations, add:

```typescript
const [uploadingIconId, setUploadingIconId] = useState<string | null>(null);
const iconInputRefs = useRef<Record<string, HTMLInputElement | null>>({});
```

- [ ] **Step 3: Add upload handler functions**

Add these two functions inside the `Admin` component:

```typescript
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

- [ ] **Step 4: Replace the corps row mapping**

In the `{tab === 'corps' && ...}` block, replace the corps list mapping:

```tsx
{corps.map(c => (
  <div key={c.id} style={{
    display: 'flex', alignItems: 'center', gap: 10,
    padding: '7px 14px', background: 'var(--surface)',
    border: '1px solid var(--border)', borderRadius: 5,
  }}>
    <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={28} />
    <span style={{ flex: 1, fontSize: 11, color: 'var(--text-heading)' }}>{c.name}</span>
    <button
      onClick={() => triggerIconUpload(c.id)}
      disabled={uploadingIconId === c.id}
      style={{
        fontSize: 9, background: 'transparent',
        border: '1px solid var(--border)', color: 'var(--text-muted)',
        cursor: uploadingIconId === c.id ? 'not-allowed' : 'pointer',
        padding: '3px 8px', borderRadius: 3,
        opacity: uploadingIconId === c.id ? 0.5 : 1,
      }}
    >
      {uploadingIconId === c.id ? 'Uploading…' : c.iconUrl ? 'Replace Icon' : 'Upload Icon'}
    </button>
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

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/Admin.tsx
git commit -m "feat: corps icon preview and upload UI in admin tab"
```

---

### Task 7: Frontend — Draft board icon rendering

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Import CorpsIcon**

Add to the imports at the top of `DraftRoom.tsx`:

```typescript
import { CorpsIcon } from '../components/CorpsIcon';
```

- [ ] **Step 2: Remove the column header blank th**

In `renderGrid`, remove the blank `<th>` that reserves space for the corps name column:

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

- [ ] **Step 3: Remove left-column corps name td and replace cell content with icons**

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

### Task 8: Frontend — League scores tab icon rendering

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

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/components/LeagueScoresTab.tsx
git commit -m "feat: replace corps name text with icon in league scores tab"
```
