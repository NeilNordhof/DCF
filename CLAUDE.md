# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET 10)
```bash
dotnet build DCF.slnx
dotnet run --project DCF.Api/DCF.Api.csproj
dotnet test DCF.Tests/DCF.Tests.csproj
dotnet test --filter "FullyQualifiedName~DraftServiceTests"  # single test class

# EF Core migrations (run from repo root)
dotnet ef migrations add <Name> --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
dotnet ef database update --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
```

### Frontend (DCF.Web/)
```bash
npm run dev        # Vite dev server on port 5173
npm run build      # tsc -b && vite build
npm run lint       # ESLint
npm test           # vitest run
npm run test:watch # vitest watch mode
```

### Infrastructure
```bash
docker compose up -d                          # postgres + mailpit + mosquitto + api + web
docker compose up postgres mosquitto mailpit   # infra only, run API/web locally
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
SMTP (Mailpit dev / Resend prod) ← API sends email notifications
```

**Projects:**
- `DCF.Data` — EF Core entities and `DcfDbContext`. All database schema lives here.
- `DCF.Api` — Controllers, services, background services, Dockerfile.
- `DCF.Tests` — xUnit tests; uses EF Core InMemory for integration tests, coverage via coverlet (collected in CI).
- `DCF.Web` — Vite + React SPA with MQTT.js, Vitest/Testing Library, and a typed fetch wrapper at `src/api/client.ts`.

## Key Patterns

**Authentication:** Auth0 JWT Bearer in non-dev environments. Local dev bypasses Auth0 entirely via `DevAuthHandler` (API, swapped in when `IsDevelopment()`) and `DevAuthContext` (frontend, when `import.meta.env.DEV`). In production the frontend does **not** use the `@auth0/auth0-react` SDK — `AuthContext.tsx` drives `Auth0LockPasswordless` directly (email code + Google OAuth), stores the token in `localStorage`, and decodes the `id_token` JWT client-side for user claims, because a custom API audience breaks Auth0's `/userinfo` endpoint. Users are auto-registered on first `/api/auth/me` call. `IsAdmin` is set directly in the database — there are no Auth0 roles.

**Real-time:** Background services publish JSON to Mosquitto over TCP (`MqttService`). The React frontend subscribes via WebSocket (port 9001) using the `useMqtt` hook. Topics: `dcf/leagues/{leagueId}/draft`, `dcf/scores/updated`. `PresenceService` tracks who's connected to a league's draft room in an in-memory `ConcurrentDictionary` (not persisted) and republishes draft state whenever someone joins or leaves.

**Draft logic (snake draft):** `DraftService.GetCurrentDrafter` is a static method that computes whose turn it is from `CurrentPickNumber` and `DraftOrderJson`. The snake reversal happens every `CorpsPerCaption * captionCount` picks. A unique constraint on `(LeagueId, CorpsId, Caption)` enforces no duplicate picks.

**Standings:** Scores are computed on read in `StandingsService` — there is no materialized fantasy score table. It reads the latest `ComputedScoreEntity` per corps per season (one snapshot per show, keyed by show date), averages each member's picks within a caption, then scales by a per-caption weight (`GetWeight`) so overlapping captions don't double-count when a league drafts both a combined caption and its sub-captions (e.g. `VisualCombined` vs. `Visual`/`VisualProficiency`/`VisualAnalysis`/`Colorguard`, or `MusicCombined` vs. `Brass`/`Percussion`/`MusicAnalysis`) — see `GetWeight` for the exact factors.

**Background scheduling:** `ScrapeSchedulerService`, `DraftSchedulerService`, and `SeasonStatusService` all use `Task.Delay` with a `ConcurrentDictionary` to schedule future work, cancelling and rescheduling when the underlying time changes. `ScrapeSchedulerService` loads all unscraped shows at startup and re-schedules new ones as shows are created. `SeasonStatusService` applies any overdue Upcoming→Active→Completed transitions eagerly at startup, then schedules the rest — since `Task.Delay` caps out around 24 days, long waits are chunked in a 20-day loop (`DelayUntilAsync`) rather than a single delay.

**Email notifications:** `IEmailService`/`SmtpEmailService` sends via MailKit — Mailpit in local dev (UI at `http://localhost:8025`, no real delivery) and Resend's SMTP relay in production. Unsubscribe links carry a stateless HMAC-SHA256 token (`EmailTokenService`, format `userId:base64url(hmac)`, constant-time compare) rather than a persisted token table. `POST /api/notifications/unsubscribe` (anonymous) and `PATCH /api/notifications/preferences` (authenticated) both flip `UserEntity.EmailNotificationsEnabled`.

## Configuration

**API** (`appsettings.json` / environment variables):
- `ConnectionStrings__Default` — Npgsql connection string
- `Auth0__Domain`, `Auth0__Audience`
- `Mqtt__Host`, `Mqtt__Port` (default 1883)
- `Scraper__DelayMinutes` — buffer after `ScoresAnnouncedTime` before scraping
- `Scraper__MaxRetries` — number of retries after a scrape failure before giving up and alerting admins (default 5)
- `Scraper__RetryIntervalMinutes` — delay between retries (default 5)
- `Email__*` — SMTP `Host`/`Port`/`Username`/`Password`/`StartTls`, `FromAddress`/`FromName`, `FrontendUrl` (used to build links in emails), `UnsubscribeSecret` (HMAC key for unsubscribe tokens)
- `AllowedOrigins` — CORS

**Frontend** (`.env` in `DCF.Web/` / Vite env vars):
- `VITE_API_URL`, `VITE_MQTT_URL` (WebSocket, e.g. `ws://localhost:9001`)
- `VITE_AUTH0_DOMAIN`, `VITE_AUTH0_CLIENT_ID`, `VITE_AUTH0_AUDIENCE`

## Scraping

Two scraper tasks share a typed `IHtmlFetcher`/`HttpHtmlFetcher` HttpClient (browser-like User-Agent/Accept headers, since DCI blocks bare requests), both built on HtmlAgilityPack:
- `IRecapScraperTask` (`RecapScraperTask`) parses a DCI recap page (`ShowEntity.Url`) into `ScoreEntity` rows. Manual trigger: `POST /api/admin/shows/{id}/scrape`. Progress is tracked via `ShowEntity.ScrapeStatus` (`NotStarted`/`Succeeded`/`Failed`), `LastScrapeAttemptAt`, and `ScrapeError`.
- `IShowInfoScraperTask` (`ShowInfoScraperTask`) parses a DCI *events* page to prefill a new show's location, lat/lng (parsed from the embedded Google Maps link, including a `dir/`-format URL), start/scores-announced time, timezone, exhibition flag, and schedule entries. Surfaced via `GET /api/admin/seasons/{seasonId}/shows/prefill` and consumed by the admin show form's fetch button.

## CI/CD & Deployment

`.github/workflows/ci.yml` runs on every push/PR to `master`: a `tests` job (.NET tests with coverlet coverage, then `npm test`) gates a `smoke` job that boots real Postgres + Mosquitto services, runs the API, and drives `scripts/smoke/smoke_test.py` — an end-to-end HTTP/DB/MQTT check — against it.

`.github/workflows/deploy.yml` fires after a successful CI run on `master` (or manual dispatch): builds and pushes `DCF.Api`/`DCF.Web` images to GHCR, then SSHes into the production VPS to `git pull` and `docker compose -f docker-compose.prod.yml up -d`.

Production (`docker-compose.prod.yml`, live at drumcorpsfantasy.net) adds an `nginx` reverse proxy (`nginx/nginx.prod.conf`) terminating TLS with Cloudflare origin certs in front of `api`/`web`, and points `Email__Host` at Resend's SMTP relay instead of Mailpit.
