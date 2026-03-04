#!/usr/bin/env bash
set -euo pipefail

hetzner-agent() {
  # If we already have a working agent, don't start a new one
  if [ -n "${SSH_AUTH_SOCK:-}" ] && ssh-add -l >/dev/null 2>&1; then
    # Add the key only if it's not already loaded
    ssh-add -l 2>/dev/null | grep -q "id_ed25519_hetzner" || ssh-add ~/.ssh/id_ed25519_hetzner
    echo "ssh-agent already running; hetzner key ensured."
    return 0
  fi

  # Otherwise start a new agent and add the key
  eval "$(ssh-agent -s)" >/dev/null
  ssh-add ~/.ssh/id_ed25519_hetzner
  echo "Started ssh-agent and loaded hetzner key."
}

# Start ssh-agent and load hetzner key if not already running
hetzner-agent

SSH_HOST="${SSH_HOST:-spark3dent-hetzner}"
IMAGE_NAME="${IMAGE_NAME:-spark3dent-web}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
SPARK3DENT_PORT="${SPARK3DENT_PORT:-8080}"
REMOTE_DIR="${REMOTE_DIR:-~/spark3dent-deploy}"
SSH_IDENTITY_FILE="${SSH_IDENTITY_FILE:-$HOME/.ssh/id_ed25519_hetzner}"
SSH_IDENTITIES_ONLY="${SSH_IDENTITIES_ONLY:-yes}"
CHUNK_SIZE_MB="${CHUNK_SIZE_MB:-10}"
SCP_RETRY_DELAY_SECONDS="${SCP_RETRY_DELAY_SECONDS:-2}"
SKIP_BUILD=false
SKIP_UPLOAD=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-build)
      SKIP_BUILD=true
      shift
      ;;
    --skip-upload)
      SKIP_BUILD=true
      SKIP_UPLOAD=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      echo "Usage: $0 [--skip-build] [--skip-upload]" >&2
      exit 1
      ;;
  esac
done

SSH_OPTS=(-o "IdentityFile=${SSH_IDENTITY_FILE}" -o "IdentitiesOnly=${SSH_IDENTITIES_ONLY}")

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/.docker/deploy-artifacts"
IMAGE_REF="${IMAGE_NAME}:${IMAGE_TAG}"
IMAGE_TAR="${ARTIFACTS_DIR}/${IMAGE_NAME//\//_}-${IMAGE_TAG}.tar.gz"
IMAGE_FILE_NAME="$(basename "${IMAGE_TAR}")"
CHUNK_PREFIX="${IMAGE_FILE_NAME}.part."
LOCAL_CHUNK_DIR="$(mktemp -d)"

cleanup() {
  rm -rf "${LOCAL_CHUNK_DIR}"
}
trap cleanup EXIT

compute_sha256() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
    return
  fi
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
    return
  fi
  echo "No SHA-256 tool found (sha256sum/shasum)." >&2
  exit 1
}

scp_with_retry() {
  local source_path="$1"
  local destination_path="$2"
  local attempt=1
  while true; do
    if scp "${SSH_OPTS[@]}" "${source_path}" "${destination_path}"; then
      return 0
    fi
    echo "SCP failed for ${source_path} (attempt ${attempt}). Retrying in ${SCP_RETRY_DELAY_SECONDS}s..."
    attempt=$((attempt + 1))
    sleep "${SCP_RETRY_DELAY_SECONDS}"
  done
}

mkdir -p "${ARTIFACTS_DIR}"

if [[ "${SKIP_BUILD}" == "true" ]]; then
  # Retry mode: skip build/save and reuse an existing archive.
  if [[ ! -f "${IMAGE_TAR}" ]]; then
    shopt -s nullglob
    MATCHING_ARCHIVES=( "${ARTIFACTS_DIR}/${IMAGE_NAME//\//_}-"*.tar.gz )
    shopt -u nullglob
    if [[ ${#MATCHING_ARCHIVES[@]} -eq 0 ]]; then
      echo "No existing image archive found in ${ARTIFACTS_DIR}. Run without --skip-build first." >&2
      exit 1
    fi
    IFS=$'\n' SORTED_ARCHIVES=( $(ls -1t "${MATCHING_ARCHIVES[@]}") )
    unset IFS
    IMAGE_TAR="${SORTED_ARCHIVES[0]}"
    IMAGE_FILE_NAME="$(basename "${IMAGE_TAR}")"
    CHUNK_PREFIX="${IMAGE_FILE_NAME}.part."
  fi
  echo "Skip-build mode enabled. Reusing image archive: ${IMAGE_TAR}"
else
  echo "Building image ${IMAGE_REF}..."
  docker build -f "${REPO_ROOT}/Web/Dockerfile" -t "${IMAGE_REF}" "${REPO_ROOT}"

  echo "Saving compressed image tar: ${IMAGE_TAR}"
  docker save "${IMAGE_REF}" | gzip -c > "${IMAGE_TAR}"
fi

IMAGE_TAR_SHA256="$(compute_sha256 "${IMAGE_TAR}")"
IMAGE_TAR_SIZE_BYTES="$(wc -c < "${IMAGE_TAR}" | tr -d '[:space:]')"

echo "Preparing remote directory ${REMOTE_DIR} on ${SSH_HOST}..."
ssh "${SSH_OPTS[@]}" "${SSH_HOST}" bash -s -- "${REMOTE_DIR}" <<'EOF'
set -euo pipefail
remote_dir="$1"
mkdir -p "${remote_dir}/chunks" "${remote_dir}/Caddy"
EOF

if [[ "${SKIP_UPLOAD}" == "true" ]]; then
  echo "Skip-upload mode enabled. Skipping chunked upload; assuming chunks already on server."
else
  echo "Preparing chunked archive upload (${CHUNK_SIZE_MB}MB chunks)..."
  split -b "${CHUNK_SIZE_MB}m" -d -a 5 "${IMAGE_TAR}" "${LOCAL_CHUNK_DIR}/${CHUNK_PREFIX}"
  shopt -s nullglob
  CHUNK_FILES=( "${LOCAL_CHUNK_DIR}/${CHUNK_PREFIX}"* )
  shopt -u nullglob
  if [[ ${#CHUNK_FILES[@]} -eq 0 ]]; then
    echo "Failed to split image archive into chunks." >&2
    exit 1
  fi

  echo "Uploading ${#CHUNK_FILES[@]} chunk(s)..."
  for chunk_file in "${CHUNK_FILES[@]}"; do
    scp_with_retry "${chunk_file}" "${SSH_HOST}:${REMOTE_DIR}/chunks/"
  done
fi

scp_with_retry "${REPO_ROOT}/docker-compose.hetzner.yml" "${SSH_HOST}:${REMOTE_DIR}/"
scp_with_retry "${REPO_ROOT}/Caddy/Caddyfile" "${SSH_HOST}:${REMOTE_DIR}/Caddy/"
scp_with_retry "${REPO_ROOT}/scripts/deploy-hetzner-remote.sh" "${SSH_HOST}:${REMOTE_DIR}/"

REMOTE_IMAGE_TAR="${REMOTE_DIR}/${IMAGE_FILE_NAME}"
echo "Running remote deployment..."
# Normalize line endings after scp (Windows CRLF -> Linux LF), then execute remotely.
ssh "${SSH_OPTS[@]}" "${SSH_HOST}" bash -s -- \
  "${REMOTE_DIR}" "${IMAGE_NAME}" "${IMAGE_TAG}" "${SPARK3DENT_PORT}" \
  "${REMOTE_IMAGE_TAR}" "${CHUNK_PREFIX}" "${IMAGE_TAR_SHA256}" "${IMAGE_TAR_SIZE_BYTES}" <<'EOF'
set -euo pipefail
remote_dir="$1"
image_name="$2"
image_tag="$3"
spark3dent_port="$4"
remote_image_tar="$5"
chunk_prefix="$6"
image_tar_sha256="$7"
image_tar_size_bytes="$8"

sed -i 's/\r$//' "${remote_dir}/deploy-hetzner-remote.sh"
chmod +x "${remote_dir}/deploy-hetzner-remote.sh"

IMAGE_NAME="${image_name}" IMAGE_TAG="${image_tag}" SPARK3DENT_PORT="${spark3dent_port}" \
REMOTE_DIR="${remote_dir}" IMAGE_TAR="${remote_image_tar}" IMAGE_TAR_CHUNK_PREFIX="${chunk_prefix}" \
IMAGE_TAR_SHA256="${image_tar_sha256}" IMAGE_TAR_SIZE_BYTES="${image_tar_size_bytes}" \
"${remote_dir}/deploy-hetzner-remote.sh"
EOF

echo "Deployment complete."
