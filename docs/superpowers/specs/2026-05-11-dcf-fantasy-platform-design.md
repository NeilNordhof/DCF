# DCF Fantasy Platform — Design Spec
**Date:** 2026-05-11

---

## Overview

A fantasy platform for Drum Corps International (DCI). Users join leagues, draft corps+caption combos via a live snake draft, and earn points based on real DCI scores scraped from dci.org throughout the season. The person with the highest score at the end of the season wins their league.

---

## Architecture

Four .NET 10 projects in one Visual Studio solution, plus a React SPA and supporting services.

### Projects

| Project | Type | Purpose |
|---|---|---|
| `DCF.Data` | Class library | EF Core models, DbContext, migrations |
| `DCF.ScoreScraper` | Class library (converted from executable) | HTML scraping logic for dci.org recap pages |
| `DCF.Api` | ASP.NET Core Web API | REST endpoints, Auth0 JWT validation, background scheduler, MQTT publisher |
| `DCF.Web` | Vite + React SPA | Frontend — REST client + MQTT WebSocket subscriber |

### Supporting Services (Docker Compose)

- **PostgreSQL** — single database
- **Mosquitto** (or EMQX) — MQTT broker with WebSocket listener enabled
- **Auth0** — social login (Google, Discord, Facebook) + OTP email passwordless; no passwords stored

---

## Data Model

### Season
- `Id` (Guid)
- `Year` (int)
- `IsActive` (bool)

### Corps *(existing)*
- `Id` (Guid)
- `Name` (string)

### Show *(existing — add SeasonId and ScoresAnnouncedTime)*
- `Id` (Guid)
- `Name` (string)
- `URL` (string) — recap page URL on dci.org
- `Date` (DateTime)
- `SeasonId` (Guid FK)
- `ScoresAnnouncedTime` (DateTimeOffset) — admin-set; used to schedule scrape jobs
- `ShowCorps` join table: `ShowId` + `CorpsId` — which corps are competing at this show

### Caption *(existing enum)*
`GeneralEffect, GeneralEffectMusic, GeneralEffectVisual, Visual, VisualAnalysis, VisualProficiency, ColorGuard, Music, Brass, MusicAnalysis, Percussion, Penalty`

### Score *(existing)*
- `Id` (Guid)
- `CorpsId` (Guid FK)
- `ShowId` (Guid FK)
- `Caption` (enum)
- `Judge` (string?)
- `RepertoireScore` (double)
- `PerformanceScore` (double)
- `TotalScore` (double) — the value used for fantasy scoring
- `RepertoireRank` (int)
- `PerformanceRank` (int)
- `TotalRank` (int)

### User
- `Id` (Guid)
- `Auth0Sub` (string) — Auth0 `sub` claim; primary link to Auth0
- `Email` (string)
- `DisplayName` (string)
- `IsAdmin` (bool) — set manually in DB; not an Auth0 role

### League
- `Id` (Guid)
- `Name` (string)
- `SeasonId` (Guid FK)
- `CommissionerUserId` (Guid FK)
- `IsPublic` (bool)
- `InviteCode` (string) — generated on creation; required to join private leagues
- `CorpsPerCaption` (int) — e.g. 3; number of corps each user drafts per caption
- `DraftableCaptions` (Caption[] stored as JSON) — which captions are in the draft pool
- `DraftStatus` (enum: `NotStarted`, `Scheduled`, `InProgress`, `Completed`)
- `DraftStartTime` (DateTimeOffset?) — if set, draft auto-starts at this time
- `DraftOrderJson` (string) — serialized ordered list of UserIds; set when draft starts
- `CurrentPickNumber` (int) — tracks the overall pick index; used to determine whose turn it is and enforce pick submission

### LeagueMember
- `LeagueId` (Guid FK)
- `UserId` (Guid FK)

### DraftPick
- `Id` (Guid)
- `LeagueId` (Guid FK)
- `UserId` (Guid FK)
- `CorpsId` (Guid FK)
- `Caption` (enum)
- `PickNumber` (int) — overall pick number in the draft
- `RoundNumber` (int)
- Unique constraint on `(LeagueId, CorpsId, Caption)` — each corps+caption can only be drafted once per league

---

## Score Scraper & Admin Flow

### Admin Configuration (React admin panel, `IsAdmin` gated)

1. Create a season (year, mark active)
2. Add world class corps to the season — this is the pool available for drafting
3. Add shows: name, date, recap URL, `ScoresAnnouncedTime`, and select competing corps from the world class list
4. Manually trigger a scrape for any show at any time (for testing or missed scrapes)

### Scheduled Scraping (background service in `DCF.Api`)

- On startup, the background service loads all unscraped shows for the active season
- Schedules a `Task.Delay` until each show's `ScoresAnnouncedTime` plus a configurable buffer (default: 5 minutes; set via `Scraper:DelayMinutes` app setting / environment variable)
- When the timer fires, calls `DCF.ScoreScraper` with the show's URL, parses the recap page, and upserts `Score` rows for corps that appear in `ShowCorps` for that show
- If a show's `ScoresAnnouncedTime` is updated by the admin, the scheduled task is cancelled and rescheduled
- After a successful scrape, publishes a message to MQTT topic `dcf/scores/updated` so connected clients can refresh standings

### Scraper Logic (`DCF.ScoreScraper`)

- Fetches recap HTML from the show URL using `HttpClient`
- Parses each corps' caption scores using HtmlAgilityPack (already a dependency)
- Returns a list of `Score` objects to the API for persistence

---

## Fantasy Engine

### Leagues

- Any authenticated user can create a league (they become commissioner)
- Commissioner configures: name, public/private, `CorpsPerCaption`, `DraftableCaptions`, optional `DraftStartTime`
- Public leagues are browsable by all users; private leagues require the invite code to join
- A league is tied to the active season — new season, new leagues
- Users can be members of multiple leagues

### Draft

**Scheduling & lobby:**
- Commissioner sets an optional `DraftStartTime`. The API background service auto-starts the draft at that time: randomizes member order, sets `DraftStatus` to `InProgress`, publishes opening state to MQTT
- If no `DraftStartTime` is set, the commissioner starts manually from the draft room
- The draft room is accessible to all league members as soon as the league is created
- Before the draft starts, members see a **lobby**: member list, scheduled start time with countdown, draft settings. New members appearing is broadcast in real time via the MQTT draft topic
- When the draft starts, all lobby clients transition directly to the live draft without a page reload — the MQTT payload carries `"status": "inProgress"` to trigger the view switch

**Draft mechanics:**
- Total picks = `CorpsPerCaption × DraftableCaptions.Count × MemberCount`
- Pick order snakes: round 1 goes 1→N, round 2 goes N→1, etc.
- Only the current drafter can submit a pick (API enforces via JWT identity and `CurrentPickNumber`)
- On a valid pick: save `DraftPick`, increment `CurrentPickNumber`, publish updated state to MQTT
- Unique constraint prevents the same corps+caption from being drafted twice in one league
- If a drafter disconnects, the draft pauses; commissioner can advance the pick manually via an "Skip current pick" button in the draft room UI, which calls a commissioner-only API endpoint
- When all picks are made, `DraftStatus` is set to `Completed`

**Stretch goal (not in scope):** Per-pick time limit with auto-pick from a user-submitted wishlist/power rankings.

**MQTT topics:**
- `dcf/leagues/{leagueId}/draft` — lobby state, draft state, pick history, whose turn it is
- `dcf/scores/updated` — broadcast after a successful scrape; clients refresh standings

### Scoring

Computed on read — no materialized fantasy score table.

For each user in a league:
1. For each caption in `DraftableCaptions`, find their `DraftPick` rows
2. For each picked corps, find the most recent `Score` row for that corps+caption (latest show by date)
3. Caption score = average of those `TotalScore` values (up to `CorpsPerCaption` corps)
4. League score = sum across all caption scores
5. Standings endpoint returns all members sorted by league score, recalculated on each request

---

## Auth

**Provider:** Auth0

**Login methods:**
- Social: Google, Discord, Facebook
- Passwordless: user enters email → Auth0 sends 6-digit OTP → user enters code to log in
- No passwords stored anywhere

**API (`DCF.Api`):**
- Validates Auth0 JWTs on all protected endpoints
- On first successful login, checks for existing `User` row by `Auth0Sub`; if none, creates one using email and display name from the token
- `IsAdmin` is set manually in the database

**React (`DCF.Web`):**
- Auth0 React SDK manages login UI and token lifecycle
- Access token attached as `Authorization: Bearer` header on all API requests

---

## Frontend (React + Vite)

### Pages

| Route | Description |
|---|---|
| `/` | Landing page for unauthenticated users; dashboard (leagues + standings snapshot) for authenticated users |
| `/leagues` | Browse public leagues + your active leagues |
| `/leagues/create` | Create a league (name, captions, corps per caption, public/private, draft start time) |
| `/leagues/:id` | League home: standings table, roster view per member, score history by show |
| `/leagues/:id/draft` | Draft room — lobby pre-start, live draft in-progress, completed draft recap |
| `/admin` | Admin panel: season setup, corps list, show management, manual scrape trigger |
| `/profile` | Display name, linked social accounts |

### Key Implementation Details

- **MQTT:** MQTT.js connects to the broker via WebSocket from the draft room page; subscribes to `dcf/leagues/{leagueId}/draft` and `dcf/scores/updated`
- **Auth:** Auth0 React SDK wraps the app; protected routes redirect to login if unauthenticated; admin routes additionally check `isAdmin` from the user profile API response
- **Standings:** Polled on page load and refreshed on `dcf/scores/updated` MQTT message

---

## Stretch Goals

- Per-pick time limit in the draft (requires wishlist/power rankings feature first)
- Wishlist / power rankings: users submit a ranked preference list before the draft; used for auto-pick if timer expires

---

## Open Questions

- Which specific `Caption` enum values are included in the default `DraftableCaptions` for a new league? (Commissioner-configurable, but a sensible default needs to be chosen)
