# Caption Preset Selection — Design Spec

**Date:** 2026-05-21

## Summary

Replace the individual caption checkboxes in the league creation form with preset subcaption combinations, grouped by the three main DCI caption categories: General Effect, Visual, and Music. All three groups are always included in every league.

## Presets

### General Effect
| Preset | Label | Captions sent |
|--------|-------|---------------|
| 0 | Combined | `GeneralEffect` |
| 1 | Split | `GeneralEffectMusic`, `GeneralEffectVisual` |

### Visual
| Preset | Label | Captions sent |
|--------|-------|---------------|
| 0 | Combined | `Visual` |
| 1 | Vis Perf + Color Guard | `VisualPerformance`, `ColorGuard` |
| 2 | Split | `VisualAnalysis`, `VisualProficiency`, `ColorGuard` |

### Music
| Preset | Label | Captions sent |
|--------|-------|---------------|
| 0 | Combined | `Music` |
| 1 | Brass + Percussion | `Brass`, `Percussion` |
| 2 | Split | `Brass`, `MusicAnalysis`, `Percussion` |

## Frontend Changes (`DCF.Web`)

### `LeagueCreate.tsx`

**State:** Replace `captions: string[]` + `toggle` with three integer preset indices:

```typescript
const [gePreset, setGePreset] = useState(0);
const [visualPreset, setVisualPreset] = useState(0);
const [musicPreset, setMusicPreset] = useState(0);
```

**Preset constants** (defined outside the component, typed):

```typescript
type CaptionPreset = { label: string; description: string; captions: string[] };

const GE_PRESETS: CaptionPreset[] = [
  { label: 'Combined',  description: 'General Effect (single score)',     captions: ['GeneralEffect'] },
  { label: 'Split',     description: 'GE1 Music · GE2 Visual',           captions: ['GeneralEffectMusic', 'GeneralEffectVisual'] },
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
```

**Submit:** Derive `draftableCaptions` from selected presets at submit time — no intermediate captions state:

```typescript
draftableCaptions: [
  ...GE_PRESETS[gePreset].captions,
  ...VISUAL_PRESETS[visualPreset].captions,
  ...MUSIC_PRESETS[musicPreset].captions,
]
```

**UI:** Replace the `<fieldset>` checkbox group with three labeled radio-button sections. Each section renders its preset array as radio inputs, showing `label` as the choice name and `description` as secondary text beneath it. Remove `ALL_CAPTIONS` and `toggle`.

The `api/client.ts` `CreateLeagueRequest` type and the API contract are unchanged.

## Backend Changes (`DCF.Data` / `DCF.Api`)

### 1. New `Caption` enum value — `VisualPerformance`

Add `VisualPerformance` to `DCF.Data/Models/Caption.cs`.

**Storage:** `Caption` has no `HasConversion` on `DraftPickEntity` or `ScoreEntity`, so EF Core stores it as an integer. `DraftableCaptions` on `LeagueEntity` is JSONB serialized with `JsonSerializer` default options, which also emits integers. Existing values are 0–13. `VisualPerformance` must be **appended at the end** (value 14) to avoid remapping any existing stored integers.

`VisualPerformance` represents Visual Analysis + Visual Proficiency scored together as a single draft slot. The scraper does **not** produce `ScoreEntity` rows for it — its value is derived at read time.

### 2. `StandingsService` — compound caption scoring

The current scoring loop queries `db.Scores` where `s.Caption == caption` for each pick. `VisualPerformance` has no scraped rows, so this needs a special case: for a pick in the `VisualPerformance` caption, fetch the corps' latest `VisualAnalysis` score and latest `VisualProficiency` score separately, then sum them:

```
VisualPerformance score for pick = latestScore(corpsId, VisualAnalysis) + latestScore(corpsId, VisualProficiency)
```

The simplest implementation is a private helper `GetEffectiveScoreAsync(corpsId, caption)` that handles this branching. For all other captions it behaves as today. The result slots into the existing `captionScores` list and the average/total logic is unchanged.

No scraper changes, no new DB rows, no migration.

## What Does Not Change

- `CreateLeagueRequest` DTO and `/api/leagues` endpoint
- Draft mechanics — each caption in `draftableCaptions` still maps to one draft pick
- Scraper — `VisualPerformance` score is derived, not scraped
- All other captions and their scoring logic
