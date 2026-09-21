#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Kills any running winui3 WinTabberUI, builds it, and launches the fresh build.

.DESCRIPTION
    A locked bin/ from a still-running instance is the most common reason a build fails
    (MSB3021/MSB3027) or silently launches a stale binary. This kills the process first so the
    build can actually overwrite it, then launches the exe that build just produced (not
    whatever happened to already exist under bin/).

    Usage:  ./run-winui3.ps1
#>

$ErrorActionPreference = 'Stop'

$running = Get-Process -Name 'WinTabberUI' -ErrorAction SilentlyContinue
if ($running) {
    $ids = ($running | ForEach-Object { $_.Id }) -join ', '
    Write-Host "WinTabberUI is already running (PID $ids) - killing it so the build can lock bin/." -ForegroundColor Yellow
    $running | Stop-Process -Force
    $running | Wait-Process -ErrorAction SilentlyContinue
}

$proj = Join-Path $PSScriptRoot 'winui3/WinTabberUI/WinTabberUI.csproj'
dotnet build $proj -c Debug -v q --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed - not launching." -ForegroundColor Red
    exit $LASTEXITCODE
}

$dll = Get-ChildItem -Path (Join-Path $PSScriptRoot 'winui3/WinTabberUI/bin') -Recurse -Filter 'WinTabberUI.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $dll) {
    Write-Host "No WinTabberUI.dll found under winui3/WinTabberUI/bin." -ForegroundColor Red
    exit 1
}

$exe = Join-Path $dll.DirectoryName 'WinTabberUI.exe'
if (-not (Test-Path $exe)) {
    Write-Host "No executable beside $($dll.Name)." -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "  running  $exe" -ForegroundColor Cyan
Write-Host ''

Start-Process -FilePath $exe
