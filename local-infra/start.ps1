#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the .NET solution, runs tests, and if successful launches the Blazor UI in a browser.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$solution  = Join-Path $repoRoot 'src\LegoList.sln'
$blazorUrl = 'http://localhost:5164'

# ---------------------------------------------------------------------------
# 1. Build
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Building solution..." -ForegroundColor Cyan
dotnet build $solution --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Aborting."
    exit $LASTEXITCODE
}

Write-Host "==> Build succeeded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Test
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Running tests..." -ForegroundColor Cyan

# Discover test projects — any *Tests* or *Test* csproj under src/
$testProjects = @(Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -Filter '*.csproj' |
    Where-Object { $_.BaseName -match 'Tests?' })

if ($testProjects.Count -eq 0) {
    Write-Host "    No test projects found — skipping." -ForegroundColor Yellow
} else {
    foreach ($proj in $testProjects) {
        Write-Host "    Testing $($proj.Name)..."
        dotnet test $proj.FullName --configuration Release --no-build

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Tests failed in $($proj.Name). Aborting."
            exit $LASTEXITCODE
        }
    }
    Write-Host "==> All tests passed." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 3. Launch the Blazor UI
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==> Launching Blazor UI at $blazorUrl ..." -ForegroundColor Cyan

$blazorProject = Join-Path $repoRoot 'src\LegoList.Blazor\LegoList.Blazor.csproj'

# Open the browser a few seconds after the server starts
$browserJob = Start-Job -ScriptBlock {
    param($url)
    Start-Sleep -Seconds 5
    Start-Process $url
} -ArgumentList $blazorUrl

try {
    dotnet run --project $blazorProject --configuration Release --launch-profile http
}
finally {
    $browserJob | Remove-Job -Force
}
