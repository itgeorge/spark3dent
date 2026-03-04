#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/docker-compose.local.yml"

ensure_dirs() {
  mkdir -p \
    "${REPO_ROOT}/.docker/local/data" \
    "${REPO_ROOT}/.docker/local/blobs" \
    "${REPO_ROOT}/.docker/local/logs"
}

usage() {
  cat <<'EOF'
Usage:
  scripts/deploy-local.sh up [--no-build]
  scripts/deploy-local.sh down
  scripts/deploy-local.sh restart
  scripts/deploy-local.sh logs
  scripts/deploy-local.sh ps

Commands:
  up         Start local stack (builds image by default)
  down       Stop and remove local stack containers
  restart    Recreate local stack (down + up)
  logs       Follow local stack logs
  ps         Show local stack status

Options:
  --no-build Skip image build during 'up'
EOF
}

if [[ ! -f "${COMPOSE_FILE}" ]]; then
  echo "Compose file not found: ${COMPOSE_FILE}" >&2
  exit 1
fi

COMMAND="${1:-up}"
shift || true

NO_BUILD=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      NO_BUILD=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

case "${COMMAND}" in
  up)
    ensure_dirs
    echo "Starting local stack..."
    if [[ "${NO_BUILD}" == "true" ]]; then
      docker compose -f "${COMPOSE_FILE}" up -d --remove-orphans
    else
      docker compose -f "${COMPOSE_FILE}" up -d --build --remove-orphans
    fi
    docker compose -f "${COMPOSE_FILE}" ps
    ;;
  down)
    echo "Stopping local stack..."
    docker compose -f "${COMPOSE_FILE}" down
    ;;
  restart)
    ensure_dirs
    echo "Restarting local stack..."
    docker compose -f "${COMPOSE_FILE}" down
    docker compose -f "${COMPOSE_FILE}" up -d --build --remove-orphans
    docker compose -f "${COMPOSE_FILE}" ps
    ;;
  logs)
    docker compose -f "${COMPOSE_FILE}" logs -f
    ;;
  ps)
    docker compose -f "${COMPOSE_FILE}" ps
    ;;
  -h|--help|help)
    usage
    ;;
  *)
    echo "Unknown command: ${COMMAND}" >&2
    usage
    exit 1
    ;;
esac
