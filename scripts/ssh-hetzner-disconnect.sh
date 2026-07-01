#!/usr/bin/env bash
# Remove the cached Hetzner ssh-agent settings from this machine.
set -euo pipefail

SSH_IDENTITY_FILE="${SSH_IDENTITY_FILE:-$HOME/.ssh/id_ed25519_hetzner}"
AGENT_ENV="${HOME}/.ssh/agent.env"

if [ -f "${AGENT_ENV}" ]; then
  # shellcheck disable=SC1090
  source "${AGENT_ENV}"
fi

if [ -n "${SSH_AUTH_SOCK:-}" ] && ssh-add -l >/dev/null 2>&1; then
  ssh-add -d "${SSH_IDENTITY_FILE}" >/dev/null 2>&1 || true
fi

rm -f "${AGENT_ENV}"
echo "Cleared cached Hetzner SSH agent settings."
