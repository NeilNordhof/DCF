# Caption Preset Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace individual caption checkboxes in the league creation form with preset subcaption combinations grouped by General Effect, Visual, and Music, and add compound scoring support for the new `VisualPerformance` caption.

**Architecture:** A new `VisualPerformance` Caption enum value (appended at position 14) represents Visual Analysis + Visual Proficiency scored as one draft slot; `StandingsService` derives its score by summing the two sub-scores at read time with no scraper or migration changes. The frontend replaces checkbox state with three preset indices and a shared `PresetGroup` component that renders radio buttons.

**Tech Stack:** C# / .NET 10, EF Core (InMemory for tests), xUnit, React 19, TypeScript, Vite

---

## Files

| Action | Path | Purpose |
|--------|------|---------|
| Modify | `DCF.Data/Models/Caption.cs` | Add `VisualPerformance = 14` |
| Modify | `DCF.Api/Services/StandingsService.cs` | Add `GetEffectiveScoreAsync` helper |
| Modify | `DCF.Tests/Services/StandingsServiceTests.cs` | Test compound scoring |
| Modify | `DCF.Web/src/pages/LeagueCreate.tsx` | Preset radio button UI |

---

## Task 1: Add `VisualPerformance` to the Caption enum

**Files:**
- Modify: `DCF.Data/Models/Caption.cs`

- [ ] **Step 1: Append `VisualPerformance` at the end of the enum**

Open `DCF.Data/Models/Caption.cs`. The file currently ends with `Total` at implicit value 13. Add one entry:

```csharp
namespace DCF.Data.Models;

public enum Caption
{
    GeneralEffect,
    GeneralEffectMusic,
    GeneralEffectVisual,
    Visual,
    VisualAnalysis,
    VisualProficiency,
    ColorGuard,
    Music,
    Brass,
    MusicAnalysis,
    Percussion,
    SubTotal,
    Penalty,
    Total,
    VisualPerformance,
}
```

`VisualPerformance` gets value 14. Do **not** insert it anywhere else — EF Core stores the enum as an integer, so reordering existing entries would corrupt existing rows.

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build DCF.slnx
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```
git add DCF.Data/Models/Caption.cs
git commit -m "feat: add VisualPerformance compound caption enum value"
```

---

## Task 2: Compound scoring in `StandingsService`

**Files:**
- Modify: `DCF.Tests/Services/StandingsServiceTests.cs` (test first)
- Modify: `DCF.Api/Services/StandingsService.cs` (implementation)

- [ ] **Step 1: Write the failing test**

Open `DCF.Tests/Services/StandingsServiceTests.cs` and add this test after the existing two:

```csharp
[Fact]
public async Task GetStandings_SumsSubScoresForVisualPerformance()
{
    using var db = CreateDb("standings_visual_perf");

    var season = new SeasonEntity { Id = Guid.NewGuid(), Year = 2025, IsActive = true };
    var corps = new CorpsEntity { Id = Guid.NewGuid(), Name = "Blue Devils" };
    var show = new ShowEntity
    {
        Id = Guid.NewGuid(), Name = "Finals", Url = "https://dci.org/scores/finals-vp",
        Date = new DateOnly(2025, 8, 10), SeasonId = season.Id, Season = season
    };
    var user = new UserEntity
    {
        Id = Guid.NewGuid(), Auth0Sub = "sub|vp1", Email = "vp@test.com", DisplayName = "VP Tester"
    };
    var league = new LeagueEntity
    {
        Id = Guid.NewGuid(), Name = "VP League", SeasonId = season.Id, Season = season,
        CommissionerUserId = user.Id, Commissioner = user,
        InviteCode = "VPLEAGUE1", CorpsPerCaption = 1,
        DraftableCaptions = [Caption.VisualPerformance],
        DraftStatus = DraftStatus.Completed,
        DraftOrderJson = $"[\"{user.Id}\"]"
    };

    db.Seasons.Add(season);
    db.Corps.Add(corps);
    db.Shows.Add(show);
    db.Users.Add(user);
    db.Leagues.Add(league);
    db.LeagueMembers.Add(new LeagueMemberEntity
    {
        LeagueId = league.Id, UserId = user.Id, League = league, User = user
    });
    db.DraftPicks.Add(new DraftPickEntity
    {
        Id = Guid.NewGuid(), LeagueId = league.Id, UserId = user.Id,
        CorpsId = corps.Id, Caption = Caption.VisualPerformance, PickNumber = 0, RoundNumber = 0,
        League = league, User = user, Corps = corps
    });
    db.Scores.AddRange(
        new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
            Caption = Caption.VisualAnalysis, TotalScore = 15.5, Corps = corps, Show = show
        },
        new ScoreEntity
        {
            Id = Guid.NewGuid(), CorpsId = corps.Id, ShowId = show.Id,
            Caption = Caption.VisualProficiency, TotalScore = 14.0, Corps = corps, Show = show
        }
    );
    await db.SaveChangesAsync();

    var service = new StandingsService(db);

    var standings = await service.GetStandingsAsync(league.Id);

    Assert.Single(standings);
    Assert.Equal(29.5, standings[0].Score, precision: 5);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~GetStandings_SumsSubScoresForVisualPerformance" -v normal
```

Expected: FAIL — the current `StandingsService` queries `s.Caption == Caption.VisualPerformance` which returns nothing, so the score will be 0 instead of 29.5.

- [ ] **Step 3: Implement `GetEffectiveScoreAsync` in `StandingsService`**

Replace the full contents of `DCF.Api/Services/StandingsService.cs` with:

```csharp
using DCF.Data;
using DCF.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record MemberStanding(Guid UserId, string DisplayName, double Score);

public class StandingsService(DcfDbContext db) : IStandingsService
{
    public async Task<List<MemberStanding>> GetStandingsAsync(Guid leagueId)
    {
        var league = await db.Leagues.FindAsync(leagueId)
            ?? throw new ArgumentException("League not found", nameof(leagueId));

        var members = await db.LeagueMembers
            .Include(m => m.User)
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync();

        var standings = new List<MemberStanding>();

        foreach (var member in members)
        {
            double totalScore = 0;

            foreach (var caption in league.DraftableCaptions)
            {
                var picks = await db.DraftPicks
                    .Where(p => p.LeagueId == leagueId &&
                                p.UserId == member.UserId &&
                                p.Caption == caption)
                    .ToListAsync();

                var captionScores = new List<double>();

                foreach (var pick in picks)
                {
                    var latestScore = await GetEffectiveScoreAsync(pick.CorpsId, caption);

                    if (latestScore.HasValue)
                    {
                        captionScores.Add(latestScore.Value);
                    }
                }

                if (captionScores.Count > 0)
                {
                    totalScore += captionScores.Average();
                }
            }

            standings.Add(new MemberStanding(member.UserId, member.User.DisplayName, totalScore));
        }

        return standings.OrderByDescending(s => s.Score).ToList();
    }

    private async Task<double?> GetEffectiveScoreAsync(Guid corpsId, Caption caption)
    {
        if (caption == Caption.VisualPerformance)
        {
            var va = await db.Scores
                .Include(s => s.Show)
                .Where(s => s.CorpsId == corpsId && s.Caption == Caption.VisualAnalysis)
                .OrderByDescending(s => s.Show.Date)
                .Select(s => (double?)s.TotalScore)
                .FirstOrDefaultAsync();

            var vp = await db.Scores
                .Include(s => s.Show)
                .Where(s => s.CorpsId == corpsId && s.Caption == Caption.VisualProficiency)
                .OrderByDescending(s => s.Show.Date)
                .Select(s => (double?)s.TotalScore)
                .FirstOrDefaultAsync();

            if (va.HasValue && vp.HasValue)
            {
                return va.Value + vp.Value;
            }

            return null;
        }

        return await db.Scores
            .Include(s => s.Show)
            .Where(s => s.CorpsId == corpsId && s.Caption == caption)
            .OrderByDescending(s => s.Show.Date)
            .Select(s => (double?)s.TotalScore)
            .FirstOrDefaultAsync();
    }
}
```

- [ ] **Step 4: Run all standings tests**

```
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~StandingsServiceTests" -v normal
```

Expected: all 3 tests PASS.

- [ ] **Step 5: Commit**

```
git add DCF.Api/Services/StandingsService.cs DCF.Tests/Services/StandingsServiceTests.cs
git commit -m "feat: add compound scoring for VisualPerformance caption"
```

---

## Task 3: Preset radio button UI in `LeagueCreate.tsx`

**Files:**
- Modify: `DCF.Web/src/pages/LeagueCreate.tsx`

- [ ] **Step 1: Replace the full file contents**

```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

type CaptionPreset = { label: string; description: string; captions: string[] };

const GE_PRESETS: CaptionPreset[] = [
  { label: 'Combined', description: 'General Effect (single score)',   captions: ['GeneralEffect'] },
  { label: 'Split',    description: 'GE1 Music · GE2 Visual',         captions: ['GeneralEffectMusic', 'GeneralEffectVisual'] },
];

const VISUAL_PRESETS: CaptionPreset[] = [
  { label: 'Combined',               description: 'Visual (single score)',                    captions: ['Visual'] },
  { label: 'Vis Perf + Color Guard', description: 'Visual Performance (VA+VP) · Color Guard', captions: ['VisualPerformance', 'ColorGuard'] },
  { label: 'Split',                  description: 'Vis Analysis · Vis Prof · Color Guard',    captions: ['VisualAnalysis', 'VisualProficiency', 'ColorGuard'] },
];

const MUSIC_PRESETS: CaptionPreset[] = [
  { label: 'Combined',           description: 'Music (single score)',                captions: ['Music'] },
  { label: 'Brass + Percussion', description: 'Brass · Percussion',                 captions: ['Brass', 'Percussion'] },
  { label: 'Split',              description: 'Brass · Music Analysis · Percussion', captions: ['Brass', 'MusicAnalysis', 'Percussion'] },
];

function PresetGroup({
  legend,
  presets,
  selected,
  onChange,
}: {
  legend: string;
  presets: CaptionPreset[];
  selected: number;
  onChange: (index: number) => void;
}) {
  return (
    <fieldset>
      <legend>{legend}</legend>
      {presets.map((preset, i) => (
        <label key={i}>
          <input
            type="radio"
            name={legend}
            checked={selected === i}
            onChange={() => onChange(i)}
          />
          <strong>{preset.label}</strong>
          {' — '}
          <span>{preset.description}</span>
        </label>
      ))}
    </fieldset>
  );
}

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [gePreset, setGePreset] = useState(0);
  const [visualPreset, setVisualPreset] = useState(0);
  const [musicPreset, setMusicPreset] = useState(0);
  const [draftStartTime, setDraftStartTime] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        draftableCaptions: [
          ...GE_PRESETS[gePreset].captions,
          ...VISUAL_PRESETS[visualPreset].captions,
          ...MUSIC_PRESETS[musicPreset].captions,
        ],
        draftStartTime: draftStartTime || null,
      });

      navigate(`/leagues/${league.id}`);
    } catch {
      setError('Failed to create league. Please try again.');
      setSubmitting(false);
    }
  };

  return (
    <form onSubmit={submit}>
      <h2>Create League</h2>
      <label>Name: <input value={name} onChange={e => setName(e.target.value)} required /></label>
      <label>Public: <input type="checkbox" checked={isPublic} onChange={e => setIsPublic(e.target.checked)} /></label>
      <label>Corps per caption: <input type="number" value={corpsPerCaption} min={1} max={10} onChange={e => setCorpsPerCaption(Number(e.target.value))} /></label>
      <PresetGroup legend="General Effect" presets={GE_PRESETS} selected={gePreset} onChange={setGePreset} />
      <PresetGroup legend="Visual" presets={VISUAL_PRESETS} selected={visualPreset} onChange={setVisualPreset} />
      <PresetGroup legend="Music" presets={MUSIC_PRESETS} selected={musicPreset} onChange={setMusicPreset} />
      <label>Draft Start Time (optional): <input type="datetime-local" value={draftStartTime} onChange={e => setDraftStartTime(e.target.value)} /></label>
      {error && <p style={{ color: 'red' }}>{error}</p>}
      <button type="submit" disabled={submitting}>Create</button>
    </form>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles clean**

Run from `DCF.Web/`:

```
npm run build
```

Expected: build succeeds with 0 TypeScript errors.

- [ ] **Step 3: Commit**

```
git add DCF.Web/src/pages/LeagueCreate.tsx
git commit -m "feat: replace caption checkboxes with preset radio button groups"
```
