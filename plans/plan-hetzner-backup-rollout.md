# Hetzner Backup Rollout Plan

Agent instructions:
- Work through this file from top to bottom.
- When a task is completed, change `[ ]` to `[x]`.
- If you partially complete a task or discover follow-up work, add a new `[ ]` item directly below the relevant step with a short note.
- Keep notes brief and factual so another agent can resume from this file.
- Do not mark an item as done until the code, docs, and verification for that item are actually finished.

## Goal

Add a server-side backup system for the Hetzner deployment that:
- Backs up the SQLite database using the SQLite `.backup` command.
- Includes the deployed `blobs` folder in the same compressed backup artifact.
- Writes backups on the server to a timestamp-based filename.
- Accepts an optional suffix such as `-before-db-update` without replacing the archive extension.
- Runs automatically once per day at `04:15` local server time.
- Rotates backups so only the most recent `100` archives are kept.
- Runs an additional on-demand backup during deployment using a `predeploy-[git commit sha]` suffix.
- Copies the commit SHA to the server via a separate `.deploycommit.txt` file.
- Makes backup setup and dependency installation part of the server-side deployment flow.

## Assumptions To Confirm During Implementation

- [x] Confirm the deployed SQLite database path is `${REMOTE_DIR}/data/spark3dent.db`.
- [x] Confirm the deployed blobs path is `${REMOTE_DIR}/blobs`.
- [x] Confirm the backup archives should live in a dedicated directory under the deploy root, such as `${REMOTE_DIR}/backups`.
- [x] Note: use `/root/spark3dent-deploy/backups`; directory does not exist yet and will be created by rollout.
- [x] Confirm the target server already has or can install the needed tools non-interactively (`sqlite3`, `tar`, `gzip`, `cron`/`crontab` support).
- [x] Note: server already has `tar`, `gzip`, `crontab`, `cron`, and Docker Compose; deployment may install missing `sqlite3` non-interactively.
- [x] Confirm the backup should run outside Docker against the host-mounted database and blobs paths.

## Implementation Plan

### 1. Backup artifact design

- [x] Choose and document the backup filename format, for example `s3d-bak-YYYYmmdd-HHMMSS[-suffix].tar.gz`.
- [x] Define sanitization rules for the optional suffix so it remains filename-safe while preserving leading dash usage in examples like `-before-db-update`.
- [x] Decide the internal archive layout, for example:
  - `db/spark3dent.db`
  - `blobs/...`
  - optional metadata file such as creation timestamp and source paths

**Design (phase 1):**

**Filename format:** `s3d-bak-YYYYmmdd-HHMMSS[-suffix].tar.gz`
- `YYYYmmdd` = date (e.g. `20250307`)
- `HHMMSS` = time (e.g. `041500`)
- `[-suffix]` = optional; if present, inserted before `.tar.gz` with a leading dash
- Examples: `s3d-bak-20250307-041500.tar.gz`, `s3d-bak-20250307-041500-predeploy-abc1234.tar.gz`

**Suffix sanitization rules:**
- Caller passes suffix without leading dash; script adds it. E.g. `predeploy-abc1234` → `-predeploy-abc1234`.
- Allow only: `a-z`, `A-Z`, `0-9`, `-`, `_`. Replace any other character with `_`.
- Collapse consecutive invalid chars to a single `_`.
- Strip leading/trailing `-` and `_` from the sanitized result.
- If result is empty after sanitization, omit the suffix entirely.
- Max length: 128 chars (to accommodate description plus commit SHA in `predeploy-` case; truncate with `_` suffix if needed).

**Internal archive layout:**
```
db/
  spark3dent.db          # SQLite backup via .backup (not raw copy)
blobs/
  <all blob files>       # Recursive copy of blobs directory
backup.json              # Metadata (JSON): created_utc (ISO8601), source_db_path, source_blobs_path
```

**backup.json structure:**
```json
{
  "created_utc": "2025-03-07T04:15:00Z",
  "source_db_path": "/root/spark3dent-deploy/data/spark3dent.db",
  "source_blobs_path": "/root/spark3dent-deploy/blobs"
}
```

### 2. Server backup script

- [x] Add a dedicated executable backup script that will live on the server, ideally under `${REMOTE_DIR}` so deploy can manage it.
- [x] Implement argument handling so the script accepts an optional suffix argument and appends it before `.tar.gz`.
- [x] In the script, create a temporary working directory for assembling backup contents safely.
- [x] Use `sqlite3 "${REMOTE_DIR}/data/spark3dent.db" ".backup '<temp db path>'"` to create a consistent database copy.
- [x] Copy or archive the `blobs` directory contents together with the backed-up database copy.
- [x] Produce a compressed archive on the server with the timestamped filename.
- [x] Clean up temporary files on success and failure.
- [x] Print the final backup path so deploy logs clearly show what was created.

### 3. Rotation policy

- [x] Implement backup rotation in the server backup script or an immediately-following helper step.
- [x] Keep only the newest `100` backup archives in the backup directory.
- [x] Make rotation deterministic by sorting by modification time or filename timestamp and deleting only older matching backup files.
- [x] Ensure rotation does not delete unrelated files outside the backup naming pattern.

### 4. Deployment-time dependency setup

- [x] Update `scripts/deploy-hetzner-remote.sh` to ensure required backup dependencies are installed on the server before backup setup runs.
- [x] Keep installation idempotent so repeated deployments do not fail or reinstall unnecessarily.
- [x] Decide where in the remote deployment flow dependency installation should happen so backup is available before the predeploy step is needed.
  - Note: runs immediately after OAUTH2 setup, before image reassembly; ensures sqlite3 is ready before any predeploy backup.

### 5. Deploy commit SHA handoff

- [x] Update `scripts/deploy-hetzner.sh` to compute the deployment commit SHA from the current repo state.
- [x] Write that SHA into a local `.deploycommit.txt` artifact during deployment preparation.
- [x] SCP `.deploycommit.txt` to the remote deploy directory as a separate file.
- [x] Decide whether the file should contain the full SHA or short SHA and keep the choice consistent with the backup suffix format.
  - Note: short SHA (7 chars) via `git rev-parse --short HEAD`; stored in `.docker/deploy-artifacts/.deploycommit.txt`, uploaded to `${REMOTE_DIR}/.deploycommit.txt`.

### 6. Predeploy backup flow

- [x] Update the deployment strategy so an on-demand backup runs before the new container deployment is applied.
- [x] Read the copied commit SHA from `.deploycommit.txt` on the server.
- [x] Invoke the server backup script with suffix `-predeploy-[git commit sha]`.
- [x] Make the predeploy backup happen after dependency/setup work is ready but before `docker compose up -d --remove-orphans`.
- [x] Decide failure behavior explicitly: if the predeploy backup fails, the deployment should stop rather than continue.

### 7. Scheduled daily backup

- [x] Choose the scheduling mechanism for the server, most likely a cron entry managed by the remote deploy script.
- [x] Install or update an idempotent cron entry to run the backup script every day at `04:15` local time.
- [x] Ensure the scheduled command runs with paths/env that do not depend on an interactive shell.
- [x] Decide where scheduled job output should go, for example a dedicated backup log file under `${REMOTE_DIR}/logs`.
- [x] Verify the cron setup avoids duplicate entries across repeated deployments.
  - Note: deploy removes any existing lines matching backup script path before appending; output to `${REMOTE_DIR}/logs/backup.log`.

### 8. Upload/install integration

- [x] Update `scripts/deploy-hetzner.sh` to upload any new backup-related files needed by the server.
- [x] Update the remote deploy setup phase so the backup script is normalized to LF on the server and marked executable.
- [x] Ensure remote directory creation includes the backup directory and any log directory needed for scheduled backups.

### 9. Documentation updates

- [x] Update `deployments.md` with the new backup behavior for Hetzner deployments.
- [x] Document the backup location, naming format, daily schedule, retention policy, and optional suffix behavior.
- [x] Document that each deployment creates a `predeploy-[git commit sha]` backup before applying the new release.
- [x] Document any server package prerequisites that the deployment script now installs automatically.

## Verification Checklist

- [x] Run the deployment flow in a safe environment and verify `.deploycommit.txt` is generated locally and copied to the server.
- [x] Verify the remote backup script can be run manually with no suffix.
- [x] Verify the remote backup script can be run manually with a suffix like `-before-db-update`.
- [x] Verify the produced archive contains both a SQLite backup copy and the blobs content.
- [x] Verify the database portion is created through SQLite `.backup`, not by raw file copy.
- [x] Verify the predeploy deployment path creates a backup with the expected `-predeploy-[git commit sha]` suffix.
- [x] Verify the daily scheduler is installed exactly once and shows the `04:15` local-time run configuration.
- [ ] Verify rotation removes older archives and preserves the newest `100`.
- [ ] Verify a failed backup causes deployment to stop before replacing the running stack.
- [x] Verify `deployments.md` matches the implemented behavior.

## Suggested Execution Order

- [x] Implement the server backup script and rotation logic first.
- [x] Wire dependency installation and script deployment into `scripts/deploy-hetzner-remote.sh`.
- [x] Add `.deploycommit.txt` creation/upload in `scripts/deploy-hetzner.sh`.
- [x] Add the predeploy backup invocation to the remote deployment flow.
- [x] Add the scheduled daily backup setup.
- [x] Update `deployments.md`.
- [ ] Run the verification checklist and mark off each item with concrete notes if anything needs follow-up.
