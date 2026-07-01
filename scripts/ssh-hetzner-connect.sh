#!/usr/bin/env bash
# Load the Hetzner SSH key into ssh-agent once; later ssh/scp skip passphrase prompts.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/ssh-hetzner-agent.sh"

HOST="${SSH_HOST:-spark3dent-hetzner}"
AGENT_ENV="${HOME}/.ssh/agent.env"

ensure_hetzner_ssh_agent

echo "Verifying passwordless SSH to ${HOST}..."
if ! ssh -o BatchMode=yes -o ConnectTimeout=15 "${HOST}" 'echo connected'; then
  echo "Could not connect to ${HOST} using the loaded key." >&2
  echo "Check that ${SSH_IDENTITY_FILE} is authorized on the server." >&2
  exit 1
fi

echo "SSH is ready for ${HOST}."
echo "Agent settings saved to ${AGENT_ENV}."
echo "Other shells can reuse it with: source ~/.ssh/agent.env"
