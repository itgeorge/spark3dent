#!/usr/bin/env bash
set -euo pipefail

REMOTE_DIR="${REMOTE_DIR:-$HOME/spark3dent-deploy}"
IMAGE_NAME="${IMAGE_NAME:-spark3dent-web}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
IMAGE_TAR_CHUNK_PREFIX="${IMAGE_TAR_CHUNK_PREFIX:-}"
IMAGE_TAR_SHA256="${IMAGE_TAR_SHA256:-}"
IMAGE_TAR_SIZE_BYTES="${IMAGE_TAR_SIZE_BYTES:-}"
SPARK3DENT_PORT="${SPARK3DENT_PORT:-8080}"
SPARK3DENT_IMAGE="${SPARK3DENT_IMAGE:-${IMAGE_NAME}:${IMAGE_TAG}}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.hetzner.yml}"

# Resolve REMOTE_DIR to absolute first; all other paths derive from it.
if [[ "${REMOTE_DIR}" == "~" ]]; then
  REMOTE_DIR="${HOME}"
elif [[ "${REMOTE_DIR}" == "~/"* ]]; then
  REMOTE_DIR="${HOME}/${REMOTE_DIR#~/}"
elif [[ "${REMOTE_DIR}" != /* ]]; then
  REMOTE_DIR="${HOME}/${REMOTE_DIR}"
fi

IMAGE_TAR="${IMAGE_TAR:-${REMOTE_DIR}/${IMAGE_NAME//\//_}-${IMAGE_TAG}.tar.gz}"
ENV_FILE="${ENV_FILE:-${REMOTE_DIR}/.env}"
CHUNK_DIR="${CHUNK_DIR:-${REMOTE_DIR}/chunks}"
COMPOSE_PATH="${REMOTE_DIR}/${COMPOSE_FILE}"

OAUTH2_DIR="${REMOTE_DIR}/oauth2-proxy"
OAUTH2_CONFIG_DIR="${OAUTH2_DIR}/config"
OAUTH2_ENV_FILE="${OAUTH2_DIR}/.env"
ALLOWED_EMAILS_FILE="${OAUTH2_CONFIG_DIR}/allowed_emails.txt"

mkdir -p "${REMOTE_DIR}/data" "${REMOTE_DIR}/blobs" "${REMOTE_DIR}/logs" "${REMOTE_DIR}/backups" "${OAUTH2_CONFIG_DIR}"

if [[ ! -f "${ALLOWED_EMAILS_FILE}" ]]; then
  cat > "${ALLOWED_EMAILS_FILE}" <<'ALLOWED_EMAILS_EOF'
# One Google account per line. Only listed emails can access the app.
# owner@example.com
ALLOWED_EMAILS_EOF
fi

if [[ ! -f "${OAUTH2_ENV_FILE}" ]]; then
  cat > "${OAUTH2_ENV_FILE}" <<'OAUTH_ENV_EOF'
# Google OAuth client credentials
OAUTH2_PROXY_CLIENT_ID=
OAUTH2_PROXY_CLIENT_SECRET=

# Random 32-character ascii string (representing 32 random bytes)
OAUTH2_PROXY_COOKIE_SECRET=

# Must match your deployed domain callback path
OAUTH2_PROXY_REDIRECT_URL=https://spark3dent.com/oauth2/callback
OAUTH2_PROXY_ALLOWED_EMAILS_FILE=/config/allowed_emails.txt
OAUTH_ENV_EOF
fi

# Backup dependencies (sqlite3 for .backup; tar/gzip assumed present)
if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "Installing sqlite3 for backup..."
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq && apt-get install -y sqlite3
else
  echo "sqlite3 already present; skipping backup dependency install."
fi

if [[ -n "${IMAGE_TAR_CHUNK_PREFIX}" ]]; then
  echo "Reassembling image archive from chunks in ${CHUNK_DIR}..."
  shopt -s nullglob
  CHUNK_FILES=( "${CHUNK_DIR}/${IMAGE_TAR_CHUNK_PREFIX}"* )
  shopt -u nullglob
  if [[ ${#CHUNK_FILES[@]} -eq 0 ]]; then
    echo "No chunk files found for prefix ${IMAGE_TAR_CHUNK_PREFIX} in ${CHUNK_DIR}" >&2
    exit 1
  fi

  TMP_IMAGE_TAR="${IMAGE_TAR}.tmp"
  cat "${CHUNK_FILES[@]}" > "${TMP_IMAGE_TAR}"

  if [[ -n "${IMAGE_TAR_SIZE_BYTES}" ]]; then
    ACTUAL_SIZE_BYTES="$(wc -c < "${TMP_IMAGE_TAR}" | tr -d '[:space:]')"
    if [[ "${ACTUAL_SIZE_BYTES}" != "${IMAGE_TAR_SIZE_BYTES}" ]]; then
      echo "Image size mismatch: expected ${IMAGE_TAR_SIZE_BYTES}, got ${ACTUAL_SIZE_BYTES}" >&2
      rm -f "${TMP_IMAGE_TAR}"
      exit 1
    fi
  fi

  if [[ -n "${IMAGE_TAR_SHA256}" ]]; then
    if command -v sha256sum >/dev/null 2>&1; then
      ACTUAL_SHA256="$(sha256sum "${TMP_IMAGE_TAR}" | awk '{print $1}')"
    elif command -v shasum >/dev/null 2>&1; then
      ACTUAL_SHA256="$(shasum -a 256 "${TMP_IMAGE_TAR}" | awk '{print $1}')"
    else
      echo "No SHA-256 tool found on server (sha256sum/shasum)." >&2
      rm -f "${TMP_IMAGE_TAR}"
      exit 1
    fi
    if [[ "${ACTUAL_SHA256}" != "${IMAGE_TAR_SHA256}" ]]; then
      echo "Image SHA-256 mismatch: expected ${IMAGE_TAR_SHA256}, got ${ACTUAL_SHA256}" >&2
      rm -f "${TMP_IMAGE_TAR}"
      exit 1
    fi
  fi

  echo "Done reassembling image tar: ${IMAGE_TAR}"
  mv -f "${TMP_IMAGE_TAR}" "${IMAGE_TAR}"
  rm -f "${CHUNK_FILES[@]}"
fi

if [[ ! -f "${IMAGE_TAR}" ]]; then
  echo "Image tar not found: ${IMAGE_TAR}" >&2
  exit 1
fi

if [[ ! -f "${COMPOSE_PATH}" ]]; then
  echo "Compose file not found: ${COMPOSE_PATH}" >&2
  exit 1
fi

echo "Loading Docker image from ${IMAGE_TAR}..."
docker load -i "${IMAGE_TAR}"

echo "Appending deployment vars to ${ENV_FILE}..."
touch "${ENV_FILE}"
IMAGE_LINE="SPARK3DENT_IMAGE=${SPARK3DENT_IMAGE}"
PORT_LINE="SPARK3DENT_PORT=${SPARK3DENT_PORT}"
APPENDED=false
if ! grep -Fxq "${IMAGE_LINE}" "${ENV_FILE}"; then
  {
    echo ""
    echo "# Added by deploy-hetzner-remote.sh on $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "${IMAGE_LINE}"
  } >> "${ENV_FILE}"
  APPENDED=true
fi
if ! grep -Fxq "${PORT_LINE}" "${ENV_FILE}"; then
  if [[ "${APPENDED}" == "false" ]]; then
    {
      echo ""
      echo "# Added by deploy-hetzner-remote.sh on $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    } >> "${ENV_FILE}"
  fi
  echo "${PORT_LINE}" >> "${ENV_FILE}"
  APPENDED=true
fi
if [[ "${APPENDED}" == "false" ]]; then
  echo "Deployment vars already present in ${ENV_FILE}; no append needed."
fi

# Predeploy backup: run only if app container already exists (not first deploy)
if docker ps -a -q --filter "name=${IMAGE_NAME}" 2>/dev/null | grep -q .; then
  DEPLOY_COMMIT_FILE="${REMOTE_DIR}/.deploycommit.txt"
  BACKUP_SCRIPT="${REMOTE_DIR}/backup-hetzner.sh"
  if [[ ! -f "${DEPLOY_COMMIT_FILE}" ]]; then
    echo "Predeploy backup: .deploycommit.txt not found." >&2
    exit 1
  fi
  if [[ ! -x "${BACKUP_SCRIPT}" ]]; then
    echo "Predeploy backup: backup script not found or not executable." >&2
    exit 1
  fi
  DEPLOY_SHA="$(tr -d '[:space:]' < "${DEPLOY_COMMIT_FILE}" | head -c 128)"
  echo "Running predeploy backup (suffix: predeploy-${DEPLOY_SHA})..."
  "${BACKUP_SCRIPT}" "predeploy-${DEPLOY_SHA}"
else
  echo "First deployment detected (no ${IMAGE_NAME} container); skipping predeploy backup."
fi

echo "Starting/updating stack..."
cd "${REMOTE_DIR}"
docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_PATH}" up -d --remove-orphans

echo "Deployment status:"
docker compose --env-file "${ENV_FILE}" -f "${COMPOSE_PATH}" ps

# Idempotent cron entry: daily backup at 04:15 local time
BACKUP_LOG="${REMOTE_DIR}/logs/backup.log"
CRON_MARKER="# Spark3Dent daily backup (managed by deploy-hetzner-remote.sh)"
BACKUP_CRON_ENTRY="15 4 * * * REMOTE_DIR=${REMOTE_DIR} ${REMOTE_DIR}/backup-hetzner.sh >> ${BACKUP_LOG} 2>&1"
CURRENT_CRONTAB="$(crontab -l 2>/dev/null || echo "")"
if echo "${CURRENT_CRONTAB}" | grep -qF "${CRON_MARKER}"; then
  echo "Cron already configured (marker present); skipping."
else
  NEW_CRONTAB="$(echo "${CURRENT_CRONTAB}" | grep -vF "${REMOTE_DIR}/backup-hetzner.sh" | grep -vF "${CRON_MARKER}" | sed '/^$/d')
${CRON_MARKER}
${BACKUP_CRON_ENTRY}"
  echo "${NEW_CRONTAB}" | crontab -
  echo "Cron: daily backup at 04:15 → ${BACKUP_LOG}"
fi
