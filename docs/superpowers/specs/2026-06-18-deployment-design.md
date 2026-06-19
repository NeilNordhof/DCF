---
name: deployment-design
description: Production deployment of DCF to drumcorpsfantasy.net — VPS + Cloudflare + GitHub Actions
metadata:
  type: project
---

# DCF Deployment Design

**Domain:** drumcorpsfantasy.net (registered on Cloudflare)
**Stack:** ASP.NET Core 10 API + React 19 SPA + PostgreSQL 16 + Mosquitto MQTT
**Hosting:** Hetzner CX22 VPS (~€4/mo) + Cloudflare DNS proxy

## Architecture

```
Browser
  │
  ├─ https://drumcorpsfantasy.net       → Cloudflare (CDN + SSL)
  ├─ https://api.drumcorpsfantasy.net   → Cloudflare (proxy)
  └─ wss://mqtt.drumcorpsfantasy.net    → Cloudflare (WebSocket proxy)
                │
          Hetzner CX22 VPS
                │
          nginx container (ports 80/443)
                │
          ┌─────┼──────────────────────┐
          ↓     ↓                      ↓
       web:80  api:8080         mosquitto:9001
    (React SPA)  (.NET API)     (MQTT WebSocket)
                   │
            postgres:5432
```

Three subdomains, one VPS. Only ports 22, 80, and 443 are open on the VPS firewall. PostgreSQL and raw MQTT ports (1883, 5432) are internal to the Docker network only.

**SSL:** Cloudflare terminates SSL for browsers. For the Cloudflare → VPS leg, a Cloudflare Origin CA certificate (free, 15-year validity) is installed in the nginx container. Cloudflare SSL/TLS mode is set to **Full (Strict)** — Cloudflare trusts its own Origin CA, so Strict mode works and is more secure than plain Full.

**MQTT WebSocket:** Mosquitto's WebSocket listener (port 9001) is proxied by nginx under `mqtt.drumcorpsfantasy.net`. Cloudflare proxies WebSocket on port 443, so the React frontend connects as `wss://mqtt.drumcorpsfantasy.net`.

## Production Docker Compose (`docker-compose.prod.yml`)

Key differences from the dev compose:

- Images pulled from GitHub Container Registry (GHCR) — no building on the VPS
- Mailpit removed — Resend SMTP used for email
- PostgreSQL and Mosquitto have no exposed ports (internal Docker network only)
- nginx container added, the only service exposing ports 80 and 443
- All services use `restart: unless-stopped`
- Persistent named volumes for PostgreSQL data and API uploads

### Services

| Service | Image | Exposed |
|---|---|---|
| postgres | postgres:16 | Internal only |
| mosquitto | eclipse-mosquitto:2 | Internal only |
| api | ghcr.io/neilnordhof/dcf-api:latest | Internal only (port 8080) |
| web | ghcr.io/neilnordhof/dcf-web:latest | Internal only (port 80) |
| nginx | nginx:alpine | 80, 443 |

### Secrets (`.env.prod` on VPS, never committed)

```
DB_PASSWORD=
RESEND_API_KEY=
UNSUBSCRIBE_SECRET=
AUTH0_DOMAIN=
AUTH0_AUDIENCE=
```

### API Environment Variables (production)

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Default=Host=postgres;Port=5432;Database=dcf;Username=dcf;Password=<DB_PASSWORD>
Auth0__Domain=<AUTH0_DOMAIN>
Auth0__Audience=<AUTH0_AUDIENCE>
Mqtt__Host=mosquitto
Mqtt__Port=1883
Email__Host=smtp.resend.com
Email__Port=587
Email__Username=resend
Email__Password=<RESEND_API_KEY>
Email__StartTls=true
Email__FromAddress=notifications@drumcorpsfantasy.net
Email__FromName=Drum Corps Fantasy
Email__FrontendUrl=https://drumcorpsfantasy.net
Email__UnsubscribeSecret=<UNSUBSCRIBE_SECRET>
AllowedOrigins=https://drumcorpsfantasy.net
```

### Web Image Build Args (baked in at GitHub Actions build time)

```
VITE_API_URL=https://api.drumcorpsfantasy.net
VITE_MQTT_URL=wss://mqtt.drumcorpsfantasy.net
VITE_AUTH0_DOMAIN=<from GitHub secret>
VITE_AUTH0_CLIENT_ID=<from GitHub secret>
VITE_AUTH0_AUDIENCE=<from GitHub secret>
```

## nginx Configuration (`nginx/nginx.prod.conf`)

Three virtual hosts on port 443 sharing the same Cloudflare Origin CA certificate:

- `drumcorpsfantasy.net` → `proxy_pass http://web:80`
- `api.drumcorpsfantasy.net` → `proxy_pass http://api:8080` with standard forwarded-for headers
- `mqtt.drumcorpsfantasy.net` → `proxy_pass http://mosquitto:9001` with WebSocket upgrade headers (`Upgrade`, `Connection`, `proxy_read_timeout 86400`)

Port 80 redirects to HTTPS.

SSL certificate files mounted from `/etc/ssl/cloudflare/` on the VPS host.

## Database Migrations

The API already calls `db.Database.MigrateAsync()` on startup (`Program.cs:84`). Migrations apply automatically when the API container restarts on deploy — no separate migration step needed.

## CI/CD Pipeline (`.github/workflows/deploy.yml`)

**Trigger:** Runs only after the Smoke Test workflow completes successfully on `master`. A broken push never reaches production.

**Job 1 — Build & Push** (GitHub-hosted runner):
1. Log in to GHCR using the automatic `GITHUB_TOKEN`
2. Build and push `dcf-api` image → `ghcr.io/neilnordhof/dcf-api:latest`
3. Build and push `dcf-web` image with production VITE build args → `ghcr.io/neilnordhof/dcf-web:latest`

**Job 2 — Deploy** (SSH into VPS, runs after Job 1):
1. SSH as the `deploy` user using a key stored in GitHub secrets
2. `docker compose -f docker-compose.prod.yml pull`
3. `docker compose -f docker-compose.prod.yml up -d`
4. `docker image prune -f`

**GHCR package visibility:** GHCR packages published from a public GitHub repo can be set to public in GitHub → Packages settings, allowing the VPS to `docker pull` without authentication. If the repo is private, the deploy step must first `docker login ghcr.io` on the VPS using a Personal Access Token with `read:packages` scope (stored as a GitHub secret).

Total time: ~3–5 minutes from push to live.

### GitHub Actions Secrets Required

| Secret | Purpose |
|---|---|
| `VPS_HOST` | VPS IP address |
| `VPS_SSH_KEY` | Private SSH key for the `deploy` user on the VPS |
| `VITE_AUTH0_DOMAIN` | Auth0 production domain |
| `VITE_AUTH0_CLIENT_ID` | Auth0 production client ID |
| `VITE_AUTH0_AUDIENCE` | Auth0 production audience |

`VITE_API_URL` and `VITE_MQTT_URL` are hardcoded in the workflow file.

## VPS File Layout

```
/srv/dcf/
├── docker-compose.prod.yml
├── .env.prod                  ← secrets, never in git
├── nginx/
│   └── nginx.prod.conf
└── mosquitto/
    └── mosquitto.conf         ← same file as dev (allow_anonymous, persistence)
```

## Production Database Access

Use an SSH tunnel for direct DB access when needed — no extra ports opened:

```bash
ssh -L 5432:localhost:5432 deploy@<VPS_IP>
```

Then connect any DB client to `localhost:5432` as normal. The tunnel is live for the duration of the SSH session.

## One-Time Setup Checklist

### Hetzner VPS
- [ ] Provision CX22 (Ubuntu 24.04 LTS)
- [ ] Install Docker + Docker Compose plugin
- [ ] Create `deploy` user, add to `docker` group, configure SSH key auth
- [ ] Open firewall: ports 22, 80, 443 only
- [ ] Clone repo to `/srv/dcf`
- [ ] Create `/srv/dcf/.env.prod` with all secrets
- [ ] Place Cloudflare Origin CA cert at `/etc/ssl/cloudflare/cert.pem` and `/etc/ssl/cloudflare/key.pem`

### Cloudflare
- [ ] Add A records for `drumcorpsfantasy.net`, `api.drumcorpsfantasy.net`, `mqtt.drumcorpsfantasy.net` → VPS IP (all proxied, orange cloud)
- [ ] Set SSL/TLS mode to **Full (Strict)**
- [ ] Generate Cloudflare Origin CA certificate (15-year validity), download cert + key
- [ ] Add Resend SPF, DKIM, and DMARC records (provided by Resend on domain verification)

### Resend
- [ ] Sign up and add `drumcorpsfantasy.net` as a sending domain
- [ ] Verify domain via DNS records in Cloudflare
- [ ] Create API key → add to `.env.prod` as `RESEND_API_KEY`
- [ ] Confirm `notifications@drumcorpsfantasy.net` sends successfully

### Auth0
- [ ] In Auth0 application settings, add `https://drumcorpsfantasy.net` to:
  - Allowed Callback URLs
  - Allowed Logout URLs
  - Allowed Web Origins
- [ ] Confirm production domain/client ID/audience, add to GitHub Actions secrets

### GitHub Actions Secrets
- [ ] `VPS_HOST`
- [ ] `VPS_SSH_KEY`
- [ ] `VITE_AUTH0_DOMAIN`
- [ ] `VITE_AUTH0_CLIENT_ID`
- [ ] `VITE_AUTH0_AUDIENCE`
