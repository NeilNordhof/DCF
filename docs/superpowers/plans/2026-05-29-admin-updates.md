# Admin Page Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add corp rename/delete, season date editing, show start-time + timezone, expandable show cards, collapsible panels, a publish warning, a Nav admin link, and a no-active-season guard on league creation.

**Architecture:** Backend gains PATCH/DELETE endpoints for corps, season dates, and shows; `ShowEntity` gets a nullable `StartTime` column (migration required). `ScoresAnnouncedTime` and the new `StartTime` are constructed from Date + time input + timezone offset on the frontend (DCI shows always run in summer DST — offsets are hardcoded PT=-07, MT=-06, CT=-05, ET=-04). Corps icon is excluded pending design discussion.

**Tech Stack:** ASP.NET Core 10, EF Core / Npgsql, xUnit (InMemory), React 19, TypeScript, Vite

---

## File Map

**Backend:**
- `DCF.Data/Entities/ShowEntity.cs` — add `StartTime: DateTimeOffset?`
- `DCF.Data/Migrations/<timestamp>_AddShowStartTime.cs` — generated
- `DCF.Api/Models/AdminRequests.cs` — add `RenameCorpsRequest`, `UpdateSeasonDatesRequest`; update `CreateShowRequest`/`UpdateShowRequest` for `StartTime`
- `DCF.Api/Services/IAdminService.cs` — add `RenameCorpsAsync`, `DeleteCorpsAsync`, `UpdateSeasonDatesAsync`, `DeleteShowAsync`; update `CreateShowAsync`/`UpdateShowAsync` signatures
- `DCF.Api/Services/AdminService.cs` — implement new methods; update `ShowSummary`; add date validation
- `DCF.Api/Controllers/AdminController.cs` — add PATCH/DELETE corps, PATCH season dates, DELETE show; update show create/update handlers
- `DCF.Tests/Services/AdminServiceTests.cs` — new test methods added to existing class

**Frontend:**
- `DCF.Web/src/types/api.ts` — add `startTime?: string` to `Show`
- `DCF.Web/src/api/client.ts` — add `adminRenameCorps`, `adminDeleteCorps`, `adminUpdateSeasonDates`, `adminDeleteShow`, `adminUpdateShow`; update `adminCreateShow`
- `DCF.Web/src/components/Nav.tsx` — admin link when `user.isAdmin`
- `DCF.Web/src/pages/Admin.tsx` — collapsible Add Season panel + corps inline edit/delete
- `DCF.Web/src/pages/SeasonDetail.tsx` — editable season dates; publish warning; redesigned collapsible show form; expandable show cards
- `DCF.Web/src/pages/LeagueCreate.tsx` — no-active-season guard

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

Add to the existing `AdminServiceTests` class in `DCF.Tests/Services/AdminServiceTests.cs`:

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

### Task 3: Frontend — corps tab inline edit and delete

**Files:**
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/pages/Admin.tsx`

- [ ] **Step 1: Add client methods**

In `client.ts`, add to the `api` object:

```typescript
adminRenameCorps: (id: string, name: string) =>
  request<Corps>(`/api/admin/corps/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) }),
adminDeleteCorps: (id: string) =>
  request<void>(`/api/admin/corps/${id}`, { method: 'DELETE' }),
```

- [ ] **Step 2: Add state to Admin.tsx**

Add after the existing corps state declarations:

```typescript
const [editingCorpsId, setEditingCorpsId] = useState<string | null>(null);
const [editingCorpsName, setEditingCorpsName] = useState('');
const [savingCorpsEdit, setSavingCorpsEdit] = useState(false);
const [deletingCorpsId, setDeletingCorpsId] = useState<string | null>(null);
```

- [ ] **Step 3: Add rename and delete handlers**

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
```

- [ ] **Step 4: Replace corps row JSX**

Replace the corps list mapping (inside `{tab === 'corps' && ...}`):

```tsx
{corps.map(c => (
  <div key={c.id} style={{
    display: 'flex', alignItems: 'center', gap: 8,
    padding: '7px 14px', background: 'var(--surface)',
    border: '1px solid var(--border)', borderRadius: 5,
  }}>
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
git add DCF.Web/src/api/client.ts DCF.Web/src/pages/Admin.tsx
git commit -m "feat: corps inline rename and delete in admin tab"
```

---

### Task 4: Admin page seasons tab — collapsible add panel + publish warning

**Files:**
- Modify: `DCF.Web/src/pages/Admin.tsx`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

- [ ] **Step 1: Collapsible Add Season in Admin.tsx**

Add state:

```typescript
const [addSeasonOpen, setAddSeasonOpen] = useState(false);
```

In the `addSeason` handler, add `setAddSeasonOpen(false)` after successfully refreshing the list.

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

- [ ] **Step 2: Add publish warning in SeasonDetail.tsx**

Add state:

```typescript
const [showPublishConfirm, setShowPublishConfirm] = useState(false);
```

Change the Publish button's `onClick` from `onClick={publish}` to `onClick={() => setShowPublishConfirm(true)}`.

Add the modal inside the outermost `<div>` of the returned JSX (before the header):

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

- [ ] **Step 3: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/pages/Admin.tsx DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: collapsible add season panel and publish confirmation dialog"
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

### Task 6: DB migration + show start time backend

**Files:**
- Modify: `DCF.Data/Entities/ShowEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddShowStartTime.cs` (generated)
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Modify: `DCF.Web/src/types/api.ts`

- [ ] **Step 1: Add StartTime to ShowEntity**

Replace `DCF.Data/Entities/ShowEntity.cs` with:

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

Expected: migration file created, database updated.

- [ ] **Step 3: Update ShowSummary record**

In `DCF.Api/Services/AdminService.cs`, update the `ShowSummary` record definition:

```csharp
public record ShowSummary(Guid Id, string Name, string Url, DateOnly Date, DateTimeOffset? StartTime, DateTimeOffset ScoresAnnouncedTime, IEnumerable<Guid> CorpsIds);
```

Update the `GetShowsAsync` projection to include `StartTime`:

```csharp
.Select(s => new ShowSummary(s.Id, s.Name, s.Url, s.Date, s.StartTime, s.ScoresAnnouncedTime,
    s.ShowCorps.Select(sc => sc.CorpsId)))
```

- [ ] **Step 4: Update request models**

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

- [ ] **Step 6: Update AdminService show methods with StartTime and validation**

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

Replace `UpdateShowAsync`:

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

Replace the `CreateShow` action body in `AdminController.cs`:

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

- [ ] **Step 8: Update api.ts Show interface**

In `DCF.Web/src/types/api.ts`, update the `Show` interface:

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

- [ ] **Step 9: Build both projects**

```bash
dotnet build DCF.slnx
cd DCF.Web && npm run build
```

Note: `SeasonDetail.tsx` will show a TypeScript error on the `adminCreateShow` call until Task 7 updates the client and Task 8 updates the call site. If building frontend only, expect one type error here.

- [ ] **Step 10: Commit**

```bash
git add DCF.Data/Entities/ShowEntity.cs DCF.Data/Migrations/ DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Web/src/types/api.ts
git commit -m "feat: add show start time with migration, validation, and updated endpoints"
```

---

### Task 7: Frontend — API client + timezone helper

**Files:**
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Update adminCreateShow signature**

In `client.ts`, replace the existing `adminCreateShow` entry:

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

- [ ] **Step 2: Add remaining admin methods**

Add these to the `api` object:

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

- [ ] **Step 3: Build**

```bash
cd DCF.Web && npm run build
```

Expected: one type error in `SeasonDetail.tsx` from the updated `adminCreateShow` signature (call site not yet updated). All other files clean.

- [ ] **Step 4: Commit**

```bash
git add DCF.Web/src/api/client.ts
git commit -m "feat: update admin API client with show start time and new endpoints"
```

---

### Task 8: SeasonDetail — show form redesign + editable dates

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

The `buildDateTime` helper (defined once, above the component) combines a date string, time string, and timezone abbreviation into an ISO 8601 offset string. DCI shows run June–August — DST offsets apply (PT=−07, MT=−06, CT=−05, ET=−04).

- [ ] **Step 1: Add helper and new state**

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

Remove the existing `showScoresTime` state and replace the show-form state block:

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

In the JSX, replace the dates display line:
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

Remove the existing `<div style={{ background: 'var(--surface)'... Add Show ...}}>` block and replace it with this — placed at the **top** of the shows column, before the shows list:

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
git commit -m "feat: redesign show form with timezone/time pickers and editable season dates"
```

---

### Task 9: Expandable show cards + LeagueCreate guard

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

- [ ] **Step 1: Add expanded-show state to SeasonDetail.tsx**

Add:

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

Add the helper above (or near) the component, alongside `buildDateTime`:

```typescript
function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}
```

- [ ] **Step 2: Add expand, save, and delete show handlers**

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

In the component's JSX return, add these early returns **before** the main form JSX (after any hooks):

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
git commit -m "feat: expandable show cards with edit/delete/scrape and league create season guard"
```
