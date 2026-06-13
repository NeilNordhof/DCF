# Smoke Test Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full smoke test suite for DCF — IHtmlFetcher refactor, fixture HTML, cleanup SQL, Python orchestrator, and GitHub Actions CI workflow.

**Architecture:** C# prerequisite extracts HTTP fetching into an injectable interface so the scraper is testable with fixture files. A Python script (`smoke_test.py`) exercises the full API happy path from user registration through draft, scraping, and standings. GitHub Actions runs it on every push to master.

**Tech Stack:** C#/.NET 10 (IHtmlFetcher refactor), Python 3.12 (smoke_test.py), httpx + paho-mqtt, PostgreSQL + Mosquitto (infrastructure), GitHub Actions (CI).

---

## File Map

| File | Action |
|---|---|
| `DCF.Api/Scraping/IHtmlFetcher.cs` | Create |
| `DCF.Api/Scraping/HttpHtmlFetcher.cs` | Create |
| `DCF.Api/Scraping/RecapScraperTask.cs` | Modify (constructor + ScrapeAsync) |
| `DCF.Api/Program.cs` | Modify (DI registration) |
| `DCF.Tests/Scraping/RecapScraperTaskTests.cs` | Modify (FakeHtmlFetcher replaces StubHttpHandler) |
| `scripts/smoke/testdata/sample-recap.html` | Create |
| `scripts/smoke/cleanup.sql` | Create |
| `scripts/smoke/mosquitto.conf` | Create |
| `scripts/smoke/requirements.txt` | Create |
| `scripts/smoke/smoke_test.py` | Create |
| `.github/workflows/smoke.yml` | Create |

---

## Task 1: IHtmlFetcher Refactor (C#)

**Files:**
- Create: `DCF.Api/Scraping/IHtmlFetcher.cs`
- Create: `DCF.Api/Scraping/HttpHtmlFetcher.cs`
- Modify: `DCF.Api/Scraping/RecapScraperTask.cs`
- Modify: `DCF.Api/Program.cs`
- Modify: `DCF.Tests/Scraping/RecapScraperTaskTests.cs`

**Why:** `RecapScraperTask` currently takes `HttpClient` directly, making the scraper untestable without a live URL. Extracting fetch into `IHtmlFetcher` lets the scraper be constructed with a fake in tests and with a real HTTP client in production. As a bonus, it moves URL validation (https-only) into `HttpHtmlFetcher` — the right place for an HTTP concern — and relaxes it to also allow `http://localhost` for the smoke test fixture server.

- [ ] **Step 1: Create `IHtmlFetcher.cs`**

```csharp
namespace DCF.Api.Scraping;

public interface IHtmlFetcher
{
    Task<string> FetchAsync(string url);
}
```

Save to `DCF.Api/Scraping/IHtmlFetcher.cs`.

- [ ] **Step 2: Create `HttpHtmlFetcher.cs`**

```csharp
namespace DCF.Api.Scraping;

public class HttpHtmlFetcher : IHtmlFetcher
{
    private readonly HttpClient _httpClient;

    public HttpHtmlFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> FetchAsync(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Show URL must use HTTPS or http://localhost: {url}");
        }

        return await _httpClient.GetStringAsync(url);
    }
}
```

Save to `DCF.Api/Scraping/HttpHtmlFetcher.cs`.

- [ ] **Step 3: Modify `RecapScraperTask.cs` constructor and `ScrapeAsync`**

Replace the field and constructor at `DCF.Api/Scraping/RecapScraperTask.cs:9-43`:

```csharp
    private readonly ICorpsService _corpsService;
    private readonly IHtmlFetcher _htmlFetcher;
    // ... (CaptionMap stays unchanged) ...

    public RecapScraperTask(ICorpsService corpsService, IHtmlFetcher htmlFetcher)
    {
        _corpsService = corpsService;
        _htmlFetcher = htmlFetcher;
    }
```

Then in `ScrapeAsync`, replace lines 47–52:

```csharp
    public async Task<List<Result>> ScrapeAsync(Show show)
    {
        var html = await _htmlFetcher.FetchAsync(show.URL);
```

(Remove the old `if (!show.URL.StartsWith("https://"))` guard — that validation now lives in `HttpHtmlFetcher`.)

- [ ] **Step 4: Update `Program.cs` DI registration**

Find this block in `DCF.Api/Program.cs` (around line 39):

```csharp
builder.Services.AddHttpClient<IRecapScraperTask, RecapScraperTask>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
});
```

Replace it with:

```csharp
builder.Services.AddHttpClient<IHtmlFetcher, HttpHtmlFetcher>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
});
builder.Services.AddTransient<IRecapScraperTask, RecapScraperTask>();
```

- [ ] **Step 5: Update `RecapScraperTaskTests.cs`**

Replace the `StubHttpHandler` class and `CreateScraper` factory in `DCF.Tests/Scraping/RecapScraperTaskTests.cs`.

Remove this (lines ~29-44):
```csharp
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _html;

        public StubHttpHandler(string html)
        {
            _html = html;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_html)
            });
        }
    }
```

Add in its place:
```csharp
    private sealed class FakeHtmlFetcher : IHtmlFetcher
    {
        private readonly string _html;

        public FakeHtmlFetcher(string html)
        {
            _html = html;
        }

        public Task<string> FetchAsync(string url)
        {
            return Task.FromResult(_html);
        }
    }
```

Replace the `CreateScraper` factory (lines ~48-53):
```csharp
    private static RecapScraperTask CreateScraper(string html, IReadOnlyDictionary<string, Corps>? corps = null)
    {
        var fetcher = new FakeHtmlFetcher(html);
        var service = new StubCorpsService(corps);

        return new RecapScraperTask(service, fetcher);
    }
```

Also remove the `using System.Net;` import at the top (it was only needed for `HttpStatusCode`).

- [ ] **Step 6: Build and run tests**

```
dotnet build DCF.slnx
dotnet test DCF.Tests/DCF.Tests.csproj
```

Expected: all existing `RecapScraperTaskTests` pass. The `StubHttpHandler` is gone; the `FakeHtmlFetcher` returns HTML directly.

- [ ] **Step 7: Commit**

```
git add DCF.Api/Scraping/IHtmlFetcher.cs
git add DCF.Api/Scraping/HttpHtmlFetcher.cs
git add DCF.Api/Scraping/RecapScraperTask.cs
git add DCF.Api/Program.cs
git add DCF.Tests/Scraping/RecapScraperTaskTests.cs
git commit -m "refactor: extract IHtmlFetcher from RecapScraperTask for testability"
```

---

## Task 2: Fixture HTML — `scripts/smoke/testdata/sample-recap.html`

> **COLLABORATIVE:** This task is a good place for the user to participate. The HTML follows the exact structure the scraper expects — pointing to the test helpers in `RecapScraperTaskTests.cs` as a reference can help.

**Why the sub-caption tables are required:** `ComputeAndUpsertComputedScoresAsync` in `ScrapeSchedulerService` builds `ComputedScoreEntity` from specific raw captions: `GeneralEffectVisual`/`GeneralEffectMusic` → `GeneralEffectCombined`, `VisualProficiency`/`VisualAnalysis`/`ColorGuard` → `VisualCombined`, `Brass`/`MusicAnalysis`/`Percussion` → `MusicCombined`. Section totals (`Caption.GeneralEffect` etc.) are not used for computed scores. The fixture must use the full sub-caption format.

**The pattern (one corps row):** Look at `FullPage2025` in `DCF.Tests/Scraping/RecapScraperTaskTests.cs:126-177` — the fixture uses the exact same structure, repeated for 12 corps. Corps names must match what the API creates: `Smoke Corps 01` through `Smoke Corps 12`.

- [ ] **Step 1: Create `scripts/smoke/testdata/sample-recap.html`**

The HTML must have:
- An outer `<table>`
- A header `<tr>` with `<td class="sticky-td">Corps</td>` followed by three main-sec-table section cells (GE, Visual, Music) and three standalone cells (Sub Total, Penalties, Total)
- Twelve corps rows, each with `<td class="sticky-td">Smoke Corps 01</td>` (through `12`), matching section data cells, and standalone data cells

Each main section header cell:
```html
<td>
  <table class="main-sec-table"><tbody>
    <tr><td class="main-title"><h2>General Effect</h2></td></tr>
    <tr>
      <td><table class="table-head"><tbody>
        <tr><td class="type">General Effect 1</td></tr>
        <tr><td class="judge">Judge A</td></tr>
        <tr class="head"><td>Rep</td><td>Perf</td><td>TOT</td></tr>
      </tbody></table></td>
      <td><table class="table-head"><tbody>
        <tr><td class="type">General Effect 2</td></tr>
        <tr><td class="judge">Judge B</td></tr>
        <tr class="head"><td>Rep</td><td>Perf</td><td>TOT</td></tr>
      </tbody></table></td>
      <td class="total-data-head data-total">TOT</td>
    </tr>
  </tbody></table>
</td>
```

Each sub-caption data cell (one GE1 judge panel per corps):
```html
<td><table class="data"><tbody>
  <tr>
    <td><span>7.9</span><span>1</span></td>
    <td><span>7.4</span><span>2</span></td>
    <td><span>15.3</span><span>1</span></td>
  </tr>
</tbody></table></td>
```

Each section total data cell:
```html
<td class="data data-total"><span>30.3</span><span>1</span></td>
```

Write the complete HTML file. Use deterministic scores — for corps N (1–12), use `(96.0 - N * 0.5)` as the TOT for every sub-caption. All 12 corps use the same judge names (Judge A–H). Rank all corps as 1 to keep it simple (ranks are not validated by the smoke test).

The full file is at `scripts/smoke/testdata/sample-recap.html`. Save it there.

- [ ] **Step 2: Verify the fixture is served correctly**

Run the fixture server manually and confirm the HTML is accessible:

```bash
cd scripts/smoke/testdata
python -m http.server 8099
# In another terminal:
curl http://localhost:8099/sample-recap.html | head -20
```

Expected: the HTML is returned. Kill the server when done.

- [ ] **Step 3: Commit**

```
git add scripts/smoke/testdata/sample-recap.html
git commit -m "test: add smoke test fixture HTML with 12 smoke corps"
```

---

## Task 3: Cleanup SQL and Mosquitto Config

**Files:**
- Create: `scripts/smoke/cleanup.sql`
- Create: `scripts/smoke/mosquitto.conf`

- [ ] **Step 1: Create `scripts/smoke/cleanup.sql`**

```sql
-- Smoke test teardown. Safe to run at any time — no-op if smoke data absent.
-- Order respects FK constraints.

DELETE FROM "Scores"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "ComputedScores"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "DraftPicks"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

DELETE FROM "LeagueMembers"
    WHERE "LeagueId" IN (SELECT "Id" FROM "Leagues" WHERE "Name" = 'Smoke League');

DELETE FROM "Leagues" WHERE "Name" = 'Smoke League';

DELETE FROM "ShowCorps"
    WHERE "ShowId" IN (SELECT "Id" FROM "Shows" WHERE "Name" = 'Smoke Show');

DELETE FROM "Shows" WHERE "Name" = 'Smoke Show';

DELETE FROM "SeasonCorps"
    WHERE "SeasonId" IN (SELECT "Id" FROM "Seasons" WHERE "Year" = 9999);

DELETE FROM "Seasons" WHERE "Year" = 9999;

DELETE FROM "Corps" WHERE "Name" LIKE 'Smoke Corps %';

DELETE FROM "Users" WHERE "Auth0Sub" LIKE 'smoke-%';
```

- [ ] **Step 2: Create `scripts/smoke/mosquitto.conf`**

```
listener 1883
allow_anonymous true
listener 9001
protocol websockets
allow_anonymous true
```

- [ ] **Step 3: Commit**

```
git add scripts/smoke/cleanup.sql scripts/smoke/mosquitto.conf
git commit -m "test: add smoke cleanup SQL and mosquitto config"
```

---

## Task 4: Python requirements

> **COLLABORATIVE:** Short task — a good introduction to Python dependency files.

**Files:**
- Create: `scripts/smoke/requirements.txt`

- [ ] **Step 1: Create `scripts/smoke/requirements.txt`**

```
httpx==0.28.1
paho-mqtt==2.1.0
```

`httpx` is a modern async-capable HTTP client (similar to Python's `requests` but more powerful). `paho-mqtt` is the Eclipse Paho MQTT client for Python.

- [ ] **Step 2: Install dependencies**

```bash
pip install -r scripts/smoke/requirements.txt
```

Expected: both packages install without errors.

- [ ] **Step 3: Commit**

```
git add scripts/smoke/requirements.txt
git commit -m "test: add smoke test Python requirements"
```

---

## Task 5: `smoke_test.py`

> **COLLABORATIVE:** This is the main Python file. Work through it section by section with the user. Explain each block as you write it — this is where the Python learning happens.

**Files:**
- Create: `scripts/smoke/smoke_test.py`

**Key design decisions:**
- Draft state (current drafter, status) comes via **MQTT messages**, not HTTP polling — `DraftService.PublishDraftStateAsync` publishes to `dcf/leagues/{id}/draft` after every open/start/pick/skip
- Paho-mqtt's `loop_start()` runs a background thread that delivers messages into a `queue.Queue` — `wait_for_message` blocks until one arrives or times out
- Caption selection per user: track `user_captions_used[sub]` and always pick the first unused caption from `CAPTIONS = [0, 3, 8]`
- Corps selection: simple sequential index into `corps_ids` list
- The skip triggers on smoke-user-3's **first** main-draft turn; subsequent turns for that user proceed normally; makeup pick uses the remaining caption

**API response codes reference:**
- `POST /api/auth/me` → 200 `{id, email, displayName, isAdmin}`
- `POST /api/admin/corps` → 200 `{id, name, ...}`
- `POST /api/admin/seasons` → 200 `{id, year, ...}`
- `PUT /api/admin/seasons/{id}/corps` → 204
- `POST /api/admin/seasons/{seasonId}/shows` → 200 `{id, name, ...}`
- `POST /api/admin/seasons/{id}/publish` → 204
- `POST /api/leagues` → 201 `{id}`
- `POST /api/leagues/{id}/join` → 204
- `POST /api/leagues/{id}/draft/open` → 204
- `POST /api/leagues/{id}/draft/start` → 200
- `POST /api/leagues/{id}/draft/pick` → 200 `{id, pickNumber}`
- `POST /api/leagues/{id}/draft/skip` → 200
- `POST /api/admin/shows/{id}/scrape` → 204
- `GET /api/leagues/{id}/standings/breakdown` → 200 `[{userId, displayName, totalScore, captions}]`

- [ ] **Step 1: Create `scripts/smoke/smoke_test.py` with imports, config, and helpers**

```python
#!/usr/bin/env python3
"""Smoke test for DCF API — exercises the full happy path end-to-end."""

import json
import os
import queue
import subprocess
import sys
import time
import uuid
from pathlib import Path

import httpx
import paho.mqtt.client as mqtt_client

# --- Configuration ---
API_URL = os.environ.get("SMOKE_API_URL", "http://localhost:5000")
DB_URL = os.environ.get("SMOKE_DB_URL")          # postgresql://user:pass@host/db
MQTT_HOST = os.environ.get("SMOKE_MQTT_HOST", "localhost")
MQTT_PORT = int(os.environ.get("SMOKE_MQTT_PORT", "1883"))

SCRIPT_DIR = Path(__file__).parent

# ComputedCaption enum values (must match C# ComputedCaption order)
GE_COMBINED = 0      # GeneralEffectCombined
VISUAL_COMBINED = 3  # VisualCombined
MUSIC_COMBINED = 8   # MusicCombined
CAPTIONS = [GE_COMBINED, VISUAL_COMBINED, MUSIC_COMBINED]

# --- Helpers ---

def headers(sub: str) -> dict:
    return {"Authorization": f"Bearer {sub}", "Content-Type": "application/json"}


def api(method: str, path: str, sub: str, **kwargs) -> httpx.Response:
    return httpx.request(method, f"{API_URL}{path}", headers=headers(sub), timeout=10, **kwargs)


def assert_status(resp: httpx.Response, expected: int, label: str) -> None:
    assert resp.status_code == expected, (
        f"{label}: expected HTTP {expected}, got {resp.status_code}\n{resp.text}"
    )


def wait_for_message(q: queue.Queue, timeout: int = 3):
    try:
        return q.get(timeout=timeout)
    except queue.Empty:
        raise AssertionError(f"No MQTT message received within {timeout}s")
```

- [ ] **Step 2: Add the setup phase (steps 1–9)**

Append to `smoke_test.py`:

```python
def main() -> None:
    if not DB_URL:
        raise RuntimeError("SMOKE_DB_URL environment variable is required")

    http_server_proc = None
    mqtt = None

    try:
        # Step 1: Start fixture HTTP server
        testdata_dir = SCRIPT_DIR / "testdata"
        http_server_proc = subprocess.Popen(
            [sys.executable, "-m", "http.server", "8099", "--directory", str(testdata_dir)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        time.sleep(1)
        print("Step 1: Fixture HTTP server started on :8099")

        # Step 2: Register admin
        resp = api("POST", "/api/auth/me", "smoke-admin",
                   json={"email": "smoke-admin@example.com", "displayName": "Smoke Admin"})
        assert_status(resp, 200, "Register admin")
        admin_id = resp.json()["id"]
        print(f"Step 2: Admin registered (id={admin_id})")

        # Step 3: Elevate to admin via psql
        subprocess.run(
            ["psql", DB_URL, "-c",
             "UPDATE \"Users\" SET \"IsAdmin\" = true WHERE \"Auth0Sub\" = 'smoke-admin';"],
            check=True,
        )
        print("Step 3: Admin elevated")

        # Step 4: Create 12 corps
        corps_ids = []
        for i in range(1, 13):
            resp = api("POST", "/api/admin/corps", "smoke-admin",
                       json={"name": f"Smoke Corps {i:02d}"})
            assert_status(resp, 200, f"Create corps {i:02d}")
            corps_ids.append(resp.json()["id"])
        print(f"Step 4: Created {len(corps_ids)} corps")

        # Step 5: Create season
        resp = api("POST", "/api/admin/seasons", "smoke-admin",
                   json={"year": 9999, "startDate": "9999-06-01", "endDate": "9999-08-31"})
        assert_status(resp, 200, "Create season")
        season_id = resp.json()["id"]
        print(f"Step 5: Season created ({season_id})")

        # Step 6: Assign corps to season
        resp = api("PUT", f"/api/admin/seasons/{season_id}/corps", "smoke-admin",
                   json={"corpsIds": corps_ids})
        assert_status(resp, 204, "Assign corps to season")
        print("Step 6: Corps assigned to season")

        # Step 7: Create show — ScoresAnnouncedTime far in future prevents scheduler auto-trigger
        resp = api("POST", f"/api/admin/seasons/{season_id}/shows", "smoke-admin", json={
            "name": "Smoke Show",
            "url": "http://localhost:8099/sample-recap.html",
            "date": "9999-07-10",
            "startTime": None,
            "scoresAnnouncedTime": "2027-01-01T00:00:00Z",
            "timezone": None,
            "corpsIds": corps_ids,
        })
        assert_status(resp, 200, "Create show")
        show_id = resp.json()["id"]
        print(f"Step 7: Show created ({show_id})")

        # Step 8: Publish season
        resp = api("POST", f"/api/admin/seasons/{season_id}/publish", "smoke-admin")
        assert_status(resp, 204, "Publish season")
        print("Step 8: Season published")

        # Step 9: Register 3 users
        user_subs = ["smoke-user-1", "smoke-user-2", "smoke-user-3"]
        all_user_ids = {"smoke-admin": admin_id}
        for sub in user_subs:
            resp = api("POST", "/api/auth/me", sub,
                       json={"email": f"{sub}@example.com",
                             "displayName": sub.replace("-", " ").title()})
            assert_status(resp, 200, f"Register {sub}")
            all_user_ids[sub] = resp.json()["id"]
        id_to_sub = {v: k for k, v in all_user_ids.items()}
        print(f"Step 9: Registered users: {list(all_user_ids.keys())}")
```

- [ ] **Step 3: Add league and draft setup (steps 10–14)**

Continue appending to `smoke_test.py` inside `main()`:

```python
        # Step 10: Create league
        resp = api("POST", "/api/leagues", "smoke-admin", json={
            "name": "Smoke League",
            "isPublic": False,
            "corpsPerCaption": 1,
            "maxPlayers": 4,
            "draftableCaptions": CAPTIONS,
            "draftStartTime": None,
        })
        assert_status(resp, 201, "Create league")
        league_id = resp.json()["id"]
        print(f"Step 10: League created ({league_id})")

        # Step 11: Fetch invite code
        resp = api("GET", f"/api/leagues/{league_id}", "smoke-admin")
        assert_status(resp, 200, "Get league")
        invite_code = resp.json()["inviteCode"]
        print(f"Step 11: Invite code: {invite_code}")

        # Step 12: Join league
        for sub in user_subs:
            resp = api("POST", f"/api/leagues/{league_id}/join", sub,
                       json={"inviteCode": invite_code})
            assert_status(resp, 204, f"Join as {sub}")
        print("Step 12: All 3 users joined")

        # Step 13: Subscribe to MQTT draft topic before opening draft
        draft_queue: queue.Queue = queue.Queue()
        scores_queue: queue.Queue = queue.Queue()

        def on_message(client, userdata, msg):
            if "draft" in msg.topic:
                draft_queue.put(msg)
            elif "scores" in msg.topic:
                scores_queue.put(msg)

        mqtt = mqtt_client.Client(client_id=f"smoke-{uuid.uuid4().hex[:8]}")
        mqtt.on_message = on_message
        mqtt.connect(MQTT_HOST, MQTT_PORT)
        mqtt.subscribe(f"dcf/leagues/{league_id}/draft")
        mqtt.loop_start()
        print(f"Step 13: MQTT subscribed to draft topic")

        # Step 14: Open draft (publishes MQTT "Open" state), then start
        resp = api("POST", f"/api/leagues/{league_id}/draft/open", "smoke-admin")
        assert_status(resp, 204, "Open draft")
        wait_for_message(draft_queue, timeout=5)   # drain the "Open" state message

        resp = api("POST", f"/api/leagues/{league_id}/draft/start", "smoke-admin")
        assert_status(resp, 200, "Start draft")
        print("Step 14: Draft opened and started")
```

- [ ] **Step 4: Add the dynamic pick loop (step 15) and makeup pick (step 16)**

Continue appending inside `main()`:

```python
        # Step 15: Dynamic pick loop — driven by MQTT messages
        # The "InProgress" MQTT message arrives after draft/start
        state_msg = wait_for_message(draft_queue, timeout=5)
        draft_state = json.loads(state_msg.payload)

        corps_idx = 0
        user_captions_used: dict[str, list[int]] = {s: [] for s in all_user_ids}
        skipped_user3 = False

        while draft_state.get("status") == "InProgress":
            drafter_id = draft_state.get("currentDrafterId")

            if drafter_id is None:
                # currentDrafterId is None during makeup phase — exit main loop
                break

            sub = id_to_sub[drafter_id]

            if sub == "smoke-user-3" and not skipped_user3:
                resp = api("POST", f"/api/leagues/{league_id}/draft/skip", "smoke-user-3")
                assert_status(resp, 200, "Skip smoke-user-3")
                skipped_user3 = True
                print(f"  Skipped smoke-user-3 (pick deferred to makeup phase)")
            else:
                corps_id = corps_ids[corps_idx]
                corps_idx += 1

                # Pick the first caption this user hasn't used yet
                caption = next(c for c in CAPTIONS if c not in user_captions_used[sub])
                user_captions_used[sub].append(caption)

                resp = api("POST", f"/api/leagues/{league_id}/draft/pick", sub,
                           json={"corpsId": corps_id, "caption": caption})
                assert_status(resp, 200, f"Pick for {sub}")
                print(f"  {sub} picked corps #{corps_idx} (caption={caption})")

            # Assert MQTT draft message arrived after each pick/skip
            state_msg = wait_for_message(draft_queue, timeout=3)
            draft_state = json.loads(state_msg.payload)

        print("Step 15: Main draft loop complete")
        assert skipped_user3, "Expected smoke-user-3 to have been skipped at least once"

        # Step 16: Makeup pick for smoke-user-3
        makeup_corps_id = corps_ids[corps_idx]
        corps_idx += 1
        makeup_caption = next(c for c in CAPTIONS if c not in user_captions_used["smoke-user-3"])
        user_captions_used["smoke-user-3"].append(makeup_caption)

        resp = api("POST", f"/api/leagues/{league_id}/draft/pick", "smoke-user-3",
                   json={"corpsId": makeup_corps_id, "caption": makeup_caption})
        assert_status(resp, 200, "Makeup pick for smoke-user-3")
        print(f"Step 16: Makeup pick done (caption={makeup_caption})")
```

- [ ] **Step 5: Add the scrape and standings phase (steps 17–20)**

Continue appending inside `main()`:

```python
        # Step 17: Subscribe to scores topic
        mqtt.subscribe("dcf/scores/updated")
        print("Step 17: Subscribed to dcf/scores/updated")

        # Step 18: Trigger scrape
        resp = api("POST", f"/api/admin/shows/{show_id}/scrape", "smoke-admin")
        assert_status(resp, 204, "Trigger scrape")
        print("Step 18: Scrape triggered")

        # Step 19: Assert MQTT scores message
        wait_for_message(scores_queue, timeout=10)
        print("Step 19: MQTT scores/updated message received")

        # Step 20: Assert at least one member has a non-zero score
        resp = api("GET", f"/api/leagues/{league_id}/standings/breakdown", "smoke-admin")
        assert_status(resp, 200, "Standings breakdown")
        breakdown = resp.json()
        has_score = any(member.get("totalScore", 0) > 0 for member in breakdown)
        assert has_score, f"Expected non-zero score in standings, got: {breakdown}"
        print("Step 20: Standings confirmed non-zero — smoke test PASSED ✓")
```

- [ ] **Step 6: Add cleanup (`finally`) and entry point**

Append outside `main()` — after the `try/finally`:

```python
    finally:
        # Step 21: Always clean up, even on failure
        print("Step 21: Cleaning up...")
        if http_server_proc is not None:
            http_server_proc.terminate()
            http_server_proc.wait()
        if mqtt is not None:
            mqtt.loop_stop()
            mqtt.disconnect()
        cleanup_sql = SCRIPT_DIR / "cleanup.sql"
        result = subprocess.run(
            ["psql", DB_URL, "-f", str(cleanup_sql)],
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            print(f"Warning: cleanup.sql had errors:\n{result.stderr}")
        else:
            print("Step 21: Cleanup complete")


if __name__ == "__main__":
    main()
```

- [ ] **Step 7: Verify the script is syntactically valid**

```bash
python -m py_compile scripts/smoke/smoke_test.py
echo "Syntax OK"
```

Expected: `Syntax OK` with no output from py_compile.

- [ ] **Step 8: Commit**

```
git add scripts/smoke/smoke_test.py
git commit -m "test: add smoke_test.py orchestrator (21-step API happy path)"
```

---

## Task 6: GitHub Actions Workflow

**Files:**
- Create: `.github/workflows/smoke.yml`

- [ ] **Step 1: Create `.github/workflows/smoke.yml`**

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
          - ${{ github.workspace }}/scripts/smoke/mosquitto.conf:/mosquitto/config/mosquitto.conf

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - uses: actions/setup-python@v5
        with:
          python-version: '3.12'

      - name: Install Python dependencies
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

      - name: Wait for API to be ready
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

- [ ] **Step 2: Commit**

```
git add .github/workflows/smoke.yml
git commit -m "ci: add smoke test GitHub Actions workflow"
```

---

## Running Locally

```bash
# Start infra only
docker compose up db mqtt -d

# Start API
dotnet run --project DCF.Api/DCF.Api.csproj

# In another terminal
pip install -r scripts/smoke/requirements.txt
SMOKE_DB_URL="postgresql://postgres:postgres@localhost:5432/dcf" \
  python scripts/smoke/smoke_test.py

# If the test was interrupted — manual cleanup
psql postgresql://postgres:postgres@localhost:5432/dcf -f scripts/smoke/cleanup.sql
```

**Note for Windows:** If `psql` isn't in your PATH, either install PostgreSQL client tools or run the cleanup step from a WSL terminal.

---

## Self-Review

**Spec coverage:**
- ✅ Task 1: IHtmlFetcher refactor (spec Part 1)
- ✅ Task 2: sample-recap.html with full sub-caption format (spec Step 2, fixture HTML section)
- ✅ Task 3: cleanup.sql + mosquitto.conf (spec Steps 3–4)
- ✅ Task 4: requirements.txt (spec Step 6)
- ✅ Task 5: smoke_test.py covering all 21 steps (spec Step 5)
- ✅ Task 6: smoke.yml (spec Step 7)

**Placeholders:** None.

**Type consistency:**
- `CAPTIONS = [0, 3, 8]` — matches `ComputedCaption.GeneralEffectCombined=0`, `VisualCombined=3`, `MusicCombined=8`
- `assert_status(resp, 200, ...)` / `assert_status(resp, 204, ...)` — verified against actual controller return codes
- `resp.json()["id"]` — `POST /api/auth/me` returns `{id}`, `POST /api/admin/corps` returns `{id}`, etc.
- `draft_state.get("currentDrafterId")` — `PublishDraftStateAsync` outputs `CurrentDrafterId` (camelCase after JSON serialization)
- `draft_state.get("status")` — `PublishDraftStateAsync` outputs `Status`
- `member.get("totalScore", 0)` — `MemberScoreBreakdown` has `TotalScore` (camelCase: `totalScore`)
