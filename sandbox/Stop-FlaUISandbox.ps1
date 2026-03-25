<#
.SYNOPSIS
    Stops Windows Sandbox and removes the sandbox entry from mcp.json.
.PARAMETER McpJsonPath
    Path to Amplifier's mcp.json. Default: ~/.amplifier/mcp.json.
#>
param(
    [string]$McpJsonPath = (Join-Path $env:USERPROFILE ".amplifier" "mcp.json")
)

$ErrorActionPreference = "Stop"
$stateFile = Join-Path $PSScriptRoot ".sandbox-state.json"

# Read sandbox state
if (-not (Test-Path $stateFile)) {
    Write-Error "No sandbox state found. Is a sandbox running? State file: $stateFile"
    exit 1
}

$state = Get-Content $stateFile -Raw | ConvertFrom-Json
Write-Host "Stopping sandbox $($state.Id)..."

# Stop the sandbox
try {
    wsb stop --id $state.Id
    Write-Host "Sandbox stopped."
}
catch {
    Write-Warning "Failed to stop sandbox (may already be stopped): $_"
}

# Clean up mcp.json
if (Test-Path $McpJsonPath) {
    Write-Host "Removing 'windows-automation-sandbox' from $McpJsonPath..."
    $mcpConfig = Get-Content $McpJsonPath -Raw | ConvertFrom-Json

    if ($mcpConfig.PSObject.Properties["windows-automation-sandbox"]) {
        $mcpConfig.PSObject.Properties.Remove("windows-automation-sandbox")
        $mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $McpJsonPath -Encoding UTF8
        Write-Host "Entry removed."
    }
    else {
        Write-Host "Entry not found in mcp.json (already clean)."
    }
}

# Clean up state file
Remove-Item $stateFile -Force
Write-Host ""
Write-Host "Sandbox teardown complete."
