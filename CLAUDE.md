# CLAUDE.md

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
