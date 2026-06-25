# Testing Gaps + CI Pipeline — Design Spec

**Date:** 2026-06-24
**Status:** Approved for implementation

## Problem

Unit/automated test coverage has three gaps:
1. Several backend services have no test coverage despite containing complex logic
2. The frontend has no test framework at all
3. No tests run in CI — the only automated gate before deploy is the smoke test

## Scope

**In scope:**
- Vitest setup + TimePicker test suite (issue #20)
- `ScrapeSchedulerService` tests (issue #27)
- `DraftSchedulerService`, `SeasonStatusService`, `CorpsService` tests (issue #28 partial)
- .NET code coverage uploaded as CI artifact (issue #28 partial)
- Rename `smoke.yml` → `ci.yml`; add `tests` job before `smoke`; update `deploy.yml`

**Out of scope:**
- Controller HTTP-level integration tests (`WebApplicationFactory`) — deferred
- Coverage threshold gate — report-only for now; threshold set after observing baseline

---

## Backend Tests

### Approach

Follow the existing pattern in `DCF.Tests/`: xUnit, EF Core InMemory database, stub implementations of external services (`NullMqtt`, `NullPresenceService`). No new testing libraries needed.

### ScrapeSchedulerService (issue #27)

The service mixes execution logic with scheduling plumbing (`Task.Delay` + `ConcurrentDictionary`). Tests target the logic; the plumbing is not tested.

**`ExecuteScrapeAsync`:**
- Upserts new `ScoreEntity` rows from a `Result` list
- Updates existing rows in-place (no duplicates)
- Handles empty results gracefully

**`ComputeAndUpsertComputedScoresAsync`:**
- No scores → no computed score rows written
- Partial captions (some corps missing scores for some captions) → only present scores averaged
- Multiple corps → each corps gets its own computed score row

**Delay calculation:**
Extract a `GetScrapeDelay(DateTimeOffset scoresAnnouncedTime, int delayMinutes)` pure static method from the service. Test that it returns the correct `TimeSpan` relative to the announced time. `Task.Delay` plumbing itself is not tested.

**Startup:**
Test that unscraped shows are loaded from the database at startup and that `ScheduleScrape` is called for each.

### DraftSchedulerService (issue #28)

Same approach — test the auto-open logic and delay calculation independently of `Task.Delay`:
- Draft scheduled at the correct offset from `DraftStartTime`
- Auto-opening a league sets the draft to active state
- Rescheduling when `DraftStartTime` changes cancels the old entry and creates a new one

### SeasonStatusService (issue #28)

Test the status transitions based on date inputs (use EF InMemory):
- Upcoming → Active when season start date is in the past
- Active → Completed when season end date is in the past
- No transition when both dates are in the future

### CorpsService (issue #28)

Test corps lookup by name: exact match, case-insensitive match, not-found case.

### Coverage Reporting

Add `coverlet.collector` to `DCF.Tests/DCF.Tests.csproj` as a package reference. Run with:

```
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
```

Upload the Cobertura XML as a GitHub Actions artifact named `dotnet-coverage`. No threshold.

---

## Frontend Tests (issue #20)

### Setup

New packages (dev dependencies in `DCF.Web/`):
- `vitest` — native Vite integration, Jest-compatible API
- `@vitest/coverage-v8` — coverage provider
- `@testing-library/react` — component rendering
- `@testing-library/user-event` — realistic user interactions
- `@testing-library/jest-dom` — custom matchers (`toBeInTheDocument`, etc.)
- `jsdom` — browser environment for Vitest

`vitest.config.ts` (separate file, not merged into `vite.config.ts` to keep build and test configs isolated):

```ts
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
});
```

`src/test/setup.ts`:

```ts
import '@testing-library/jest-dom';
```

New scripts in `package.json`:

```json
"test": "vitest run",
"test:watch": "vitest"
```

### TimePicker Test Suite

`DCF.Web/src/components/TimePicker.test.tsx` — covers:

- **Arrow stepping:** hour up/down with 12→1 and 1→12 wrap; minute up/down with 55→0 and 0→55 wrap
- **Minute carry:** step up at 7:55 → 8:00; step down at 8:00 → 7:55
- **AM/PM:** toggling changes `ampm` only; hour and minute unchanged
- **Empty state:** renders `--:-- --`; first arrow click initialises to 12:00 PM and emits `"12:00"`
- **Direct typing:** invalid input reverts on blur; out-of-range values clamp (hour 1–12, minute 0–59)
- **`value` prop sync:** changing `value` from outside updates the displayed hour, minute, and AM/PM
- **`required=false`:** clearing the hour field and blurring emits `""`

---

## CI/CD Pipeline

### Rename

- File: `smoke.yml` → `ci.yml`
- Workflow name: `Smoke Test` → `CI`
- `deploy.yml` trigger: `workflows: ["Smoke Test"]` → `workflows: ["CI"]`

### New `tests` job

Added to `ci.yml` as a new job before `smoke`. The `smoke` job gains `needs: [tests]`. No postgres or MQTT needed — backend tests use EF InMemory.

```yaml
tests:
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v4

    - uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.x'

    - name: Restore .NET dependencies
      run: dotnet restore DCF.slnx

    - name: Run .NET tests with coverage
      run: dotnet test DCF.Tests/DCF.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./coverage

    - name: Upload .NET coverage report
      uses: actions/upload-artifact@v4
      with:
        name: dotnet-coverage
        path: coverage/**/coverage.cobertura.xml

    - uses: actions/setup-node@v4
      with:
        node-version: '20'

    - name: Install frontend dependencies
      run: npm ci
      working-directory: DCF.Web

    - name: Run frontend tests
      run: npm test
      working-directory: DCF.Web
```

The existing `smoke` job is unchanged except for adding `needs: [tests]`.

### Resulting pipeline

```
push / PR → ci.yml
               ├── tests (dotnet test + vitest)
               └── smoke (needs: tests) → deploy.yml
```

---

## What Doesn't Change

- All existing test files — no modifications
- Smoke test script and infrastructure setup (postgres, MQTT, API start)
- Deploy workflow logic — only the trigger name changes
- No backend service logic changes beyond extracting delay calculation into a testable pure method per scheduler service
