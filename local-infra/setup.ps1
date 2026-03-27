#Requires -Version 5.1
<#
.SYNOPSIS
    Initializes the PostgreSQL database and applies Liquibase migrations via Docker Compose.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "==> Starting PostgreSQL and applying Liquibase migrations..." -ForegroundColor Cyan

Push-Location $repoRoot
try {
    # Bring up postgres (with health check) and run liquibase migrations
    docker compose up --wait postgres liquibase

    if ($LASTEXITCODE -ne 0) {
        Write-Error "docker compose failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host ""
    Write-Host "==> Database is ready and migrations have been applied." -ForegroundColor Green
}
finally {
    Pop-Location
}
