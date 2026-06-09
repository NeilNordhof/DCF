---
name: smoke-test-design
description: Design for API smoke test suite covering full happy path — draft, scraping, MQTT — plus prerequisite IHtmlFetcher refactor for scraper testability
metadata:
  type: project
---

# Smoke Test Suite Design

## Overview

A Python smoke test script that exercises the full API happy path end-to-end: user registration, admin setup, season/corps/show creation, league creation with multiple users, a complete snake draft including a skip and makeup pick, score scraping from a local HTML fixture, MQTT message assertions, and standings verification. It is designed to run locally and in GitHub Actions CI with no external dependencies (no live DCI.org calls, no Auth0).

A prerequisite C# refactor extracts HTTP fetching from `RecapScraperTask` into an `IHtmlFetcher` interface so the scraper can be tested without a live URL.

---

## Part 1: Prerequisite — IHtmlFetcher Refactor

### Problem

`RecapScraperTask` currently does two things: fetch HTML over HTTP and parse it. The fetch is not injectable, so there is no way to test the parser with a fixture file without hitting a live URL. The URL validation (`https://` only) also blocks local test servers.

### Changes

**New file: `DCF.Api/Scraping/IHtmlFetcher.cs`**

```csharp
public interface IHtmlFetcher
{
    Task<string> FetchAsync(string url);
}
```

**New file: `DCF.Api/Scraping/HttpHtmlFetcher.cs`**

Implements `IHtmlFetcher` using `HttpClient`. Moves the URL validation here (where it belongs as an HTTP concern, not a parsing concern). Allows `https://` for production and `http://localhost` for local testing. Throws `ArgumentException` for anything else.

**Modified: `DCF.Api/Scraping/RecapScraperTask.cs`**

Constructor changes from `(ICorpsService corpsService, HttpClient httpClient)` to `(ICorpsService corpsService, IHtmlFetcher htmlFetcher)`. The `ScrapeAsync` method replaces `_httpClient.GetStringAsync(show.URL)` and the inline URL guard with a single `await _htmlFetcher.FetchAsync(show.URL)` call. No other logic changes.

**Modified: `DCF.Api/Program.cs`**

Replace:
```csharp
builder.Services.AddHttpClient<IRecapScraperTask, RecapScraperTask>(...);
```
With:
```csharp
builder.Services.AddHttpClient<IHtmlFetcher, HttpHtmlFetcher>();
builder.Services.AddTransient<IRecapScraperTask, RecapScraperTask>();
```

**Test usage (xUnit)**

Inject a `FakeHtmlFetcher` that takes a fixture HTML string in its constructor and returns it from `FetchAsync` regardless of URL.

---

## Part 2: Smoke Test

### File Structure

```
scripts/
  smoke/
    smoke_test.py          # full orchestrator — API calls, MQTT assertions, psql ops
    cleanup.sql            # idempotent teardown — safe to run at any time
    requirements.txt       # httpx, paho-mqtt
    testdata/
      sample-recap.html    # DCI recap HTML fixture with all 12 smoke corps
.github/
  workflows/
    smoke.yml              # GitHub Actions workflow
```

### Configuration

All configuration via environment variables with sensible local defaults:

| Variable | Default | Purpose |
|---|---|---|
| `SMOKE_API_URL` | `http://localhost:5000` | Base URL for API calls |
| `SMOKE_DB_URL` | *(required)* | psql connection string |
| `SMOKE_MQTT_HOST` | `localhost` | Mosquitto TCP host |
| `SMOKE_MQTT_PORT` | `1883` | Mosquitto TCP port |

### Test Data Conventions

All smoke data is tagged for safe, targeted cleanup:

- **Users**: `Auth0Sub` values `smoke-admin`, `smoke-user-1`, `smoke-user-2`, `smoke-user-3`
- **Corps**: named `Smoke Corps 01` through `Smoke Corps 12`
- **Season**: year `9999`
- **Show**: named `Smoke Show`
- **League**: named `Smoke League`

The fixture HTML (`sample-recap.html`) uses the same corps names `Smoke Corps 01`–`12` so the scraper's name-matching finds them in the DB.

### Script Flow

The script runs all steps in a `try/finally` block so `cleanup.sql` always executes, including on assertion failures.

#### Setup Phase

**Step 1 — Start fixture HTTP server**
Spawn `python -m http.server 8099` as a subprocess pointed at `scripts/smoke/testdata/`. Killed in `finally`.

**Step 2 — Register admin user**
`POST /api/auth/me` with `Authorization: Bearer smoke-admin`, body `{ email, displayName }`. Creates the `Users` row.

**Step 3 — Elevate to admin (psql)**
```sql
UPDATE "Users" SET "IsAdmin" = true WHERE "Auth0Sub" = 'smoke-admin';
```

**Step 4 — Create 12 corps**
`POST /api/admin/corps` × 12, names `Smoke Corps 01`–`12`. Collect returned IDs.

**Step 5 — Create season**
`POST /api/admin/seasons` with year `9999`.

**Step 6 — Assign corps to season**
`PUT /api/admin/seasons/{id}/corps` with all 12 corps IDs.

**Step 7 — Create show**
`POST /api/admin/seasons/{seasonId}/shows` — name `Smoke Show`, URL `http://localhost:8099/sample-recap.html`, `ScoresAnnouncedTime` set to 1 year in the future (prevents scheduler auto-trigger), `CorpsIds` = all 12 IDs.

**Step 8 — Publish season**
`POST /api/admin/seasons/{id}/publish`.

**Step 9 — Register 3 more users**
`POST /api/auth/me` as `smoke-user-1`, `smoke-user-2`, `smoke-user-3`.

#### League + Draft Phase

**Step 10 — Create league**
`POST /api/leagues` as `smoke-admin`:
```json
{
  "name": "Smoke League",
  "isPublic": false,
  "corpsPerCaption": 1,
  "maxPlayers": 4,
  "draftableCaptions": [0, 3, 8],
  "draftStartTime": null
}
```
`draftableCaptions` values: `0` = `GeneralEffectCombined`, `3` = `VisualCombined`, `8` = `MusicCombined`.

**Step 11 — Fetch invite code**
`GET /api/leagues/{id}` as `smoke-admin`, extract `inviteCode`.

**Step 12 — Join league**
`POST /api/leagues/{id}/join` with `inviteCode` as `smoke-user-1`, `smoke-user-2`, `smoke-user-3`.

**Step 13 — Subscribe to MQTT draft topic**
Connect `paho-mqtt` client to Mosquitto TCP. Subscribe to `dcf/leagues/{leagueId}/draft`. Messages are collected in a thread-safe queue.

**Step 14 — Open and start draft**
`POST /api/leagues/{id}/draft/open` then `POST /api/leagues/{id}/draft/start` as `smoke-admin`.

**Step 15 — Dynamic pick loop**
With 4 users × 3 captions × 1 corps each = 12 main-draft picks. The loop runs until the draft is no longer in the `InProgress` state:

1. `GET /api/leagues/{id}` — read `currentDrafterUserId` and `currentPickNumber`
2. Determine which smoke user matches `currentDrafterUserId` by comparing against the user IDs collected during registration
3. On the first turn where `currentDrafterUserId` matches `smoke-user-3`'s ID: call `POST /api/leagues/{id}/draft/skip` instead of pick. Record that `smoke-user-3` has a makeup pick outstanding.
4. Otherwise: call `POST /api/leagues/{id}/draft/pick` as the current drafter with any undrafted corps and the correct caption.
5. Assert an MQTT message arrived on `dcf/leagues/{leagueId}/draft` within 3 seconds.

**Step 16 — Makeup pick**
After the main draft loop ends, `smoke-user-3` submits their missed pick: `POST /api/leagues/{id}/draft/pick` with a remaining corps and the skipped caption.

#### Scrape + Standings Phase

**Step 17 — Subscribe to MQTT scores topic**
Subscribe to `dcf/scores/updated`.

**Step 18 — Trigger scrape**
`POST /api/admin/shows/{showId}/scrape` as `smoke-admin`.

**Step 19 — Assert MQTT scores message**
Wait up to 5 seconds for a message on `dcf/scores/updated`. Fail if none arrives.

**Step 20 — Assert standings have scores**
`GET /api/leagues/{id}/standings/breakdown` — assert at least one entry has a non-zero total score.

**Step 21 — Cleanup (finally)**
Kill fixture HTTP server subprocess. Run `cleanup.sql` via `subprocess.run(["psql", DB_URL, "-f", "cleanup.sql"])`.

### MQTT Assertion Pattern

Messages are collected in a background thread using a `queue.Queue()`. The helper used for every assertion:

```python
def wait_for_message(q, timeout=3):
    try:
        return q.get(timeout=timeout)
    except queue.Empty:
        raise AssertionError(f"No MQTT message received within {timeout}s")
```

The MQTT client connects before the draft opens (step 13) so no messages are missed.

### Cleanup SQL (`cleanup.sql`)

Deletes all smoke test data. Safe to re-run at any time — uses `WHERE` conditions that are a no-op if no smoke data exists. Order respects FK constraints.

```sql
-- Scores linked to the smoke show
DELETE FROM "Scores"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

-- Computed scores for smoke league members
DELETE FROM "ComputedScores"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

-- Draft picks
DELETE FROM "DraftPicks"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

-- League members
DELETE FROM "LeagueMembers"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

-- League
DELETE FROM "Leagues" WHERE "Name" = 'Smoke League';

-- ShowCorps
DELETE FROM "ShowCorps"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

-- Show
DELETE FROM "Shows" WHERE "Name" = 'Smoke Show';

-- SeasonCorps
DELETE FROM "SeasonCorps"
    WHERE "SeasonId" IN (SELECT "Id" FROM "Seasons" WHERE "Year" = 9999);

-- Season
DELETE FROM "Seasons" WHERE "Year" = 9999;

-- Corps
DELETE FROM "Corps" WHERE "Name" LIKE 'Smoke Corps %';

-- Users
DELETE FROM "Users" WHERE "Auth0Sub" LIKE 'smoke-%';
```

### Fixture HTML (`testdata/sample-recap.html`)

Must follow the DCI recap HTML structure that `RecapScraperTask` expects:

- A `<td class="sticky-td">Corps</td>` header cell in the first row of the outer table
- One column per section (GE, Visual, Music) plus standalone columns (Sub Total, Total)
- Each of the three main sections: a header `<td>` containing a `<table class="main-sec-table">` with a `<td class="main-title"><h2>{Section Name}</h2></td>` and a `<td class="total-data-head">` for the section total header
- Each corps row: a `<td class="sticky-td">{Corps Name}</td>` followed by section cells containing `<td class="data-total"><span>{score}</span><span>{rank}</span></td>`
- Section names must be exactly `General Effect`, `Visual`, and `Music` so `TryMapCaption` resolves them to `Caption.GeneralEffect`, `Caption.Visual`, and `Caption.Music`
- One data row per corps: `Smoke Corps 01` through `Smoke Corps 12` with deterministic scores (e.g., Corps 01 = 95.0 rank 1, Corps 02 = 94.5 rank 2, etc.)

Sub-caption tables within each section are optional — the scraper gracefully skips them if absent and still captures the section total.

### GitHub Actions Workflow (`.github/workflows/smoke.yml`)

```yaml
name: Smoke Test

on:
  push:
    branches: [master]
  workflow_dispatch:

jobs:
  smoke:
    runs-on: ubuntu-latest

    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_DB: dcf
          POSTGRES_USER: postgres
          POSTGRES_PASSWORD: postgres
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

      mosquitto:
        image: eclipse-mosquitto:2
        ports:
          - 1883:1883
          - 9001:9001
        volumes:
          - ./scripts/smoke/mosquitto.conf:/mosquitto/config/mosquitto.conf

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - uses: actions/setup-python@v4
        with:
          python-version: '3.12'

      - name: Install Python deps
        run: pip install -r scripts/smoke/requirements.txt

      - name: Apply DB migrations
        run: dotnet ef database update --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
        env:
          ConnectionStrings__Default: Host=localhost;Database=dcf;Username=postgres;Password=postgres

      - name: Start API
        run: dotnet run --project DCF.Api/DCF.Api.csproj &
        env:
          ConnectionStrings__Default: Host=localhost;Database=dcf;Username=postgres;Password=postgres
          Mqtt__Host: localhost
          Mqtt__Port: 1883
          ASPNETCORE_URLS: http://localhost:5000
          ASPNETCORE_ENVIRONMENT: Development

      - name: Wait for API
        run: |
          for i in $(seq 1 30); do
            code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/seasons/active)
            [ "$code" != "000" ] && break || sleep 2
          done

      - name: Run smoke test
        run: python scripts/smoke/smoke_test.py
        env:
          SMOKE_API_URL: http://localhost:5000
          SMOKE_DB_URL: postgresql://postgres:postgres@localhost:5432/dcf
          SMOKE_MQTT_HOST: localhost
          SMOKE_MQTT_PORT: "1883"
```

**Additional file needed: `scripts/smoke/mosquitto.conf`**

The default Mosquitto 2.x image requires explicit listener and authentication configuration — without this file the container starts but refuses all connections:

```
listener 1883
allow_anonymous true
listener 9001
protocol websockets
allow_anonymous true
```

### Running Locally

```bash
# Start infra only (DB + MQTT)
docker compose up db mqtt -d

# Start API separately
dotnet run --project DCF.Api/DCF.Api.csproj

# In another terminal
pip install -r scripts/smoke/requirements.txt
SMOKE_DB_URL="Host=localhost;Database=dcf;Username=postgres;Password=postgres" \
  python scripts/smoke/smoke_test.py

# Cleanup only (if the test was interrupted)
psql $SMOKE_DB_URL -f scripts/smoke/cleanup.sql
```

---

## Implementation Order

1. `IHtmlFetcher` refactor (C# — prerequisite for everything else)
2. Fixture HTML (`sample-recap.html`)
3. `cleanup.sql`
4. `mosquitto.conf`
5. `smoke_test.py`
6. `requirements.txt`
7. GitHub Actions workflow (`smoke.yml`)
