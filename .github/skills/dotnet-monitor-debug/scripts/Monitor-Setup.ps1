#Requires -Version 7.0
<#
.SYNOPSIS
    Verify and install dotnet-monitor global tool.
.DESCRIPTION
    Checks for .NET SDK and dotnet-monitor. Installs dotnet-monitor if missing.
.EXAMPLE
    pwsh scripts/Monitor-Setup.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host '=== dotnet-monitor Setup ==='

# Check .NET SDK
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Error '.NET SDK not found. Install from https://dot.net/download'
    exit 1
}

$sdkVersion = & dotnet --version
Write-Host "✓ .NET SDK: $sdkVersion"

# Check if dotnet-monitor is installed
$toolList = & dotnet tool list -g 2>&1
if ($toolList -match 'dotnet-monitor') {
    $monitorVersion = try { & dotnet monitor --version 2>&1 } catch { 'unknown' }
    Write-Host "✓ dotnet-monitor already installed: $monitorVersion"
} else {
    Write-Host 'dotnet-monitor not found. Installing...'
    & dotnet tool install -g dotnet-monitor
    if ($LASTEXITCODE -ne 0) { Write-Error 'Failed to install dotnet-monitor'; exit 1 }
    $monitorVersion = try { & dotnet monitor --version 2>&1 } catch { 'unknown' }
    Write-Host "✓ dotnet-monitor installed: $monitorVersion"
}

# Verify it runs
try {
    $null = & dotnet monitor --version 2>&1
    Write-Host '✓ dotnet-monitor is functional'
} catch {
    Write-Warning 'dotnet-monitor installed but may not be on PATH'
    Write-Host '  Add ~/.dotnet/tools to your PATH'
}

Write-Host ''
Write-Host 'Setup complete. Launch with:'
Write-Host '  dotnet monitor collect -- dotnet run --project ./src/YourApp'
