# Spark3Dent Deployment Guide

This document describes the project structure and run/deployment strategy for development, local Docker, and Hetzner production deployments.

---

## Project Structure

```
spark3dent/
├── Web/                    # ASP.NET Core web app (Blazor + API)
│   ├── Dockerfile          # Multi-stage build for container deployments
│   ├── appsettings.json    # App config (Runtime, SingleBox, App)
│   └── wwwroot/            # Static assets (index.html embedded)
├── Cli/                    # CLI tool (shares config with Web)
├── AppSetup/               # Bootstrap, DB migrations, DI wiring
├── Configuration/          # Config models (HostingMode, SingleBox, etc.)
├── Invoices/               # Invoice domain
├── Accounting/             # Accounting domain
├── Database/               # EF Core + SQLite
├── Storage/                # Blob storage
├── ChromiumFetcher/        # Bundles Chromium for PDF generation
├── Caddy/                  # Caddy reverse-proxy config (Hetzner only)
│   └── Caddyfile
├── docker-compose.local.yml   # Local Docker stack
├── docker-compose.hetzner.yml  # Production Hetzner stack (web + Caddy)
└── scripts/
    ├── deploy-local.sh        # Local Docker: up/down/restart/logs/ps
    ├── deploy-local.ps1        # Same, for PowerShell on Windows
    ├── deploy-hetzner.sh       # Build, upload, deploy to Hetzner
    ├── deploy-hetzner-remote.sh # Server-side deploy (load image, compose up)
    └── backup-hetzner.sh       # Server-side backup (db + blobs; used by deploy and cron)
```

### Hosting Modes

The app supports three hosting modes via `Runtime.HostingMode`:

| Mode           | Bind address | Port source              | Use case                    |
|----------------|--------------|---------------------------|-----------------------------|
| `Desktop`      | 127.0.0.1    | Dynamic or `PORT` env     | Dev: `dotnet run` from IDE  |
| `LocalDocker`  | 0.0.0.0      | `Runtime.Port` or env    | Local Docker (8080)         |
| `HetznerDocker`| 0.0.0.0      | `Runtime.Port` required   | Production on Hetzner       |

---

## Development (Desktop)

Run the web app directly without Docker. Uses `HostingMode.Desktop` by default.

**Prerequisites:** .NET 9 SDK

**Run:**
```bash
dotnet run --project Web
```

- Binds to `http://127.0.0.1:<dynamic-port>` (or `PORT` env).
- Auto-opens browser when `ASPNETCORE_ENVIRONMENT` is `Development` or `Mvp`.
- Data paths default to `%LocalAppData%\Spark3Dent` and `Documents\Spark3Dent` (Windows).

---

## Local Docker

Run the app in a container for local testing. Uses `HostingMode.LocalDocker`, port 8080.

**Prerequisites:** Docker, Docker Compose

**Run:**
```bash
# Bash (Git Bash / WSL)
./scripts/deploy-local.sh up

# PowerShell (Windows)
.\scripts\deploy-local.ps1 up
```

**Commands:**
| Command | Description |
|---------|-------------|
| `up` | Start stack (builds image by default) |
| `up --no-build` | Start without rebuilding |
| `down` | Stop and remove containers |
| `restart` | Down + up with rebuild |
| `logs` | Follow container logs |
| `ps` | Show container status |

**Details:**
- Compose file: `docker-compose.local.yml`
- Image: `spark3dent-web:local`
- Port: `8080:8080` (reachable at `http://localhost:8080`)
- Data: `.docker/local/data`, `.docker/local/blobs`, `.docker/local/logs`
- For legacy PDF import: pass `App__OpenAiKey` via environment (e.g. in `.env` file, not committed)

---

## Hetzner Production

Deploy to a Hetzner VPS with Caddy as reverse proxy, TLS via Let's Encrypt, and the app on internal port 8080.

**Prerequisites:**
- SSH access to Hetzner host (e.g. `~/.ssh/id_ed25519_hetzner`)
- SSH config alias `spark3dent-hetzner` (or set `SSH_HOST`)
- Domain `spark3dent.com` with A records pointing to the server
- Firewall allows TCP 22, 80, 443 (8080 is internal only)

**Run:**
```bash
./scripts/deploy-hetzner.sh
```

**Options:**
| Option | Description |
|--------|-------------|
| `--skip-build` | Reuse existing image archive (retry after upload failure) |
| `--skip-upload` | Skip image upload; assume chunks already on server |

**Deploy flow:**
1. Build Docker image from `Web/Dockerfile`
2. Save image as compressed tar, split into chunks
3. Write deployment commit SHA to `.deploycommit.txt`, upload chunks, `docker-compose.hetzner.yml`, `Caddy/Caddyfile`, `backup-hetzner.sh`, `.deploycommit.txt`, and remote script via SCP (`scp` tends to fail sometimes on large files, so we split into chunks with retries)
4. SSH into server: install backup deps (sqlite3 if missing), reassemble image, validate SHA-256, `docker load`
5. Append `SPARK3DENT_IMAGE` and `SPARK3DENT_PORT` to `.env`
6. Run predeploy backup (suffix `-predeploy-[commit sha]`) if app container exists; on failure, deployment stops
7. Run `docker compose up -d --remove-orphans`
8. Wait for the `web` container health check to report healthy (`/healthz`)
9. Run `docker image prune -a -f` to remove old unused images only after the new stack is up and healthy
10. Install or update cron for daily backup at 04:15

**Stack:**
- **web**: `spark3dent-web:latest`, listens on 8080 inside Docker network only (no host port), exposes `/healthz` for Docker health checks
- **caddy**: Listens on 80 and 443; terminates TLS; proxies to `web:8080`

**Caddy:**
- `spark3dent.com` → reverse proxy to app, auto HTTP→HTTPS, Let's Encrypt certs
- `www.spark3dent.com` → redirect to `https://spark3dent.com`
- Certificates stored in `caddy_data` volume; renewal is automatic

**Image cleanup note:**
- Disk growth on Hetzner was traced to stale Docker/containerd image storage under `/var/lib/containerd`, not app data or SQLite.
- After adding post-deploy `docker image prune -a -f`, unused image revisions from repeated `spark3dent-web:latest` deploys are removed once the new container is healthy.
- In practice this reduced `/var/lib/containerd` from roughly **18G** to about **1.6G**.

**Remote paths (default `~/spark3dent-deploy`):**
- `data/`, `blobs/`, `logs/` — app persistence
- `backups/` — backup archives (see Backups below)
- `Caddy/Caddyfile` — Caddy config
- `.env` — `SPARK3DENT_IMAGE`, `SPARK3DENT_PORT`, `App__OpenAiKey` (for legacy PDF import; never commit real keys)
- `backup-hetzner.sh` — server-side backup script (uploaded and made executable by deploy)
- `.deploycommit.txt` — deployment commit SHA for predeploy backup suffix (uploaded each deploy)

**Backups:**
- **Location:** `${REMOTE_DIR}/backups` (e.g. `/root/spark3dent-deploy/backups`)
- **Filename format:** `s3d-bak-YYYYmmdd-HHMMSS[-suffix].tar.gz` (e.g. `s3d-bak-20250307-041500.tar.gz`)
- **Optional suffix:** Passed without leading dash; sanitized to `a-z`, `A-Z`, `0-9`, `-`, `_`; max 128 chars. Example: `predeploy-abc1234` → `-predeploy-abc1234`
- **Daily schedule:** Cron runs the backup script every day at **04:15** local server time; output to `${REMOTE_DIR}/logs/backup.log`
- **Retention:** Only the newest **100** archives are kept; older `s3d-bak-*.tar.gz` files are rotated out
- **Predeploy backup:** Each deployment creates a backup with suffix `-predeploy-[git commit sha]` (short SHA, 7 chars) before applying the new release. If the predeploy backup fails, deployment stops. First-time deploys (no existing container) skip the predeploy backup.
- **Archive contents:** `db/spark3dent.db` (SQLite `.backup`), `blobs/`, and `backup.json` (metadata)
- **Server prerequisites:** The deployment script installs `sqlite3` non-interactively if missing (`apt-get install -y sqlite3`). `tar`, `gzip`, `cron`/`crontab` are assumed present.

---

## Environment Variables

| Variable | Scope | Default | Description |
|----------|-------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Web | - | `Development`, `Production`, etc. |
| `App__OpenAiKey` | Web | null | OpenAI API key for legacy PDF import. **Never commit real keys.** Use env or env file. |
| `OPENAI_API_KEY` | Web | - | Fallback for OpenAI key (alternative to `App__OpenAiKey`) |
| `Runtime__HostingMode` | Web | Desktop | `Desktop`, `LocalDocker`, `HetznerDocker` |
| `Runtime__Port` | Web | null | Port to bind (required for HetznerDocker) |
| `Runtime__BindAddress` | Web | mode-based | Override bind address |
| `SPARK3DENT_IMAGE` | Hetzner compose | spark3dent-web:latest | Image name:tag |
| `SPARK3DENT_PORT` | Hetzner compose | 8080 | Internal app port |
| `SSH_HOST` | deploy-hetzner.sh | spark3dent-hetzner | SSH host alias |

---

## Data Paths by Deployment

| Deployment | Database | Blobs | Logs |
|------------|----------|-------|------|
| Desktop | `%LocalAppData%\Spark3Dent\spark3dent.db` | `Documents\Spark3Dent` | `%LocalAppData%\Spark3Dent\logs` |
| Local Docker | `.docker/local/data/spark3dent.db` | `.docker/local/blobs` | `.docker/local/logs` |
| Hetzner | `./data/spark3dent.db` | `./blobs` | `./logs` |
