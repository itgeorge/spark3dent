# Google OAuth Login (Hetzner Deployment)

This document describes the **current login handling** for the Spark3dent web app on Hetzner and how the deployment sets it up.

## Overview

The app is protected by **Google OAuth** so only the business owner (and any listed emails) can access it. The application itself does not implement authentication; Caddy and oauth2-proxy handle it at the reverse-proxy layer.

## Architecture

```
User → Caddy → oauth2-proxy (auth check) → Google login → back to Caddy → ASP.NET app
```

Only authenticated users whose email is in the allowlist can reach the application.

## Request Flow

1. **OAuth endpoints** (`/oauth2/*`): Caddy forwards these directly to oauth2-proxy. This includes `/oauth2/start`, `/oauth2/callback`, and `/oauth2/auth` (used for `forward_auth`).

2. **Protected app routes** (everything else): Caddy runs `forward_auth` against oauth2-proxy. If the user is not authenticated, oauth2-proxy returns 401 and Caddy redirects to `/oauth2/start?rd={uri}` to start the Google login flow. After successful login, the user is sent back to the original URI.

3. **Fail closed**: Any request that does not match the above is responded to with 403 Forbidden.

## Caddy Configuration

The Caddyfile (`Caddy/Caddyfile`) defines:

- `handle /oauth2/*` — proxy OAuth traffic to oauth2-proxy
- `handle @protected` — for non-OAuth paths: run `forward_auth`, then proxy to the web app; on 401, redirect to `/oauth2/start?rd={uri}`
- `respond 403` — default for unmatched paths

## Docker Compose Stack

The Hetzner stack (`docker-compose.hetzner.yml`) runs three services:

| Service       | Role                                                                 |
|---------------|----------------------------------------------------------------------|
| `web`         | ASP.NET app (internal only, exposed to Caddy)                        |
| `oauth2-proxy`| Google OAuth provider, email allowlist, cookie handling              |
| `caddy`       | Reverse proxy, TLS, forward_auth, routes to web and oauth2-proxy     |

oauth2-proxy reads:

- **Environment**: `./oauth2-proxy/.env` (client ID, secret, cookie secret, redirect URL)
- **Config volume**: `./oauth2-proxy/config` mounted at `/config` (contains `allowed_emails.txt`)

## Deployment Setup

### What the deploy scripts do

1. **`scripts/deploy-hetzner.sh`** (runs locally, SSHs to server):
   - Builds the web image and uploads it (chunked)
   - Prepares the remote directory and creates placeholder files **only if they do not exist**:
     - `~/spark3dent-deploy/oauth2-proxy/config/allowed_emails.txt`
     - `~/spark3dent-deploy/oauth2-proxy/.env`
   - Uploads `docker-compose.hetzner.yml`, `Caddy/Caddyfile`, and `deploy-hetzner-remote.sh`
   - Invokes the remote script

2. **`scripts/deploy-hetzner-remote.sh`** (runs on the server):
   - Creates the same placeholder files if missing (idempotent)
   - Loads the Docker image, appends deployment vars to `~/spark3dent-deploy/.env`
   - Runs `docker compose up -d --remove-orphans`

### Placeholder files (never overwritten)

| File | Purpose |
|------|---------|
| `oauth2-proxy/config/allowed_emails.txt` | One email per line; only these accounts can access the app |
| `oauth2-proxy/.env` | OAuth credentials and config (see below) |

### Required configuration in `oauth2-proxy/.env`

Before first use, fill in:

- `OAUTH2_PROXY_CLIENT_ID` — Google OAuth client ID
- `OAUTH2_PROXY_CLIENT_SECRET` — Google OAuth client secret
- `OAUTH2_PROXY_COOKIE_SECRET` — Generate with `openssl rand -base64 32`

Optional overrides:

- `OAUTH2_PROXY_REDIRECT_URL` — Default: `https://spark3dent.com/oauth2/callback`
- `OAUTH2_PROXY_ALLOWED_EMAILS_FILE` — Default: `/config/allowed_emails.txt` (path inside container)

### Directory layout on server

```
~/spark3dent-deploy/
├── .env                    # SPARK3DENT_IMAGE, SPARK3DENT_PORT (managed by deploy)
├── docker-compose.hetzner.yml
├── Caddy/
│   └── Caddyfile
├── oauth2-proxy/
│   ├── .env                # OAuth secrets (you edit)
│   └── config/
│       └── allowed_emails.txt
├── data/
├── blobs/
└── logs/
```

## Applying changes

- **OAuth config or allowlist**: Edit `oauth2-proxy/.env` or `oauth2-proxy/config/allowed_emails.txt`, then:
  ```bash
  docker compose --env-file .env -f docker-compose.hetzner.yml restart oauth2-proxy
  ```
- **Caddyfile or compose file**: After editing, run:
  ```bash
  docker compose --env-file .env -f docker-compose.hetzner.yml up -d
  ```

## Security checks

- If oauth2-proxy is down, app traffic should not be publicly accessible (forward_auth fails; requests get 502 or similar).
- Only emails in `allowed_emails.txt` can access the app after authenticating with Google.
