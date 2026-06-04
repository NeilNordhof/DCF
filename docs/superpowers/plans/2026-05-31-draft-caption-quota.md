# Draft Caption Quota Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce per-user per-caption corps quotas in the draft (backend validation) and reflect quota status visually in the pick grid (x/y counter, sticky headers, full-column dim).

**Architecture:** Backend adds a single in-memory count check to `SubmitPickAsync` using the already-loaded `league.DraftPicks` navigation collection. Frontend computes `myPicksByCaption` inside `renderGrid()`, updates `<th>` to be sticky with a counter, and dims full-caption columns identically to taken cells.

**Tech Stack:** C# / .NET 10, xUnit, EF Core InMemory, React 19, TypeScript, inline styles

---

## File Map

| Action | Path | Purpose |
|--------|------|---------|
| Modify | `DCF.Api/Services/DraftService.cs` | Add quota guard after the `alreadyPicked` check in `SubmitPickAsync` |
| Modify | `DCF.Tests/Services/DraftServiceTests.cs` | Add `SubmitPickTests` class with quota-exceeded and valid-pick tests |
| Modify | `DCF.Web/src/pages/DraftRoom.tsx` | `renderGrid()`: quota tracking, sticky headers, x/y counter, full-column dim |

---

## Task 1: Backend quota check (TDD)

**Files:**
- Modify: `DCF.Tests/Services/DraftServiceTests.cs`
- Modify: `DCF.Api/Services/DraftService.cs`

- [ ] **Step 1: Add `SubmitPickTests` class with a failing quota test**

  Open `DCF.Tests/Services/DraftServiceTests.cs`. Add the following class at the end of the file (after `PublishStateTests`):

  ```csharp
  public class SubmitPickTests
  {
      private static DcfDbContext CreateDb() =>
          new(new DbContextOptionsBuilder<DcfDbContext>()
              .UseInMemoryDatabase(Guid.NewGuid().ToString())
              .Options);

      private static (DcfDbContext Db, DraftService Service, Guid PlayerId, Guid LeagueId, Guid Corps1Id, Guid Corps2Id) Seed()
      {
          var db = CreateDb();
          var player = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = "auth|player", DisplayName = "Player", Email = "p@test.com" };
          var corps1 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
          var corps2 = new CorpsEntity { Id = Guid.NewGuid(), Name = "Bluecoats" };
          var draftOrder = JsonSerializer.Serialize(new[] { player.Id.ToString() });
          var league = new LeagueEntity
          {
              Id = Guid.NewGuid(),
              Name = "Test League",
              CommissionerUserId = player.Id,
              DraftStatus = DraftStatus.InProgress,
              DraftOrderJson = draftOrder,
              CurrentPickNumber = 0,
              InviteCode = "TESTCODE",
              DraftableCaptions = [ComputedCaption.Brass],
              CorpsPerCaption = 1
          };
          db.Users.Add(player);
          db.Corps.AddRange(corps1, corps2);
          db.Leagues.Add(league);
          db.LeagueMembers.Add(new LeagueMemberEntity { LeagueId = league.Id, UserId = player.Id });
          db.SaveChanges();
          return (db, new DraftService(db, new NullMqtt(), new NullPresenceService()), player.Id, league.Id, corps1.Id, corps2.Id);
      }

      [Fact]
      public async Task SubmitPick_ThrowsWhenCaptionQuotaExceeded()
      {
          var (db, svc, playerId, leagueId, corps1Id, corps2Id) = Seed();

          // Pre-seed a pick for corps1+Brass, filling the quota of 1
          db.DraftPicks.Add(new DraftPickEntity
          {
              Id = Guid.NewGuid(), LeagueId = leagueId, UserId = playerId,
              CorpsId = corps1Id, Caption = ComputedCaption.Brass,
              PickNumber = 0, RoundNumber = 0
          });
          await db.SaveChangesAsync();

          // Attempt to pick corps2+Brass (not taken, but quota already met)
          var ex = await Assert.ThrowsAsync<InvalidOperationException>(
              () => svc.SubmitPickAsync(leagueId, "auth|player", corps2Id, ComputedCaption.Brass));

          Assert.Contains("maximum", ex.Message);
      }

      [Fact]
      public async Task SubmitPick_SucceedsWhenWithinQuota()
      {
          var (db, svc, _, leagueId, corps1Id, _) = Seed();

          var (id, pickNumber) = await svc.SubmitPickAsync(leagueId, "auth|player", corps1Id, ComputedCaption.Brass);

          Assert.NotEqual(Guid.Empty, id);
          Assert.Equal(0, pickNumber);
      }
  }
  ```

- [ ] **Step 2: Run the quota test to confirm it fails**

  ```
  dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SubmitPickTests.SubmitPick_ThrowsWhenCaptionQuotaExceeded" -v normal
  ```

  Expected: FAIL — the pick succeeds when it should throw (no quota check exists yet).

- [ ] **Step 3: Add the quota check to `SubmitPickAsync`**

  Open `DCF.Api/Services/DraftService.cs`. Find the `alreadyPicked` block (around line 152):

  ```csharp
  var alreadyPicked = await db.DraftPicks.AnyAsync(p =>
      p.LeagueId == leagueId && p.CorpsId == corpsId && p.Caption == caption);

  if (alreadyPicked)
  {
      throw new InvalidOperationException("That corps+caption is already drafted in this league");
  }
  ```

  Insert the following block immediately after (after the closing `}` of the `alreadyPicked` check):

  ```csharp
  var picksForCaption = league.DraftPicks.Count(p => p.UserId == user.Id && p.Caption == caption);

  if (picksForCaption >= league.CorpsPerCaption)
  {
      throw new InvalidOperationException($"You have already drafted the maximum {league.CorpsPerCaption} corps for this caption");
  }
  ```

  `league.DraftPicks` is already loaded via `Include(l => l.DraftPicks)` on line 135, so this is an in-memory LINQ count with no extra DB round-trip.

- [ ] **Step 4: Run both `SubmitPickTests` tests**

  ```
  dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~SubmitPickTests" -v normal
  ```

  Expected: both PASS.

- [ ] **Step 5: Run the full test suite to check for regressions**

  ```
  dotnet test DCF.Tests/DCF.Tests.csproj -v normal
  ```

  Expected: all tests PASS.

- [ ] **Step 6: Commit**

  ```
  git add DCF.Api/Services/DraftService.cs DCF.Tests/Services/DraftServiceTests.cs
  git commit -m "feat: validate corps-per-caption quota in SubmitPickAsync"
  ```

---

## Task 2: Frontend grid — quota tracking, sticky headers, x/y counter, column dim

**Files:**
- Modify: `DCF.Web/src/pages/DraftRoom.tsx` — `renderGrid()` only

- [ ] **Step 1: Add quota tracking and `isCaptionFull` at the top of `renderGrid()`**

  Open `DCF.Web/src/pages/DraftRoom.tsx`. Locate `renderGrid()` (around line 290). After the existing constants:

  ```tsx
  const captions = league.draftableCaptions!;
  const gridLocked = status !== 'InProgress' || !isMyTurn;
  const cellWidth = captions.length <= 3 ? Math.min(88, Math.floor(176 / captions.length)) : 44;
  const hGap = H_GAP[captions.length] ?? 2;
  ```

  Add immediately after:

  ```tsx
  const myPicksByCaption: Record<string, number> = {};
  draftState.picks
    .filter(p => p.userId === user?.id)
    .forEach(p => { myPicksByCaption[p.caption] = (myPicksByCaption[p.caption] ?? 0) + 1; });
  const isCaptionFull = (cap: string) =>
    (myPicksByCaption[cap] ?? 0) >= (league.corpsPerCaption ?? 0);
  ```

- [ ] **Step 2: Update `<th>` to be sticky and show the x/y counter**

  Find the `<th>` element inside the `<thead>` (around line 307):

  ```tsx
  <th key={cap} style={{ width: cellWidth, fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-muted)', paddingBottom: 6, textAlign: 'center', fontWeight: 600 }}>
    {CAPTION_SHORT[cap] ?? cap}
  </th>
  ```

  Replace with:

  ```tsx
  <th key={cap} style={{
    width: cellWidth,
    fontSize: 10,
    textTransform: 'uppercase',
    letterSpacing: '0.5px',
    color: 'var(--text-muted)',
    paddingTop: 4,
    paddingBottom: 6,
    textAlign: 'center',
    fontWeight: 600,
    position: 'sticky',
    top: 0,
    background: 'var(--bg)',
    zIndex: 1,
  }}>
    {CAPTION_SHORT[cap] ?? cap}
    <div style={{ fontSize: 7, color: 'var(--text-faint)', marginTop: 1, fontWeight: 400, textTransform: 'none', letterSpacing: 'normal' }}>
      {myPicksByCaption[cap] ?? 0}/{league.corpsPerCaption}
    </div>
  </th>
  ```

- [ ] **Step 3: Add `captionFull` to the cell rendering logic**

  Inside the `captions.map(cap => { ... })` loop, find this line (around line 320):

  ```tsx
  const isLobby = status === 'Open';
  ```

  Add immediately after:

  ```tsx
  const captionFull = isCaptionFull(cap) && !taken;
  ```

- [ ] **Step 4: Add the `captionFull` branch to the if-else styling chain**

  Find the cell styling chain. Currently it reads:

  ```tsx
  if (taken) {
    bg = '#12141a';
    border = '1px solid var(--border-subtle)';
    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
  }
  else if (selected) {
    ...
  }
  else if (previewed) {
    ...
  }
  else {
    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} />;
  }
  ```

  Insert a new `else if` between `selected` and `previewed`:

  ```tsx
  else if (captionFull) {
    bg = '#12141a';
    border = '1px solid var(--border-subtle)';
    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
  }
  ```

  So the full chain becomes:

  ```tsx
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
  else if (captionFull) {
    bg = '#12141a';
    border = '1px solid var(--border-subtle)';
    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
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
  ```

- [ ] **Step 5: Update cursor and pointerEvents on the cell div**

  Find the cell `<div>` style (around line 357). The `cursor` line currently is:

  ```tsx
  const cursor = gridLocked || taken ? 'not-allowed' : 'pointer';
  ```

  Replace with:

  ```tsx
  const cursor = gridLocked || taken || captionFull ? 'not-allowed' : 'pointer';
  ```

  Then find the `pointerEvents` line inside the cell div style:

  ```tsx
  pointerEvents: gridLocked ? 'none' : 'auto',
  ```

  Replace with:

  ```tsx
  pointerEvents: (gridLocked || captionFull) ? 'none' : 'auto',
  ```

- [ ] **Step 6: Run the TypeScript build to confirm no errors**

  From `DCF.Web/`:

  ```
  npm run build
  ```

  Expected: build succeeds with 0 TypeScript errors.

- [ ] **Step 7: Commit**

  ```
  git add DCF.Web/src/pages/DraftRoom.tsx
  git commit -m "feat: caption quota counter, sticky headers, full-column dim in draft grid"
  ```
