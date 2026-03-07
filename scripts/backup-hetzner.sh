#!/usr/bin/env bash
# Server-side backup script for Hetzner deployment.
# Creates s3d-bak-YYYYmmdd-HHMMSS[-suffix].tar.gz with db + blobs + backup.json.
# Usage: backup-hetzner.sh [suffix]
#   suffix: optional, e.g. "predeploy-abc1234" or "before-db-update"
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REMOTE_DIR="${REMOTE_DIR:-$SCRIPT_DIR}"
BACKUP_DIR="${REMOTE_DIR}/backups"
DB_PATH="${REMOTE_DIR}/data/spark3dent.db"
BLOBS_PATH="${REMOTE_DIR}/blobs"
ROTATION_KEEP=100

# Sanitize suffix: allow a-z A-Z 0-9 - _; replace others with _; collapse; strip; max 128 chars.
sanitize_suffix() {
  local raw="$1"
  if [[ -z "${raw}" ]]; then
    echo ""
    return
  fi
  local sanitized
  sanitized="$(printf '%s' "${raw}" | sed 's/[^a-zA-Z0-9_-]/_/g' | sed 's/__*/_/g' | sed 's/^[-_]*//' | sed 's/[-_]*$//')"
  if [[ -z "${sanitized}" ]]; then
    echo ""
    return
  fi
  if [[ ${#sanitized} -gt 128 ]]; then
    echo "${sanitized:0:127}_"
  else
    echo "${sanitized}"
  fi
}

SUFFIX_ARG="${1:-}"
SUFFIX="$(sanitize_suffix "${SUFFIX_ARG}")"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
if [[ -n "${SUFFIX}" ]]; then
  BASENAME="s3d-bak-${TIMESTAMP}-${SUFFIX}.tar.gz"
else
  BASENAME="s3d-bak-${TIMESTAMP}.tar.gz"
fi
OUTPUT_PATH="${BACKUP_DIR}/${BASENAME}"

mkdir -p "${BACKUP_DIR}"
TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "${TMP_DIR}"
}
trap cleanup EXIT

# Create staging layout
STAGE_DB="${TMP_DIR}/db"
STAGE_BLOBS="${TMP_DIR}/blobs"
mkdir -p "${STAGE_DB}" "${STAGE_BLOBS}"

if [[ ! -f "${DB_PATH}" ]]; then
  echo "Database not found: ${DB_PATH}" >&2
  exit 1
fi

echo "Backing up database via SQLite .backup..."
sqlite3 "${DB_PATH}" ".backup '${STAGE_DB}/spark3dent.db'"

echo "Copying blobs..."
if [[ -d "${BLOBS_PATH}" ]]; then
  cp -a "${BLOBS_PATH}/." "${STAGE_BLOBS}/"
else
  mkdir -p "${STAGE_BLOBS}"
fi

CREATED_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "${TMP_DIR}/backup.json" <<EOF
{
  "created_utc": "${CREATED_UTC}",
  "source_db_path": "${DB_PATH}",
  "source_blobs_path": "${BLOBS_PATH}"
}
EOF

echo "Creating archive: ${OUTPUT_PATH}"
tar -czf "${OUTPUT_PATH}" -C "${TMP_DIR}" db blobs backup.json

# Rotation: keep only newest ROTATION_KEEP s3d-bak-*.tar.gz (sort by mtime, delete oldest)
shopt -s nullglob
BACKUPS=( "${BACKUP_DIR}"/s3d-bak-*.tar.gz )
shopt -u nullglob
if [[ ${#BACKUPS[@]} -gt ${ROTATION_KEEP} ]]; then
  # ls -t = newest first; tail -n +N = skip first N-1 (keep newest ROTATION_KEEP)
  mapfile -t TO_DELETE < <(ls -1t "${BACKUPS[@]}" 2>/dev/null | tail -n +$((ROTATION_KEEP + 1)))
  for f in "${TO_DELETE[@]}"; do
    rm -f "$f"
    echo "Rotated (removed): $f"
  done
fi

echo "Backup created: ${OUTPUT_PATH}"
