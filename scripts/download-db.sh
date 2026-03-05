#!/usr/bin/env bash
set -euo pipefail

hetzner-agent() {
  if [ -n "${SSH_AUTH_SOCK:-}" ] && ssh-add -l >/dev/null 2>&1; then
    ssh-add -l 2>/dev/null | grep -q "id_ed25519_hetzner" || ssh-add ~/.ssh/id_ed25519_hetzner
    echo "ssh-agent already running; hetzner key ensured."
    return 0
  fi
  eval "$(ssh-agent -s)" >/dev/null
  ssh-add ~/.ssh/id_ed25519_hetzner
  echo "Started ssh-agent and loaded hetzner key."
}

hetzner-agent

SSH_HOST="${SSH_HOST:-spark3dent-hetzner}"
REMOTE_DIR="${REMOTE_DIR:-~/spark3dent-deploy}"
SSH_IDENTITY_FILE="${SSH_IDENTITY_FILE:-$HOME/.ssh/id_ed25519_hetzner}"
SSH_IDENTITIES_ONLY="${SSH_IDENTITIES_ONLY:-yes}"
COMPOSE_FILE="docker-compose.hetzner.yml"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
# Usage: download-db.sh [local_dest]
#   local_dest  Where to save the db (default: db_dumps/spark3dent.db)
DB_DUMPS_DIR="${REPO_ROOT}/db_dumps"
LOCAL_DEST="${1:-${DB_DUMPS_DIR}/spark3dent.db}"

SSH_OPTS=(-o "IdentityFile=${SSH_IDENTITY_FILE}" -o "IdentitiesOnly=${SSH_IDENTITIES_ONLY}")

REMOTE_DB_PATH="${REMOTE_DIR}/data/spark3dent.db"

ENV_FILE=".env"

echo "Connecting to ${SSH_HOST} to download database..."
echo "  1. Stopping spark3dent-web container..."
ssh "${SSH_OPTS[@]}" "${SSH_HOST}" bash -s -- "${REMOTE_DIR}" "${COMPOSE_FILE}" "${ENV_FILE}" <<'EOF'
set -euo pipefail
remote_dir="$1"
compose_file="$2"
env_file="$3"
if [[ "${remote_dir}" == "~/"* ]]; then
  remote_dir="${HOME}/${remote_dir#~/}"
elif [[ "${remote_dir}" != /* ]]; then
  remote_dir="${HOME}/${remote_dir}"
fi
cd "${remote_dir}"
env_file_path="${remote_dir}/${env_file}"
if [[ -f "${env_file_path}" ]]; then
  docker compose --env-file "${env_file_path}" -f "${compose_file}" stop web
else
  docker compose -f "${compose_file}" stop web
fi
EOF

echo "  2. Copying database from ${SSH_HOST}:${REMOTE_DB_PATH} to ${LOCAL_DEST}..."
mkdir -p "$(dirname "${LOCAL_DEST}")"
scp "${SSH_OPTS[@]}" "${SSH_HOST}:${REMOTE_DB_PATH}" "${LOCAL_DEST}"

echo "  3. Restarting spark3dent-web container..."
ssh "${SSH_OPTS[@]}" "${SSH_HOST}" bash -s -- "${REMOTE_DIR}" "${COMPOSE_FILE}" "${ENV_FILE}" <<'EOF'
set -euo pipefail
remote_dir="$1"
compose_file="$2"
env_file="$3"
if [[ "${remote_dir}" == "~/"* ]]; then
  remote_dir="${HOME}/${remote_dir#~/}"
elif [[ "${remote_dir}" != /* ]]; then
  remote_dir="${HOME}/${remote_dir}"
fi
cd "${remote_dir}"
env_file_path="${remote_dir}/${env_file}"
if [[ -f "${env_file_path}" ]]; then
  docker compose --env-file "${env_file_path}" -f "${compose_file}" start web
else
  docker compose -f "${compose_file}" start web
fi
EOF

echo "Done. Database saved to ${LOCAL_DEST}"
