# Corps Sort Order Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let admins assign prior-season DCI placements to each corps in a season; this ordering drives the draft board row sequence and all other corps displays.

**Architecture:** `SeasonCorpsEntity` gains a nullable `SortOrder` integer; a new admin endpoint bulk-saves orders; the `SeasonDetail` response includes the current orders; a new public `GET /api/seasons/{id}/corps` endpoint returns season corps in sort order for the draft room (also fixing the existing non-admin access issue); `LeagueDetail` exposes `SeasonId` so the draft room knows which season to query.

**Tech Stack:** ASP.NET Core 10, EF Core / Npgsql, xUnit (InMemory), React 19, TypeScript, Vite

---

## File Map

**Backend:**
- Modify: `DCF.Data/Entities/SeasonCorpsEntity.cs` — add `SortOrder: int?`
- Create: migration (generated)
- Modify: `DCF.Api/Models/AdminRequests.cs` — add `CorpsOrderItem`, `SetCorpsOrderRequest`
- Modify: `DCF.Api/Services/IAdminService.cs` — add `SetSeasonCorpsOrderAsync`
- Modify: `DCF.Api/Services/AdminService.cs` — update `SeasonDetail` record, `GetSeasonDetailAsync`, add `SetSeasonCorpsOrderAsync`
- Modify: `DCF.Api/Controllers/AdminController.cs` — add `PUT seasons/{id}/corps/order`
- Modify: `DCF.Api/Controllers/SeasonsController.cs` — add `GET {id}/corps`
- Modify: `DCF.Api/Services/LeagueService.cs` — add `SeasonId` to `LeagueDetail`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs` — new tests

**Frontend:**
- Modify: `DCF.Web/src/types/api.ts` — update `SeasonDetail`, `League`; add `SeasonCorps`
- Modify: `DCF.Web/src/api/client.ts` — add `adminSetCorpsOrder`, `getSeasonCorps`
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx` — Draft Order section
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` — use ordered corps from season endpoint

---

### Task 1: SeasonCorpsEntity.SortOrder + migration

**Files:**
- Modify: `DCF.Data/Entities/SeasonCorpsEntity.cs`
- Create: `DCF.Data/Migrations/<timestamp>_AddCorpsSortOrder.cs` (generated)

- [ ] **Step 1: Add SortOrder to SeasonCorpsEntity**

Replace `DCF.Data/Entities/SeasonCorpsEntity.cs`:

```csharp
namespace DCF.Data.Entities;

public class SeasonCorpsEntity
{
    public Guid SeasonId { get; set; }
    public SeasonEntity Season { get; set; } = null!;
    public Guid CorpsId { get; set; }
    public CorpsEntity Corps { get; set; } = null!;
    public int? SortOrder { get; set; }
}
```

- [ ] **Step 2: Generate migration**

```bash
dotnet ef migrations add AddCorpsSortOrder --project DCF.Data --startup-project DCF.Api
```

Expected: new migration file created in `DCF.Data/Migrations/`.

- [ ] **Step 3: Apply migration**

```bash
dotnet ef database update --project DCF.Data --startup-project DCF.Api
```

Expected: `Done.`

- [ ] **Step 4: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add DCF.Data/Entities/SeasonCorpsEntity.cs DCF.Data/Migrations/
git commit -m "feat: add SortOrder to SeasonCorpsEntity with migration"
```

---

### Task 2: Backend — SeasonDetail with SortOrders + SetSeasonCorpsOrderAsync

**Files:**
- Modify: `DCF.Api/Models/AdminRequests.cs`
- Modify: `DCF.Api/Services/IAdminService.cs`
- Modify: `DCF.Api/Services/AdminService.cs`
- Modify: `DCF.Api/Controllers/AdminController.cs`
- Modify: `DCF.Tests/Services/AdminServiceTests.cs`

- [ ] **Step 1: Add request models**

In `DCF.Api/Models/AdminRequests.cs`, add:

```csharp
public record CorpsOrderItem(Guid CorpsId, int? SortOrder);
public record SetCorpsOrderRequest(List<CorpsOrderItem> Orders);
```

- [ ] **Step 2: Update SeasonDetail record to include CorpsSortOrders**

In `DCF.Api/Services/AdminService.cs`, change the `SeasonDetail` record:

```csharp
public record SeasonDetail(Guid Id, int Year, DateOnly StartDate, DateOnly EndDate, SeasonStatus Status, bool IsPublished, IEnumerable<Guid> CorpsIds, IReadOnlyDictionary<Guid, int> CorpsSortOrders);
```

- [ ] **Step 3: Update GetSeasonDetailAsync to populate CorpsSortOrders**

Replace `GetSeasonDetailAsync` in `AdminService.cs`:

```csharp
public async Task<SeasonDetail?> GetSeasonDetailAsync(Guid id)
{
    var season = await db.Seasons
        .Include(s => s.SeasonCorps)
        .FirstOrDefaultAsync(s => s.Id == id);

    if (season is null)
    {
        return null;
    }

    var orderedCorpsIds = season.SeasonCorps
        .OrderBy(sc => sc.SortOrder == null)
        .ThenBy(sc => sc.SortOrder)
        .Select(sc => sc.CorpsId);

    var sortOrders = season.SeasonCorps
        .Where(sc => sc.SortOrder.HasValue)
        .ToDictionary(sc => sc.CorpsId, sc => sc.SortOrder!.Value);

    return new SeasonDetail(
        season.Id, season.Year, season.StartDate, season.EndDate,
        season.Status, season.IsPublished,
        orderedCorpsIds,
        sortOrders);
}
```

- [ ] **Step 4: Add interface method**

In `DCF.Api/Services/IAdminService.cs`, add:

```csharp
Task<(bool Found, bool CanEdit)> SetSeasonCorpsOrderAsync(Guid seasonId, List<(Guid CorpsId, int? SortOrder)> orders);
```

- [ ] **Step 5: Write failing tests**

Add to `DCF.Tests/Services/AdminServiceTests.cs`:

```csharp
[Fact]
public async Task SetSeasonCorpsOrderAsync_UpdatesSortOrders()
{
    using var db = CreateDb("corps_sort_update");
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31)
    };
    var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Alpha" };
    var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Beta" };
    db.Seasons.Add(season);
    db.Corps.AddRange(corps1, corps2);
    db.SeasonCorps.AddRange(
        new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps1.Id },
        new SeasonCorpsEntity { SeasonId = season.Id, CorpsId = corps2.Id }
    );
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var orders = new List<(Guid CorpsId, int? SortOrder)>
    {
        (corps1.Id, 2),
        (corps2.Id, 1)
    };
    var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(season.Id, orders);

    Assert.True(found);
    Assert.True(canEdit);
    Assert.Equal(2, db.SeasonCorps.Single(sc => sc.CorpsId == corps1.Id).SortOrder);
    Assert.Equal(1, db.SeasonCorps.Single(sc => sc.CorpsId == corps2.Id).SortOrder);
}

[Fact]
public async Task SetSeasonCorpsOrderAsync_PublishedSeason_ReturnsCanEditFalse()
{
    using var db = CreateDb("corps_sort_published");
    var season = new SeasonEntity
    {
        Id = Guid.NewGuid(), Year = 2026,
        StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 31),
        IsPublished = true
    };
    db.Seasons.Add(season);
    await db.SaveChangesAsync();

    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());
    var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(season.Id, []);

    Assert.True(found);
    Assert.False(canEdit);
}

[Fact]
public async Task SetSeasonCorpsOrderAsync_MissingSeason_ReturnsFoundFalse()
{
    using var db = CreateDb("corps_sort_missing");
    var svc = new AdminService(db, null!, null!, new NoOpSeasonStatus());

    var (found, canEdit) = await svc.SetSeasonCorpsOrderAsync(Guid.NewGuid(), []);

    Assert.False(found);
    Assert.False(canEdit);
}
```

- [ ] **Step 6: Run tests — verify fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetSeasonCorpsOrderAsync" -v n
```

Expected: FAIL (method not implemented).

- [ ] **Step 7: Implement SetSeasonCorpsOrderAsync in AdminService.cs**

Add to `AdminService.cs`:

```csharp
public async Task<(bool Found, bool CanEdit)> SetSeasonCorpsOrderAsync(Guid seasonId, List<(Guid CorpsId, int? SortOrder)> orders)
{
    var season = await db.Seasons.FindAsync(seasonId);

    if (season is null)
    {
        return (false, false);
    }

    if (season.IsPublished)
    {
        return (true, false);
    }

    var seasonCorps = await db.SeasonCorps
        .Where(sc => sc.SeasonId == seasonId)
        .ToListAsync();

    var orderMap = orders.ToDictionary(o => o.CorpsId, o => o.SortOrder);

    foreach (var sc in seasonCorps)
    {
        if (orderMap.TryGetValue(sc.CorpsId, out var order))
        {
            sc.SortOrder = order;
        }
    }

    await db.SaveChangesAsync();

    return (true, true);
}
```

- [ ] **Step 8: Run tests — verify pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "SetSeasonCorpsOrderAsync" -v n
```

Expected: PASS (3 tests).

- [ ] **Step 9: Add controller endpoint**

In `DCF.Api/Controllers/AdminController.cs`, add after the existing `SetSeasonCorps` action:

```csharp
[HttpPut("seasons/{id}/corps/order")]
public async Task<IActionResult> SetCorpsOrder(Guid id, SetCorpsOrderRequest req)
{
    if (!await adminService.IsAdminAsync(GetSub()))
    {
        return Forbid();
    }

    var orders = req.Orders.Select(o => (o.CorpsId, o.SortOrder)).ToList();
    var (found, canEdit) = await adminService.SetSeasonCorpsOrderAsync(id, orders);

    if (!found)
    {
        return NotFound();
    }

    if (!canEdit)
    {
        return Conflict(new { error = "Season is published and cannot be modified." });
    }

    return NoContent();
}
```

- [ ] **Step 10: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 11: Commit**

```bash
git add DCF.Api/Models/AdminRequests.cs DCF.Api/Services/IAdminService.cs DCF.Api/Services/AdminService.cs DCF.Api/Controllers/AdminController.cs DCF.Tests/Services/AdminServiceTests.cs
git commit -m "feat: SeasonDetail.corpsSortOrders and SetSeasonCorpsOrderAsync endpoint"
```

---

### Task 3: Backend — public GET /api/seasons/{id}/corps

**Files:**
- Modify: `DCF.Api/Controllers/SeasonsController.cs`

- [ ] **Step 1: Add the corps endpoint**

In `DCF.Api/Controllers/SeasonsController.cs`, add after `GetActive`:

```csharp
[HttpGet("{id}/corps")]
public async Task<IActionResult> GetCorps(Guid id)
{
    var exists = await db.Seasons.AnyAsync(s => s.Id == id);

    if (!exists)
    {
        return NotFound();
    }

    var corps = await db.SeasonCorps
        .Where(sc => sc.SeasonId == id)
        .Include(sc => sc.Corps)
        .OrderBy(sc => sc.SortOrder == null)
        .ThenBy(sc => sc.SortOrder)
        .ThenBy(sc => sc.Corps.Name)
        .Select(sc => new
        {
            id = sc.CorpsId,
            name = sc.Corps.Name,
            iconUrl = sc.Corps.IconPath != null ? "/uploads/" + sc.Corps.IconPath : null,
            sortOrder = sc.SortOrder
        })
        .ToListAsync();

    return Ok(corps);
}
```

- [ ] **Step 2: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add DCF.Api/Controllers/SeasonsController.cs
git commit -m "feat: public GET /api/seasons/{id}/corps returning corps in sort order"
```

---

### Task 4: Backend — SeasonId in LeagueDetail

**Files:**
- Modify: `DCF.Api/Services/LeagueService.cs`

- [ ] **Step 1: Add SeasonId to LeagueDetail record**

In `DCF.Api/Services/LeagueService.cs`, update the `LeagueDetail` record:

```csharp
public record LeagueDetail(
    Guid Id, string Name, bool IsPublic, string? InviteCode,
    DraftStatus DraftStatus, DateTimeOffset? DraftStartTime,
    int CorpsPerCaption, Guid CommissionerUserId,
    IEnumerable<string> DraftableCaptions, int SeasonYear, Guid SeasonId,
    IEnumerable<MemberSummary> Members,
    IEnumerable<PickSummary> Picks,
    bool IsMember, int MaxPlayers);
```

- [ ] **Step 2: Update the LeagueDetail constructor call**

In `GetLeagueAsync`, update the `return new LeagueDetail(...)` call to add `league.SeasonId` after `league.Season.Year`:

```csharp
return new LeagueDetail(
    league.Id, league.Name, league.IsPublic,
    isMember ? league.InviteCode : null,
    league.DraftStatus, league.DraftStartTime, league.CorpsPerCaption,
    league.CommissionerUserId,
    league.DraftableCaptions.Select(c => c.ToString()),
    league.Season.Year,
    league.SeasonId,
    league.Members.Select(m => new MemberSummary(m.UserId, m.User.DisplayName)),
    league.DraftPicks.Select(p => new PickSummary(
        p.UserId, p.CorpsId, p.Corps.Name,
        p.Caption.ToString(), p.PickNumber, p.RoundNumber)),
    isMember,
    league.MaxPlayers);
```

- [ ] **Step 3: Build**

```bash
dotnet build DCF.slnx
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add DCF.Api/Services/LeagueService.cs
git commit -m "feat: add SeasonId to LeagueDetail response"
```

---

### Task 5: Frontend — types + API client

**Files:**
- Modify: `DCF.Web/src/types/api.ts`
- Modify: `DCF.Web/src/api/client.ts`

- [ ] **Step 1: Update SeasonDetail type**

In `DCF.Web/src/types/api.ts`, update `SeasonDetail`:

```typescript
export interface SeasonDetail extends Season {
  corpsIds: string[];
  corpsSortOrders: Record<string, number>;
}
```

- [ ] **Step 2: Add seasonId to League type**

In `DCF.Web/src/types/api.ts`, update the `League` interface to add `seasonId`:

```typescript
export interface League {
  id: string;
  name: string;
  isPublic: boolean;
  inviteCode?: string;
  commissionerUserId?: string;
  draftStatus: DraftStatus;
  draftStartTime?: string;
  corpsPerCaption?: number;
  draftableCaptions?: ComputedCaption[];
  seasonYear?: number;
  seasonId?: string;
  maxPlayers: number;
  memberCount: number;
  isMember?: boolean;
  userRank?: number;
  userScore?: number;
  members?: Member[];
  picks?: DraftPick[];
}
```

- [ ] **Step 3: Add SeasonCorps interface**

In `DCF.Web/src/types/api.ts`, add:

```typescript
export interface SeasonCorps {
  id: string;
  name: string;
  iconUrl?: string;
  sortOrder?: number;
}
```

- [ ] **Step 4: Add client methods**

In `DCF.Web/src/api/client.ts`, add to the `api` object:

```typescript
adminSetCorpsOrder: (seasonId: string, orders: { corpsId: string; sortOrder: number | null }[]) =>
  request<void>(`/api/admin/seasons/${seasonId}/corps/order`, { method: 'PUT', body: JSON.stringify({ orders }) }),
getSeasonCorps: (seasonId: string) =>
  request<SeasonCorps[]>(`/api/seasons/${seasonId}/corps`),
```

Also update the import at the top of `client.ts` to include `SeasonCorps`:

```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, Standing, UserProfile } from '../types/api';
```

- [ ] **Step 5: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/types/api.ts DCF.Web/src/api/client.ts
git commit -m "feat: SeasonDetail.corpsSortOrders, League.seasonId, SeasonCorps type, and client methods"
```

---

### Task 6: Frontend — SeasonDetail Draft Order section

**Files:**
- Modify: `DCF.Web/src/pages/SeasonDetail.tsx`

- [ ] **Step 1: Add CorpsIcon import**

At the top of `DCF.Web/src/pages/SeasonDetail.tsx`, add:

```typescript
import { CorpsIcon } from '../components/CorpsIcon';
```

- [ ] **Step 2: Add state**

Inside the `SeasonDetail` component, after the existing state declarations, add:

```typescript
const [corpsSortInputs, setCorpsSortInputs] = useState<Record<string, string>>({});
const [savingOrder, setSavingOrder] = useState(false);
```

- [ ] **Step 3: Initialise corpsSortInputs when season loads**

In the `useEffect` that loads the season, inside the `.then(s => { ... })` callback, add after `setSelectedCorpsIds(new Set(s.corpsIds))`:

```typescript
setCorpsSortInputs(
  Object.fromEntries(
    Object.entries(s.corpsSortOrders ?? {}).map(([corpsId, order]) => [corpsId, String(order)])
  )
);
```

- [ ] **Step 4: Add saveCorpsOrder handler**

Inside the `SeasonDetail` component:

```typescript
const saveCorpsOrder = async () => {
  if (!id || savingOrder) return;
  setSavingOrder(true);
  setError(null);

  try {
    const orders = Object.entries(corpsSortInputs).map(([corpsId, val]) => ({
      corpsId,
      sortOrder: parseInt(val) > 0 ? parseInt(val) : null,
    }));
    await api.adminSetCorpsOrder(id, orders);
    const updated = await api.adminGetSeason(id);
    setSeason(updated);
    setCorpsSortInputs(
      Object.fromEntries(
        Object.entries(updated.corpsSortOrders ?? {}).map(([corpsId, order]) => [corpsId, String(order)])
      )
    );
  } catch {
    setError('Failed to save order.');
  } finally {
    setSavingOrder(false);
  }
};
```

- [ ] **Step 5: Compute sortedSeasonCorps**

Add this derived value inside the component (after `seasonCorps` is defined — `seasonCorps` is already defined as `allCorps.filter(c => season.corpsIds.includes(c.id))`):

```typescript
const sortedSeasonCorps = [...seasonCorps].sort((a, b) => {
  const aVal = parseInt(corpsSortInputs[a.id] ?? '');
  const bVal = parseInt(corpsSortInputs[b.id] ?? '');
  const aRanked = !isNaN(aVal) && aVal > 0;
  const bRanked = !isNaN(bVal) && bVal > 0;
  if (aRanked && bRanked) return aVal - bVal;
  if (aRanked) return -1;
  if (bRanked) return 1;
  return a.name.localeCompare(b.name);
});
```

- [ ] **Step 6: Add Draft Order JSX section**

In the left column JSX (the `<div style={{ flex: '0 0 280px' ... }}>` that contains the corps chip section), add the Draft Order section immediately after the closing `</form>` tag of the Save Corps form:

```tsx
{seasonCorps.length > 0 && (
  <>
    <div style={{ height: 1, background: 'var(--border)', margin: '4px 0 12px' }} />
    <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 4 }}>Draft Order</div>
    {!season.isPublished && (
      <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 10 }}>
        Enter prior season placements. List re-sorts as you type.
      </div>
    )}
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginBottom: 10 }}>
      {sortedSeasonCorps.map(c => {
        const val = corpsSortInputs[c.id] ?? '';
        const isUnranked = val === '' || !(parseInt(val) > 0);
        return (
          <div key={c.id} style={{
            display: 'flex', alignItems: 'center', gap: 8,
            padding: '5px 10px', background: 'var(--surface)',
            border: '1px solid var(--border)', borderRadius: 4,
          }}>
            <input
              type="number"
              min={1}
              value={val}
              onChange={e => setCorpsSortInputs(prev => ({ ...prev, [c.id]: e.target.value }))}
              disabled={season.isPublished}
              placeholder="–"
              style={{
                width: 36, background: 'var(--bg)',
                border: `1px ${isUnranked ? 'dashed' : 'solid'} var(--border-input)`,
                borderRadius: 3, padding: '3px 5px',
                color: isUnranked ? 'var(--text-faint)' : 'var(--text-heading)',
                fontSize: 10, textAlign: 'center',
                opacity: season.isPublished ? 0.5 : 1,
                outline: 'none',
              }}
            />
            <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={22} />
            <span style={{ fontSize: 11, color: isUnranked ? 'var(--text-muted)' : 'var(--text-heading)', flex: 1 }}>
              {c.name}
            </span>
          </div>
        );
      })}
    </div>
    {!season.isPublished && (
      <button
        onClick={saveCorpsOrder}
        disabled={savingOrder}
        style={{
          padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
          background: savingOrder ? 'var(--border)' : 'var(--accent)',
          color: savingOrder ? 'var(--text-faint)' : 'var(--bg)',
          border: 'none', cursor: savingOrder ? 'not-allowed' : 'pointer',
        }}
      >
        {savingOrder ? 'Saving…' : 'Save Order'}
      </button>
    )}
  </>
)}
```

- [ ] **Step 7: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add DCF.Web/src/pages/SeasonDetail.tsx
git commit -m "feat: Draft Order section in SeasonDetail with placement inputs and live sort"
```

---

### Task 7: Frontend — DraftRoom ordered corps

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx`

- [ ] **Step 1: Update import**

In `DraftRoom.tsx`, update the import from `../types/api` to include `SeasonCorps` and remove `Corps`:

```typescript
import type { SeasonCorps, DraftState, League, PickPreview } from '../types/api';
```

- [ ] **Step 2: Change corps state type**

In `DraftRoom.tsx`, change the `corps` state declaration from `Corps[]` to `SeasonCorps[]`:

```typescript
const [corps, setCorps] = useState<SeasonCorps[]>([]);
```

- [ ] **Step 3: Replace corps fetch**

In the `useEffect` that currently fetches league and corps in parallel, replace it entirely:

```typescript
useEffect(() => {
  if (!id) return;
  api.getLeague(id)
    .then(l => {
      setLeague(l);
      if (l.seasonId) {
        api.getSeasonCorps(l.seasonId).then(setCorps).catch(() => {});
      }
    })
    .catch(() => setError('Failed to load league.'));
}, [id]);
```

- [ ] **Step 4: Build**

```bash
cd DCF.Web && npm run build
```

Expected: no errors.

- [ ] **Step 5: Run all tests**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj -v n
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/pages/DraftRoom.tsx
git commit -m "feat: DraftRoom uses season-ordered corps from public endpoint"
```
