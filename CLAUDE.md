# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Code Style

### C#/.NET
Curly Brackets should always start on a new line
Don't use lambdas for methods, even if its a one line method
Wrap code blocks with curly brackets, even if they are one line (if/foreach/using/etc)
1 line of padding before return statements
1 line of padding before and after code blocks (if/foreach/using/etc)
1 line of padding before and after await statements
never have more than 1 blank line in a row.

### Javascript
Nothing defined for now.

## Claude Code Behaviour Guidelines

- Avoid ownership-dodging behaviour: if you encounter an issue, take responsibility for it and work towards a solution instead of passing it on to someone else. Don't say things like "not caused by my changes" or say that it's "a pre-existing issue". Instead, acknowledge the problem and take initiative to fix it. Also, don't give up with excuses like "known limitation" and don't mark it for "future work".
- Avoid premature stopping: if you encounter a problem, don't stop at the first obstacle. Instead, keep pushing forward and find a way to overcome it. Don't say things like "good stopping point" or "natural checkpoint". Instead, keep going until you have a complete solution.
- Avoid permission-seeking behaviour: if you have the knowledge and capability to solve a problem, push through. Don't say things like "should I continue?" or "want me to keep going?". Instead, take initiative and act towards the solution.
- Do plan multi-step approaches before acting (plan which files to read and in what order, which tools to use, etc).
- Do recall and apply project-specific conventions from CLAUDE.md files.
- Do catch your own mistakes by applying reasoning loops and self-checks, and fix them before committing or asking for help.


### Code Quality
- Prefer correct, complete implementations over minimal ones.
- Use appropriate data structures and algorithms — don't brute-force what has a known better solution.
- When fixing a bug, fix the root cause, not the symptom.
- If something I asked for requires error handling or validation to work reliably, include it without asking.
- "correct, complete over minimal" — directly counters the "simplest approach first" default without saying "write more code." It's a quality signal, not a quantity signal.


### Use of tools

Adhere to the following guidelines when using tools:

- Always use a **Research-First approach**: Before using any tool, conduct thorough research to understand the context and requirements. This ensures that you use the most appropriate tool for the task at hand. Never use an Edit-First approach. You should prefer making surgical edits to the codebase instead of rewriting whole files or doing large, sweeping changes.
- Use **Reasoning Loops** very frequently. Don't be lazy and skip them. Reasoning loops are essential for ensuring the quality and accuracy of your work.


### Thinking Depth

When working on tasks that require complex problem-solving, always apply the highest **level of thinking depth**.

When thinking is shallow, the model outputs to the cheapest action available. We don't want that. We don't mind consuming more tokens if it means a better output. So always apply the highest level of thinking depth.

Never reason from assumptions, always reason from the actual data. You need to read and understand the actual code, publication or documentation in order to make informed decisions. Don't rely on assumptions or guesses, as they can lead to mistakes and misunderstandings.

## Commands

### Backend (.NET 10)
```bash
dotnet build DCF.slnx
dotnet run --project DCF.Api/DCF.Api.csproj
dotnet test DCF.Tests/DCF.Tests.csproj
dotnet test --filter "FullyQualifiedName~DraftServiceTests"  # single test class
```

### Frontend (DCF.Web/)
```bash
npm run dev      # Vite dev server on port 5173
npm run build    # tsc -b && vite build
npm run lint     # ESLint
```

### Infrastructure
```bash
docker compose up -d         # PostgreSQL + Mosquitto + API
docker compose up db mqtt    # infra only, run API locally
```

## Architecture

**Stack:** ASP.NET Core Web API (.NET 10) + React 19 SPA + PostgreSQL 16 + Mosquitto MQTT

```
DCF.Web (React + Vite + TypeScript)
    ↓ REST + Auth0 JWT
DCF.Api (ASP.NET Core)
    ↓ EF Core / Npgsql
DCF.Data (entities, DcfDbContext)
    ↓
PostgreSQL
MQTT Broker (Mosquitto) ← API publishes → React subscribes via WebSocket
```

**Projects:**
- `DCF.Data` — EF Core entities and `DcfDbContext`. All database schema lives here.
- `DCF.Api` — Controllers, services, background services, Dockerfile.
- `DCF.Tests` — xUnit tests; uses EF Core InMemory for integration tests.
- `DCF.Web` — Vite + React SPA with Auth0, MQTT.js, and a typed fetch wrapper at `src/api/client.ts`.

## Key Patterns

**Authentication:** Auth0 JWT Bearer. Users are auto-registered on first `/api/auth/me` call. `IsAdmin` is set directly in the database — there are no Auth0 roles.

**Real-time:** Background services publish JSON to Mosquitto over TCP (`MqttService`). The React frontend subscribes via WebSocket (port 9001) using the `useMqtt` hook. Topics: `dcf/leagues/{leagueId}/draft`, `dcf/scores/updated`.

**Draft logic (snake draft):** `DraftService.GetCurrentDrafter` is a static method that computes whose turn it is from `CurrentPickNumber` and `DraftOrderJson`. The snake reversal happens every `CorpsPerCaption * captionCount` picks. A unique constraint on `(LeagueId, CorpsId, Caption)` enforces no duplicate picks.

**Standings:** Scores are computed on read in `StandingsService` — there is no materialized fantasy score table. For each user and caption, it averages the `TotalScore` from their drafted corps' latest `ScoreEntity` rows.

**Background scheduling:** `ScrapeSchedulerService` and `DraftSchedulerService` both use `Task.Delay` with a `ConcurrentDictionary` to schedule future work. `ScrapeSchedulerService` loads all unscraped shows at startup and re-schedules new ones as shows are created.

## Configuration

**API** (`appsettings.json` / environment variables):
- `ConnectionStrings__Default` — Npgsql connection string
- `Auth0__Domain`, `Auth0__Audience`
- `Mqtt__Host`, `Mqtt__Port` (default 1883)
- `Scraper__DelayMinutes` — buffer after `ScoresAnnouncedTime` before scraping
- `AllowedOrigins` — CORS

**Frontend** (`.env` / Vite env vars):
- `VITE_API_URL`, `VITE_MQTT_URL` (WebSocket, e.g. `ws://localhost:9001`)
- `VITE_AUTH0_DOMAIN`, `VITE_AUTH0_AUDIENCE`

## Scores Scraping

`IRecapScraperTask` (implemented in `RecapScraperTask`) fetches the DCI recap HTML for a `ShowEntity.Url` using HtmlAgilityPack. Scraped results populate `ScoreEntity` rows. Manual trigger: `POST /api/admin/shows/{id}/scrape`.
