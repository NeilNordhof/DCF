# DCF Production Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy DCF to drumcorpsfantasy.net on a Hetzner VPS behind Cloudflare, with fully automated deploys via GitHub Actions on every passing push to master.

**Architecture:** A Hetzner CX22 VPS runs all five services (postgres, mosquitto, api, web, nginx) via `docker-compose.prod.yml`. nginx reverse-proxies three subdomains. Cloudflare sits in front for SSL termination and CDN — the VPS only needs ports 22, 80, and 443 open. GitHub Actions builds Docker images on GHCR and SSHes into the VPS to restart containers.

**Tech Stack:** Docker Compose, nginx:alpine, Cloudflare (Origin CA, DNS proxy, Full Strict SSL), GitHub Actions (GHCR, `docker/build-push-action@v6`, `appleboy/ssh-action@v1`), Resend (SMTP via smtp.resend.com:587), Hetzner Cloud, Auth0.

## Global Constraints

- VPS: Hetzner CX22, Ubuntu 24.04 LTS, 2 vCPU, 4 GB RAM
- Domain: drumcorpsfantasy.net (already registered on Cloudflare)
- Subdomains: `drumcorpsfantasy.net`, `api.drumcorpsfantasy.net`, `mqtt.drumcorpsfantasy.net`
- GHCR image names: `ghcr.io/neilnordhof/dcf-api:latest`, `ghcr.io/neilnordhof/dcf-web:latest`
- VPS deploy user: `deploy` — app root at `/srv/dcf`
- SSL cert on VPS: `/etc/ssl/cloudflare/cert.pem` and `/etc/ssl/cloudflare/key.pem`
- All runtime secrets in `/srv/dcf/.env.prod` on the VPS (never committed to git)
- Firewall: ports 22, 80, 443 only — postgres (5432) and raw MQTT (1883, 9001) internal only
- Email from: `notifications@drumcorpsfantasy.net` via Resend SMTP

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `docker-compose.prod.yml` | Create | Production service definitions — GHCR images, no build, no Mailpit |
| `nginx/nginx.prod.conf` | Create | Three virtual hosts: web, API, MQTT WebSocket |
| `.github/workflows/deploy.yml` | Create | Build images to GHCR then SSH-deploy to VPS on smoke-test pass |
| `.gitignore` | Modify | Add `.env.prod` |

---

## Task 1: Production Docker Compose, nginx Config, and Deploy Workflow

**Files:**
- Create: `docker-compose.prod.yml`
- Create: `nginx/nginx.prod.conf`
- Create: `.github/workflows/deploy.yml`
- Modify: `.gitignore`

---

- [ ] **Step 1: Create `docker-compose.prod.yml`**

Create this file at the repo root:

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: dcf
      POSTGRES_USER: dcf
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    restart: unless-stopped
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U dcf -d dcf"]
      interval: 5s
      timeout: 5s
      retries: 10

  mosquitto:
    image: eclipse-mosquitto:2
    volumes:
      - ./mosquitto/mosquitto.conf:/mosquitto/config/mosquitto.conf
    restart: unless-stopped

  api:
    image: ghcr.io/neilnordhof/dcf-api:latest
    volumes:
      - uploads:/app/uploads
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
      mosquitto:
        condition: service_started
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Default=Host=postgres;Port=5432;Database=dcf;Username=dcf;Password=${DB_PASSWORD}
      - Auth0__Domain=${AUTH0_DOMAIN}
      - Auth0__Audience=${AUTH0_AUDIENCE}
      - Mqtt__Host=mosquitto
      - Mqtt__Port=1883
      - Email__Host=smtp.resend.com
      - Email__Port=587
      - Email__Username=resend
      - Email__Password=${RESEND_API_KEY}
      - Email__StartTls=true
      - Email__FromAddress=notifications@drumcorpsfantasy.net
      - Email__FromName=Drum Corps Fantasy
      - Email__FrontendUrl=https://drumcorpsfantasy.net
      - Email__UnsubscribeSecret=${UNSUBSCRIBE_SECRET}
      - AllowedOrigins=https://drumcorpsfantasy.net

  web:
    image: ghcr.io/neilnordhof/dcf-web:latest
    restart: unless-stopped
    depends_on:
      - api

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx/nginx.prod.conf:/etc/nginx/conf.d/default.conf:ro
      - /etc/ssl/cloudflare:/etc/ssl/cloudflare:ro
    depends_on:
      - api
      - web
    restart: unless-stopped

volumes:
  postgres_data:
  uploads:
```

- [ ] **Step 2: Validate the compose file syntax**

```bash
docker compose -f docker-compose.prod.yml config
```

Expected: YAML output of the resolved config with no errors. (Will show unresolved `${DB_PASSWORD}` etc. as empty — that's fine, the file isn't run locally.)

- [ ] **Step 3: Create `nginx/nginx.prod.conf`**

Create the `nginx/` directory at the repo root, then create this file:

```nginx
server {
    listen 80;
    server_name drumcorpsfantasy.net api.drumcorpsfantasy.net mqtt.drumcorpsfantasy.net;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name drumcorpsfantasy.net;

    ssl_certificate /etc/ssl/cloudflare/cert.pem;
    ssl_certificate_key /etc/ssl/cloudflare/key.pem;

    location / {
        proxy_pass http://web:80;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}

server {
    listen 443 ssl;
    server_name api.drumcorpsfantasy.net;

    ssl_certificate /etc/ssl/cloudflare/cert.pem;
    ssl_certificate_key /etc/ssl/cloudflare/key.pem;

    location / {
        proxy_pass http://api:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}

server {
    listen 443 ssl;
    server_name mqtt.drumcorpsfantasy.net;

    ssl_certificate /etc/ssl/cloudflare/cert.pem;
    ssl_certificate_key /etc/ssl/cloudflare/key.pem;

    location / {
        proxy_pass http://mosquitto:9001;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_read_timeout 86400;
    }
}
```

- [ ] **Step 4: Create `.github/workflows/deploy.yml`**

```yaml
name: Deploy

on:
  workflow_run:
    workflows: ["Smoke Test"]
    types: [completed]
    branches: [master]
  workflow_dispatch:

jobs:
  build-and-push:
    if: ${{ github.event_name == 'workflow_dispatch' || github.event.workflow_run.conclusion == 'success' }}
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push API image
        uses: docker/build-push-action@v6
        with:
          context: .
          file: DCF.Api/Dockerfile
          push: true
          tags: ghcr.io/${{ github.repository_owner }}/dcf-api:latest

      - name: Build and push web image
        uses: docker/build-push-action@v6
        with:
          context: ./DCF.Web
          file: DCF.Web/Dockerfile
          push: true
          tags: ghcr.io/${{ github.repository_owner }}/dcf-web:latest
          build-args: |
            VITE_API_URL=https://api.drumcorpsfantasy.net
            VITE_MQTT_URL=wss://mqtt.drumcorpsfantasy.net
            VITE_AUTH0_DOMAIN=${{ secrets.VITE_AUTH0_DOMAIN }}
            VITE_AUTH0_CLIENT_ID=${{ secrets.VITE_AUTH0_CLIENT_ID }}
            VITE_AUTH0_AUDIENCE=${{ secrets.VITE_AUTH0_AUDIENCE }}

  deploy:
    needs: build-and-push
    runs-on: ubuntu-latest

    steps:
      - name: Deploy to VPS
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.VPS_HOST }}
          username: deploy
          key: ${{ secrets.VPS_SSH_KEY }}
          script: |
            cd /srv/dcf
            git pull
            docker compose -f docker-compose.prod.yml pull
            docker compose -f docker-compose.prod.yml up -d
            docker image prune -f
```

Note: `workflow_dispatch` allows manually re-triggering the deploy from GitHub Actions UI — useful for the first deploy, when the VPS may not have been ready when the first push landed.

- [ ] **Step 5: Add `.env.prod` to `.gitignore`**

Open `.gitignore` and add this line (add it near any existing `.env` entries):

```
.env.prod
```

- [ ] **Step 6: Commit and push**

```bash
git add docker-compose.prod.yml nginx/nginx.prod.conf .github/workflows/deploy.yml .gitignore
git commit -m "feat: add production docker-compose, nginx config, and deploy workflow"
git push origin master
```

Expected: The Smoke Test workflow starts on GitHub Actions. The Deploy workflow will also attempt to run after smoke passes — it will fail at the deploy step because the VPS doesn't exist yet. That's expected and fine; you'll manually re-trigger it in Task 8 once the VPS is ready.

---

## Task 2: Provision Hetzner VPS

**Prerequisites:** Hetzner Cloud account (cloud.hetzner.com).

---

- [ ] **Step 1: Create the server**

In the Hetzner Cloud Console:
1. New Project → name it `dcf`
2. Add Server:
   - Location: pick closest to you (e.g. Ashburn, VA for US)
   - Image: Ubuntu 24.04
   - Type: Shared CPU → **CX22** (2 vCPU, 4 GB RAM)
   - SSH Keys: add your local public key (contents of `~/.ssh/id_ed25519.pub` or similar) — this lets you SSH in as `root` initially
   - Name: `dcf-prod`
3. Click Create & Buy

Note the server's public IP address — you'll need it for Cloudflare DNS and GitHub secrets.

- [ ] **Step 2: SSH in as root and harden**

```bash
ssh root@<VPS_IP>
```

Run these commands on the server:

```bash
apt update && apt upgrade -y
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status
```

Expected `ufw status` output:
```
Status: active
To                         Action      From
--                         ------      ----
22/tcp                     ALLOW       Anywhere
80/tcp                     ALLOW       Anywhere
443/tcp                    ALLOW       Anywhere
```

- [ ] **Step 3: Install Docker**

```bash
curl -fsSL https://get.docker.com | sh
```

Expected: Docker installs, ends with a message about running `docker run hello-world`.

- [ ] **Step 4: Create the `deploy` user**

On the server (still as root):

```bash
useradd -m -s /bin/bash deploy
usermod -aG docker deploy
mkdir -p /home/deploy/.ssh
chmod 700 /home/deploy/.ssh
chown deploy:deploy /home/deploy/.ssh
```

- [ ] **Step 5: Generate a dedicated SSH key pair for GitHub Actions (run this on your local machine, not the server)**

```bash
ssh-keygen -t ed25519 -C "dcf-github-deploy" -f ~/.ssh/dcf_deploy -N ""
```

This creates:
- `~/.ssh/dcf_deploy` — private key (goes into GitHub secret `VPS_SSH_KEY`)
- `~/.ssh/dcf_deploy.pub` — public key (goes onto the VPS)

- [ ] **Step 6: Install the public key on the VPS**

Back on the server (as root):

```bash
nano /home/deploy/.ssh/authorized_keys
```

Paste the contents of `~/.ssh/dcf_deploy.pub` and save. Then:

```bash
chmod 600 /home/deploy/.ssh/authorized_keys
chown deploy:deploy /home/deploy/.ssh/authorized_keys
```

- [ ] **Step 7: Verify SSH access as `deploy`**

On your local machine:

```bash
ssh -i ~/.ssh/dcf_deploy deploy@<VPS_IP>
```

Expected: You get a shell prompt as `deploy@dcf-prod`. Type `exit` to disconnect.

- [ ] **Step 8: Create the app directory**

On the server (SSH in as `deploy`):

```bash
sudo mkdir -p /srv/dcf
sudo chown deploy:deploy /srv/dcf
```

---

## Task 3: Cloudflare DNS and SSL

**Prerequisites:** Task 2 complete (you have the VPS IP). Cloudflare account with drumcorpsfantasy.net already added.

---

- [ ] **Step 1: Add DNS A records**

In Cloudflare dashboard → drumcorpsfantasy.net → DNS → Records → Add record:

Add all three, each pointing to the VPS IP, each with **Proxy status: Proxied** (orange cloud):

| Type | Name | IPv4 address | Proxy status |
|---|---|---|---|
| A | `@` (or `drumcorpsfantasy.net`) | `<VPS_IP>` | Proxied |
| A | `api` | `<VPS_IP>` | Proxied |
| A | `mqtt` | `<VPS_IP>` | Proxied |

- [ ] **Step 2: Set SSL/TLS mode to Full (Strict)**

In Cloudflare dashboard → drumcorpsfantasy.net → SSL/TLS → Overview:

Select **Full (strict)**.

- [ ] **Step 3: Generate Cloudflare Origin CA certificate**

In Cloudflare dashboard → SSL/TLS → Origin Server → Create Certificate:

- Key type: RSA (2048)
- Hostnames: `drumcorpsfantasy.net`, `*.drumcorpsfantasy.net`
- Certificate Validity: 15 years

Click Create. You'll see two values: the certificate and the private key. **Copy both now — the private key is only shown once.**

- [ ] **Step 4: Install the cert on the VPS**

SSH in as `deploy`:

```bash
sudo mkdir -p /etc/ssl/cloudflare
sudo nano /etc/ssl/cloudflare/cert.pem
```

Paste the certificate content (begins with `-----BEGIN CERTIFICATE-----`), save.

```bash
sudo nano /etc/ssl/cloudflare/key.pem
```

Paste the private key content (begins with `-----BEGIN PRIVATE KEY-----`), save.

```bash
sudo chmod 644 /etc/ssl/cloudflare/cert.pem
sudo chmod 600 /etc/ssl/cloudflare/key.pem
```

- [ ] **Step 5: Verify cert files exist with correct permissions**

```bash
ls -la /etc/ssl/cloudflare/
```

Expected:
```
-rw-r--r-- 1 root root  XXXX cert.pem
-rw------- 1 root root  XXXX key.pem
```

---

## Task 4: Resend Email Setup

**Prerequisites:** Task 3 complete (DNS records can be added to Cloudflare).

---

- [ ] **Step 1: Create Resend account and add domain**

1. Sign up at resend.com
2. Go to Domains → Add Domain
3. Enter `drumcorpsfantasy.net`
4. Resend will show you DNS records to add (SPF, DKIM, DMARC)

- [ ] **Step 2: Add Resend DNS records to Cloudflare**

In Cloudflare dashboard → drumcorpsfantasy.net → DNS, add each record Resend provides. They will look something like:

| Type | Name | Value |
|---|---|---|
| TXT | `@` | `v=spf1 include:amazonses.com ~all` |
| CNAME | `resend._domainkey` | `resend._domainkey.resend.com` |
| TXT | `_dmarc` | `v=DMARC1; p=none;` |

Add each one exactly as Resend specifies. Click Verify in Resend after adding — DNS propagation can take a few minutes.

- [ ] **Step 3: Create Resend API key**

In Resend dashboard → API Keys → Create API Key:
- Name: `dcf-prod`
- Permission: Sending access
- Domain: drumcorpsfantasy.net

Copy the key — it's shown only once. Keep it ready for Task 7 (`.env.prod`).

---

## Task 5: Auth0 Production Configuration

**Prerequisites:** None (this is dashboard-only).

---

- [ ] **Step 1: Update allowed URLs in Auth0**

In Auth0 dashboard → Applications → your DCF application → Settings:

Add `https://drumcorpsfantasy.net` to each of these fields (keeping existing dev values, comma-separated):

- **Allowed Callback URLs:** `https://drumcorpsfantasy.net`
- **Allowed Logout URLs:** `https://drumcorpsfantasy.net`
- **Allowed Web Origins:** `https://drumcorpsfantasy.net`

Click Save Changes.

- [ ] **Step 2: Note production credentials**

From the same Settings page, copy:
- **Domain** (e.g. `dcf-dev.us.auth0.com`) → becomes `VITE_AUTH0_DOMAIN`
- **Client ID** → becomes `VITE_AUTH0_CLIENT_ID`
- **API Audience** (from APIs section) → becomes `VITE_AUTH0_AUDIENCE`

Keep these ready for Task 6.

---

## Task 6: GitHub Actions Secrets

**Prerequisites:** Task 2 (VPS IP + SSH key), Task 4 (Resend API key), Task 5 (Auth0 credentials).

---

- [ ] **Step 1: Add all secrets**

In GitHub → your DCF repo → Settings → Secrets and variables → Actions → New repository secret:

Add each of the following:

| Name | Value |
|---|---|
| `VPS_HOST` | Your VPS IP address |
| `VPS_SSH_KEY` | Contents of `~/.ssh/dcf_deploy` (the private key — the entire file including `-----BEGIN...` and `-----END...` lines) |
| `VITE_AUTH0_DOMAIN` | Auth0 domain (e.g. `dcf-dev.us.auth0.com`) |
| `VITE_AUTH0_CLIENT_ID` | Auth0 client ID |
| `VITE_AUTH0_AUDIENCE` | Auth0 audience URL |

- [ ] **Step 2: Verify secrets are saved**

In GitHub → Settings → Secrets and variables → Actions, confirm all 5 secrets appear in the list (values are hidden, that's expected).

---

## Task 7: VPS Initialization

**Prerequisites:** Tasks 2–6 complete. The deploy workflow must have run at least once on master (from the push in Task 1) so GHCR packages exist.

---

- [ ] **Step 1: Make GHCR packages public**

After the first deploy workflow run in Task 1, GHCR packages are created but private by default. Make them public so the VPS can pull without auth:

1. GitHub → your profile → Packages → `dcf-api` → Package settings → Change package visibility → Public → confirm
2. GitHub → your profile → Packages → `dcf-web` → Package settings → Change package visibility → Public → confirm

If you don't see the packages yet, wait for the Task 1 deploy workflow build job to complete first.

- [ ] **Step 2: Clone the repo to the VPS**

SSH in as `deploy`:

```bash
ssh deploy@<VPS_IP>
cd /srv/dcf
git clone https://github.com/NeilNordhof/DCF.git .
```

- [ ] **Step 3: Create `.env.prod`**

Still SSH'd in as `deploy`:

```bash
nano /srv/dcf/.env.prod
```

Paste and fill in all values:

```
DB_PASSWORD=<choose a strong random password>
RESEND_API_KEY=<from Task 4>
UNSUBSCRIBE_SECRET=<choose a random string, e.g. output of: openssl rand -hex 32>
AUTH0_DOMAIN=<from Task 5, e.g. dcf-dev.us.auth0.com>
AUTH0_AUDIENCE=<from Task 5, e.g. https://dcf-dev.us.auth0.com/api/v2/>
```

Save the file. Verify it's not world-readable:

```bash
chmod 600 /srv/dcf/.env.prod
```

---

## Task 8: First Deploy and Verification

**Prerequisites:** All previous tasks complete.

---

- [ ] **Step 1: Trigger the deploy workflow manually**

In GitHub → your DCF repo → Actions → Deploy → Run workflow → Run workflow (on master).

This re-runs the build-and-push + deploy jobs. Watch it in the Actions tab — it takes 3–5 minutes.

- [ ] **Step 2: Verify all containers are running on the VPS**

SSH in as `deploy`:

```bash
cd /srv/dcf
docker compose -f docker-compose.prod.yml ps
```

Expected: all five services (`postgres`, `mosquitto`, `api`, `web`, `nginx`) show `running` status.

If any container shows `exited`, check logs:

```bash
docker compose -f docker-compose.prod.yml logs <service-name> --tail 50
```

- [ ] **Step 3: Verify the web frontend loads**

In a browser, navigate to `https://drumcorpsfantasy.net`.

Expected: The DCF login page loads with HTTPS (padlock in browser). No certificate errors.

- [ ] **Step 4: Verify the API responds**

```bash
curl -s https://api.drumcorpsfantasy.net/api/seasons/active
```

Expected: A JSON response (either a season object or a 404 — either confirms the API is up and nginx routing works).

- [ ] **Step 5: Verify the MQTT WebSocket endpoint**

Open the browser devtools console on `https://drumcorpsfantasy.net` and run:

```javascript
const ws = new WebSocket('wss://mqtt.drumcorpsfantasy.net');
ws.onopen = () => console.log('MQTT WebSocket connected');
ws.onerror = (e) => console.error('MQTT error', e);
```

Expected: `MQTT WebSocket connected` logged within a second or two.

- [ ] **Step 6: Verify login works end-to-end**

In the browser, click the login button on `https://drumcorpsfantasy.net`. Complete the Auth0 login flow.

Expected: You're redirected back to the app and authenticated. If Auth0 throws a callback error, double-check that `https://drumcorpsfantasy.net` was saved in the allowed callback URLs in Task 5.

- [ ] **Step 7: Verify automated deploys work**

Make a trivial change locally (e.g. update a comment), push to master:

```bash
git commit --allow-empty -m "chore: trigger deploy verification"
git push origin master
```

Watch GitHub Actions → the Smoke Test runs, then the Deploy workflow runs automatically after it passes. After ~5 minutes, the change is live.

Expected: Deploy workflow completes with green checkmarks on all steps.

---

## Production Database Access (Reference)

To connect a DB client to the production database, open an SSH tunnel on your local machine:

```bash
ssh -i ~/.ssh/dcf_deploy -L 5432:localhost:5432 deploy@<VPS_IP>
```

Then connect any DB client (DBeaver, TablePlus, psql) to:
- Host: `localhost`
- Port: `5432`
- Database: `dcf`
- User: `dcf`
- Password: value of `DB_PASSWORD` from `.env.prod`

The tunnel stays open as long as the SSH session is active.
