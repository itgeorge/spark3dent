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

- [ ] Confirm the deployed SQLite database path is `${REMOTE_DIR}/data/spark3dent.db`.
- [ ] Confirm the deployed blobs path is `${REMOTE_DIR}/blobs`.
- [ ] Confirm the backup archives should live in a dedicated directory under the deploy root, such as `${REMOTE_DIR}/backups`.
- [ ] Confirm the target server already has or can install the needed tools non-interactively (`sqlite3`, `tar`, `gzip`, `cron`/`crontab` support).
- [ ] Confirm the backup should run outside Docker against the host-mounted database and blobs paths.

## Implementation Plan

### 1. Backup artifact design

- [ ] Choose and document the backup filename format, for example `spark3dent-backup-YYYYmmdd-HHMMSS[-suffix].tar.gz`.
- [ ] Define sanitization rules for the optional suffix so it remains filename-safe while preserving leading dash usage in examples like `-before-db-update`.
- [ ] Decide the internal archive layout, for example:
  - `db/spark3dent.db`
  - `blobs/...`
  - optional metadata file such as creation timestamp and source paths

### 2. Server backup script

- [ ] Add a dedicated executable backup script that will live on the server, ideally under `${REMOTE_DIR}` so deploy can manage it.
- [ ] Implement argument handling so the script accepts an optional suffix argument and appends it before `.tar.gz`.
- [ ] In the script, create a temporary working directory for assembling backup contents safely.
- [ ] Use `sqlite3 "${REMOTE_DIR}/data/spark3dent.db" ".backup '<temp db path>'"` to create a consistent database copy.
- [ ] Copy or archive the `blobs` directory contents together with the backed-up database copy.
- [ ] Produce a compressed archive on the server with the timestamped filename.
- [ ] Clean up temporary files on success and failure.
- [ ] Print the final backup path so deploy logs clearly show what was created.

### 3. Rotation policy

- [ ] Implement backup rotation in the server backup script or an immediately-following helper step.
- [ ] Keep only the newest `100` backup archives in the backup directory.
- [ ] Make rotation deterministic by sorting by modification time or filename timestamp and deleting only older matching backup files.
- [ ] Ensure rotation does not delete unrelated files outside the backup naming pattern.

### 4. Deployment-time dependency setup

- [ ] Update `scripts/deploy-hetzner-remote.sh` to ensure required backup dependencies are installed on the server before backup setup runs.
- [ ] Keep installation idempotent so repeated deployments do not fail or reinstall unnecessarily.
- [ ] Decide where in the remote deployment flow dependency installation should happen so backup is available before the predeploy step is needed.

### 5. Deploy commit SHA handoff

- [ ] Update `scripts/deploy-hetzner.sh` to compute the deployment commit SHA from the current repo state.
- [ ] Write that SHA into a local `.deploycommit.txt` artifact during deployment preparation.
- [ ] SCP `.deploycommit.txt` to the remote deploy directory as a separate file.
- [ ] Decide whether the file should contain the full SHA or short SHA and keep the choice consistent with the backup suffix format.

### 6. Predeploy backup flow

- [ ] Update the deployment strategy so an on-demand backup runs before the new container deployment is applied.
- [ ] Read the copied commit SHA from `.deploycommit.txt` on the server.
- [ ] Invoke the server backup script with suffix `-predeploy-[git commit sha]`.
- [ ] Make the predeploy backup happen after dependency/setup work is ready but before `docker compose up -d --remove-orphans`.
- [ ] Decide failure behavior explicitly: if the predeploy backup fails, the deployment should stop rather than continue.

### 7. Scheduled daily backup

- [ ] Choose the scheduling mechanism for the server, most likely a cron entry managed by the remote deploy script.
- [ ] Install or update an idempotent cron entry to run the backup script every day at `04:15` local time.
- [ ] Ensure the scheduled command runs with paths/env that do not depend on an interactive shell.
- [ ] Decide where scheduled job output should go, for example a dedicated backup log file under `${REMOTE_DIR}/logs`.
- [ ] Verify the cron setup avoids duplicate entries across repeated deployments.

### 8. Upload/install integration

- [ ] Update `scripts/deploy-hetzner.sh` to upload any new backup-related files needed by the server.
- [ ] Update the remote deploy setup phase so the backup script is normalized to LF on the server and marked executable.
- [ ] Ensure remote directory creation includes the backup directory and any log directory needed for scheduled backups.

### 9. Documentation updates

- [ ] Update `deployments.md` with the new backup behavior for Hetzner deployments.
- [ ] Document the backup location, naming format, daily schedule, retention policy, and optional suffix behavior.
- [ ] Document that each deployment creates a `predeploy-[git commit sha]` backup before applying the new release.
- [ ] Document any server package prerequisites that the deployment script now installs automatically.

## Verification Checklist

- [ ] Run the deployment flow in a safe environment and verify `.deploycommit.txt` is generated locally and copied to the server.
- [ ] Verify the remote backup script can be run manually with no suffix.
- [ ] Verify the remote backup script can be run manually with a suffix like `-before-db-update`.
- [ ] Verify the produced archive contains both a SQLite backup copy and the blobs content.
- [ ] Verify the database portion is created through SQLite `.backup`, not by raw file copy.
- [ ] Verify the predeploy deployment path creates a backup with the expected `-predeploy-[git commit sha]` suffix.
- [ ] Verify the daily scheduler is installed exactly once and shows the `04:15` local-time run configuration.
- [ ] Verify rotation removes older archives and preserves the newest `100`.
- [ ] Verify a failed backup causes deployment to stop before replacing the running stack.
- [ ] Verify `deployments.md` matches the implemented behavior.

## Suggested Execution Order

- [ ] Implement the server backup script and rotation logic first.
- [ ] Wire dependency installation and script deployment into `scripts/deploy-hetzner-remote.sh`.
- [ ] Add `.deploycommit.txt` creation/upload in `scripts/deploy-hetzner.sh`.
- [ ] Add the predeploy backup invocation to the remote deployment flow.
- [ ] Add the scheduled daily backup setup.
- [ ] Update `deployments.md`.
- [ ] Run the verification checklist and mark off each item with concrete notes if anything needs follow-up.
