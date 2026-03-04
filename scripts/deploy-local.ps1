param(
    [Parameter(Position = 0)]
    [ValidateSet("up", "down", "restart", "logs", "ps")]
    [string]$Command = "up",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ComposeFile = Join-Path $RepoRoot "docker-compose.local.yml"

if (-not (Test-Path $ComposeFile)) {
    throw "Compose file not found: $ComposeFile"
}

function Ensure-LocalDirs {
    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot ".docker/local/data") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot ".docker/local/blobs") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot ".docker/local/logs") | Out-Null
}

switch ($Command) {
    "up" {
        Ensure-LocalDirs
        Write-Host "Starting local stack..."
        if ($NoBuild) {
            docker compose -f $ComposeFile up -d --remove-orphans
        }
        else {
            docker compose -f $ComposeFile up -d --build --remove-orphans
        }
        docker compose -f $ComposeFile ps
    }
    "down" {
        Write-Host "Stopping local stack..."
        docker compose -f $ComposeFile down
    }
    "restart" {
        Ensure-LocalDirs
        Write-Host "Restarting local stack..."
        docker compose -f $ComposeFile down
        docker compose -f $ComposeFile up -d --build --remove-orphans
        docker compose -f $ComposeFile ps
    }
    "logs" {
        docker compose -f $ComposeFile logs -f
    }
    "ps" {
        docker compose -f $ComposeFile ps
    }
}
