#!/usr/bin/env bash
# Ensures the local Docker Engine daemon is reachable before build/save steps.
# Safe to source from deploy scripts on Windows (Docker Desktop), macOS, and Linux.

ensure_docker_engine() {
  if docker info >/dev/null 2>&1; then
    return 0
  fi

  echo "Docker Engine is not running. Attempting to start it..."

  if try_start_docker_engine; then
    wait_for_docker_engine || return 1
    return 0
  fi

  echo "Could not start Docker Engine automatically." >&2
  echo "Start Docker Desktop (or your Docker service) and retry." >&2
  return 1
}

try_start_docker_engine() {
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*)
      start_docker_desktop_windows
      ;;
    Darwin*)
      if command -v open >/dev/null 2>&1; then
        open -a Docker >/dev/null 2>&1 || true
        return 0
      fi
      return 1
      ;;
    Linux*)
      start_docker_linux
      ;;
    *)
      return 1
      ;;
  esac
}

start_docker_desktop_windows() {
  local desktop_exe=""

  if [[ -n "${DOCKER_DESKTOP_EXE:-}" && -f "${DOCKER_DESKTOP_EXE}" ]]; then
    desktop_exe="${DOCKER_DESKTOP_EXE}"
  elif [[ -f "/c/Program Files/Docker/Docker/Docker Desktop.exe" ]]; then
    desktop_exe="/c/Program Files/Docker/Docker/Docker Desktop.exe"
  elif [[ -f "${ProgramFiles}/Docker/Docker/Docker Desktop.exe" ]]; then
    desktop_exe="${ProgramFiles}/Docker/Docker/Docker Desktop.exe"
  fi

  if [[ -z "${desktop_exe}" ]]; then
    echo "Docker Desktop executable not found." >&2
    return 1
  fi

  echo "Launching Docker Desktop..."
  if command -v powershell.exe >/dev/null 2>&1; then
    local ps_path="${desktop_exe//\//\\}"
    powershell.exe -NoProfile -Command "Start-Process -FilePath '${ps_path}'" >/dev/null
    return 0
  fi

  cmd.exe //c start "" "${desktop_exe}" >/dev/null
}

start_docker_linux() {
  if command -v systemctl >/dev/null 2>&1; then
    if systemctl --user is-active docker >/dev/null 2>&1 || systemctl is-active docker >/dev/null 2>&1; then
      return 0
    fi
    if systemctl --user start docker >/dev/null 2>&1; then
      return 0
    fi
    if systemctl start docker >/dev/null 2>&1; then
      return 0
    fi
  fi

  if command -v service >/dev/null 2>&1 && service docker start >/dev/null 2>&1; then
    return 0
  fi

  return 1
}

wait_for_docker_engine() {
  local timeout_seconds="${DOCKER_READY_TIMEOUT_SECONDS:-180}"
  local poll_seconds="${DOCKER_READY_POLL_SECONDS:-2}"
  local elapsed=0

  while ! docker info >/dev/null 2>&1; do
    if (( elapsed >= timeout_seconds )); then
      echo "Timed out after ${timeout_seconds}s waiting for Docker Engine." >&2
      return 1
    fi
    sleep "${poll_seconds}"
    elapsed=$((elapsed + poll_seconds))
  done

  echo "Docker Engine is ready."
  return 0
}
