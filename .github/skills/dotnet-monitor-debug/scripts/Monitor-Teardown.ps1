#Requires -Version 7.0
<#
.SYNOPSIS
    Kill dotnet-monitor sidecar and collect diagnostic artifacts.
.DESCRIPTION
    Stops any running dotnet-monitor processes and moves diagnostic
    artifacts (.dmp, .nettrace, .gcdump, logs.txt) to a timestamped
    directory under the specified artifacts path.
.PARAMETER ArtifactsDir
    Base directory for collected artifacts (default: ./diagnostics).
.EXAMPLE
    pwsh scripts/Monitor-Teardown.ps1
.EXAMPLE
    pwsh scripts/Monitor-Teardown.ps1 -ArtifactsDir ./my-diagnostics
#>

[CmdletBinding()]
param(
    [string]$ArtifactsDir = './diagnostics'
)

$ErrorActionPreference = 'Stop'

Write-Host '=== dotnet-monitor Teardown ==='

# Kill dotnet-monitor processes
$monitorProcs = Get-Process -Name 'dotnet-monitor' -ErrorAction SilentlyContinue
if ($monitorProcs) {
    $monitorProcs | Stop-Process -Force
    Write-Host "✓ dotnet-monitor process(es) killed (PIDs: $($monitorProcs.Id -join ', '))"
} else {
    Write-Host '⚠ No dotnet-monitor process found (may have already exited)'
}

# Collect diagnostic artifacts from current directory
$timestamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$dest = Join-Path $ArtifactsDir $timestamp
$collected = 0

$extensions = @('*.dmp', '*.nettrace', '*.gcdump')
foreach ($ext in $extensions) {
    $files = Get-ChildItem -Path . -Filter $ext -File -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
        Move-Item -Path $f.FullName -Destination $dest
        Write-Host "  Moved: $($f.Name) -> $dest/"
        $collected++
    }
}

# Also collect logs.txt
if (Test-Path './logs.txt') {
    if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
    Move-Item -Path './logs.txt' -Destination $dest
    Write-Host '  Moved: logs.txt -> $dest/'
    $collected++
}

if ($collected -gt 0) {
    Write-Host ''
    Write-Host "✓ $collected artifact(s) saved to $dest/"
    Write-Host ''
    Write-Host 'Contents:'
    Get-ChildItem -Path $dest | Format-Table Name, Length -AutoSize
} else {
    Write-Host ''
    Write-Host 'No diagnostic artifacts found in current directory.'
}

Write-Host ''
Write-Host 'Teardown complete.'
