#!/usr/bin/env bash
# Load ssh-agent with the Hetzner deploy key, reusing ~/.ssh/agent.env when valid.
set -euo pipefail

SSH_IDENTITY_FILE="${SSH_IDENTITY_FILE:-$HOME/.ssh/id_ed25519_hetzner}"
AGENT_ENV="${HOME}/.ssh/agent.env"

agent_responds() {
  [ -n "${SSH_AUTH_SOCK:-}" ] && ssh-add -l >/dev/null 2>&1
}

hetzner_key_loaded() {
  agent_responds || return 1
  ssh-add -l 2>/dev/null | grep -qE 'hetzner-vps|id_ed25519_hetzner'
}

load_saved_agent() {
  [ -f "${AGENT_ENV}" ] || return 1
  # shellcheck disable=SC1090
  source "${AGENT_ENV}"
  agent_responds
}

save_agent_env() {
  {
    echo "SSH_AUTH_SOCK=${SSH_AUTH_SOCK}; export SSH_AUTH_SOCK;"
    echo "SSH_AGENT_PID=${SSH_AGENT_PID}; export SSH_AGENT_PID;"
  } > "${AGENT_ENV}"
}

start_agent_with_key() {
  eval "$(ssh-agent -s)" >/dev/null
  ssh-add "${SSH_IDENTITY_FILE}"
  save_agent_env
}

ensure_hetzner_ssh_agent() {
  if hetzner_key_loaded; then
    return 0
  fi

  if load_saved_agent && hetzner_key_loaded; then
    return 0
  fi

  echo "Loading Hetzner SSH key into ssh-agent (passphrase prompted once)..."
  start_agent_with_key
}

if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  ensure_hetzner_ssh_agent
fi
