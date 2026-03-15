#Requires -Version 7.0
<#
.SYNOPSIS
    Launch dotnet-monitor sidecar, detect ports, and poll until the target process is ready.
.DESCRIPTION
    Starts dotnet-monitor in sidecar mode alongside the target .NET app.
    Polls the /processes endpoint until a monitored process is detected.
.PARAMETER ProjectPath
    Path to the .NET project to run (default: current directory).
.PARAMETER CollectionPort
    Collection API port (default: 52323, or $env:DOTNETMONITOR_PORT).
.PARAMETER MetricsPort
    Metrics API port (default: 52325, or $env:DOTNETMONITOR_METRICS_PORT).
.PARAMETER NoAuth
    Disable authentication (default: true for local debugging).
    Set to $false in production environments.
.PARAMETER MaxRetries
    Number of polling attempts before giving up (default: 20).
.PARAMETER RetryDelay
    Seconds between polling attempts (default: 2).
.EXAMPLE
    pwsh scripts/Monitor-Collect.ps1 -ProjectPath ./src/MyApp
.EXAMPLE
    pwsh scripts/Monitor-Collect.ps1 -ProjectPath ./src/MyApp -NoAuth:$false
#>

[CmdletBinding()]
param(
    [string]$ProjectPath = '.',
    [int]$CollectionPort = ($env:DOTNETMONITOR_PORT ?? 52323),
    [int]$MetricsPort = ($env:DOTNETMONITOR_METRICS_PORT ?? 52325),
    [switch]$NoAuth = $true,
    [int]$MaxRetries = 20,
    [int]$RetryDelay = 2
)

$ErrorActionPreference = 'Stop'

Write-Host '=== dotnet-monitor Collect ==='
Write-Host "Project:    $ProjectPath"
Write-Host "Collection: http://localhost:$CollectionPort"
Write-Host "Metrics:    http://localhost:$MetricsPort"
Write-Host ''

# Set diagnostic env var if not already set
if (-not $env:Logging__EventSource__LogLevel__Default) {
    $env:Logging__EventSource__LogLevel__Default = 'Trace'
    Write-Host '✓ Set Logging__EventSource__LogLevel__Default=Trace'
}

# Build the dotnet monitor command arguments
$monitorArgs = @(
    'monitor', 'collect'
    '--urls', "http://localhost:$CollectionPort"
    '--metricUrls', "http://localhost:$MetricsPort"
)
if ($NoAuth) { $monitorArgs += '--no-auth' }
$monitorArgs += @('--', 'dotnet', 'run', '--project', $ProjectPath)

# Launch dotnet-monitor as a background process
Write-Host 'Launching dotnet-monitor sidecar...'
$process = Start-Process -FilePath 'dotnet' -ArgumentList $monitorArgs -PassThru -NoNewWindow
Write-Host "✓ dotnet-monitor started (PID: $($process.Id))"

# Poll /processes until a process appears
Write-Host 'Waiting for target process...'
$collectionUrl = "http://localhost:$CollectionPort"
$metricsUrl = "http://localhost:$MetricsPort"

for ($i = 1; $i -le $MaxRetries; $i++) {
    Start-Sleep -Seconds $RetryDelay

    try {
        $response = Invoke-RestMethod -Uri "$collectionUrl/processes" -TimeoutSec 5 -ErrorAction Stop
    } catch {
        $response = @()
    }

    if ($response.Count -gt 0) {
        Write-Host "✓ Target process detected ($($response.Count) process(es))"
        Write-Host ''
        $response | ConvertTo-Json -Depth 3 | Write-Host
        Write-Host ''

        $targetPid = $response[0].pid
        Write-Host '=== Ready ==='
        Write-Host "Monitor PID:  $($process.Id)"
        Write-Host "Target PID:   $targetPid"
        Write-Host "Collection:   $collectionUrl"
        Write-Host "Metrics:      $metricsUrl"
        Write-Host ''
        Write-Host 'Quick commands:'
        Write-Host "  Invoke-RestMethod $metricsUrl/metrics"
        Write-Host "  Invoke-RestMethod $collectionUrl/processes"
        Write-Host "  Invoke-WebRequest -OutFile dump.dmp '$collectionUrl/dump?pid=$targetPid&type=Full'"
        exit 0
    }

    Write-Host "  Retry $i/$MaxRetries..."
}

$totalWait = $MaxRetries * $RetryDelay
Write-Warning "No processes detected after ${totalWait}s"
Write-Host 'dotnet-monitor may still be starting. Check manually:'
Write-Host "  Invoke-RestMethod $collectionUrl/processes"
exit 1
