# Drum Corps Fantasy (DCF)

A fantasy sports platform for Drum Corps International (DCI) — draft corps into captions, run a live snake draft, and track standings computed from real DCI recap scores throughout the season. Live at [drumcorpsfantasy.net](https://drumcorpsfantasy.net).

## Stack

ASP.NET Core Web API (.NET 10) · React 19 + Vite + TypeScript · PostgreSQL 16 · Mosquitto (MQTT) · Auth0

## Getting started

Prerequisites: .NET 10 SDK, Node 20+, Docker.

Local dev bypasses Auth0 entirely (`DevAuthHandler` / `DevAuthContext`), so no Auth0 tenant is needed to run the app locally.

1. Create `DCF.Web/.env` (gitignored) — only `VITE_API_URL` and `VITE_MQTT_URL` actually matter for local dev, since Auth0 is bypassed:
   ```
   VITE_API_URL=http://localhost:5000
   VITE_MQTT_URL=ws://localhost:9001
   VITE_AUTH0_DOMAIN=
   VITE_AUTH0_CLIENT_ID=
   VITE_AUTH0_AUDIENCE=
   ```
2. Start local infrastructure:
   ```bash
   docker compose up postgres mosquitto mailpit
   ```
3. Run the API — `appsettings.Development.json` already points at the compose stack, and pending EF Core migrations are applied automatically on startup:
   ```bash
   dotnet run --project DCF.Api/DCF.Api.csproj
   ```
4. Run the frontend:
   ```bash
   cd DCF.Web
   npm install
   npm run dev
   ```

The API listens on `http://localhost:5000`, the frontend on `http://localhost:5173`. Outbound email is caught by Mailpit at `http://localhost:8025` instead of being sent anywhere real.

To run the fully containerized stack instead (`docker compose up -d`), populate the root `.env` (gitignored) with `AUTH0_DOMAIN`, `AUTH0_AUDIENCE`, `VITE_AUTH0_DOMAIN`, `VITE_AUTH0_CLIENT_ID`, `VITE_AUTH0_AUDIENCE` — `docker-compose.yml` wires these into the `api` container's environment and the `web` image's Vite build args.

## Testing

```bash
dotnet test DCF.Tests/DCF.Tests.csproj   # backend (xUnit)
cd DCF.Web && npm test                   # frontend (Vitest)
```

`scripts/smoke/smoke_test.py` drives a full HTTP/DB/MQTT smoke test against a real running stack — it's what `.github/workflows/ci.yml` runs after both unit test suites pass, gating the production deploy.

## Project layout

- `DCF.Data` — EF Core entities and migrations
- `DCF.Api` — ASP.NET Core Web API, background services, DCI scrapers
- `DCF.Web` — React SPA
- `DCF.Tests` — xUnit backend tests

See `CLAUDE.md` for architecture details, key patterns, and conventions.
