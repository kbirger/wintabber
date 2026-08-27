#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and launches the current checkout, proving the binary matches HEAD before running it.

.DESCRIPTION
    Two ways this went wrong before, both now closed:

    1. The output lives in bin/x64/Debug/, not bin/Debug/. An earlier AnyCPU build had left a
       stale copy in bin/Debug/ and this script kept launching it, so several rounds of testing
       exercised hours-old code.

    2. Timestamps are not evidence. WinTabberUI.exe is the native apphost and is identical
       regardless of code changes, and MSBuild legitimately skips the copy when content is
       unchanged.

    So instead of trusting paths or timestamps, the build stamps HEAD's SHA into the assembly
    via SourceRevisionId and this script refuses to launch unless the built DLL reports that
    exact SHA. If it ever says "assembly does not match HEAD", the binary is stale -- do not
    trust the test.

    Usage:  git checkout <sha>; ./check.ps1
#>

$ErrorActionPreference = 'Stop'

$running = Get-Process -Name 'WinTabberUI' -ErrorAction SilentlyContinue
if ($running) {
    $ids = ($running | ForEach-Object { $_.Id }) -join ', '
    Write-Host "WinTabberUI is already running (PID $ids)." -ForegroundColor Red
    Write-Host "Close it first - its lock on bin/ makes the build fail on the copy step." -ForegroundColor Red
    exit 1
}

$sha = (git rev-parse HEAD).Trim()

dotnet build WinTabberUI/WinTabberUI.csproj -v q --nologo -p:SourceRevisionId=$sha
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed - not launching." -ForegroundColor Red
    exit $LASTEXITCODE
}

# Located by search rather than hard-coded: the platform subdirectory has bitten us once already.
$dll = Get-ChildItem -Path (Join-Path $PSScriptRoot 'WinTabberUI/bin') -Recurse -Filter 'WinTabberUI.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $dll) {
    Write-Host "No WinTabberUI.dll found under WinTabberUI/bin." -ForegroundColor Red
    exit 1
}

$stamped = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll.FullName).ProductVersion
if ($stamped -notlike "*$sha*") {
    Write-Host "Assembly does not match HEAD - refusing to launch a stale binary." -ForegroundColor Red
    Write-Host "  HEAD     $sha" -ForegroundColor Red
    Write-Host "  assembly $stamped" -ForegroundColor Red
    Write-Host "  path     $($dll.FullName)" -ForegroundColor Red
    Write-Host "Try: Remove-Item -Recurse -Force WinTabberUI/obj, WinTabberUI/bin" -ForegroundColor Yellow
    exit 1
}

$exe = Join-Path $dll.DirectoryName 'WinTabberUI.exe'
if (-not (Test-Path $exe)) {
    Write-Host "No executable beside $($dll.Name)." -ForegroundColor Red
    exit 1
}

$trace = Join-Path $env:TEMP 'wintabber-trace.log'
Remove-Item $trace -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "  commit   $($sha.Substring(0,7))  $(git log -1 --format=%s)" -ForegroundColor Cyan
Write-Host "  verified assembly stamped with this SHA" -ForegroundColor Green
Write-Host "  running  $($dll.DirectoryName)" -ForegroundColor Cyan
Write-Host ''

Start-Process -FilePath $exe
