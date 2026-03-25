<#
.SYNOPSIS
    Starts Windows Sandbox with FlaUI-MCP in HTTP mode.
.DESCRIPTION
    1. Publishes FlaUI-MCP (if needed)
    2. Starts Windows Sandbox with published binary mapped
    3. Launches FlaUI-MCP inside sandbox on specified port
    4. Discovers sandbox IP
    5. Polls until HTTP endpoint is ready
    6. Adds windows-automation-sandbox entry to Amplifier mcp.json
.PARAMETER Port
    Port for FlaUI-MCP HTTP server inside sandbox. Default: 8765.
.PARAMETER McpJsonPath
    Path to Amplifier's mcp.json. Default: ~/.amplifier/mcp.json.
.PARAMETER SkipPublish
    Skip the dotnet publish step (use existing publish/ output).
#>
param(
    [int]$Port = 8765,
    [string]$McpJsonPath = (Join-Path $env:USERPROFILE ".amplifier" "mcp.json"),
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$PublishDir = Join-Path $RepoRoot "publish"

# Step 1: Publish if needed
if (-not $SkipPublish) {
    Write-Host "Publishing FlaUI-MCP..."
    & (Join-Path $PSScriptRoot "Publish-FlaUIMcp.ps1")
}

if (-not (Test-Path (Join-Path $PublishDir "FlaUI.Mcp.exe"))) {
    Write-Error "FlaUI.Mcp.exe not found in $PublishDir. Run Publish-FlaUIMcp.ps1 first."
    exit 1
}

# Step 2: Start Windows Sandbox
Write-Host "Starting Windows Sandbox..."
$wsbConfig = @"
<Configuration>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$PublishDir</HostFolder>
      <SandboxFolder>C:\FlaUI-MCP</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
</Configuration>
"@

$tempWsb = Join-Path $env:TEMP "flaui-mcp-sandbox.wsb"
$wsbConfig | Set-Content $tempWsb -Encoding UTF8

$sandboxResult = wsb start --config $tempWsb --raw | ConvertFrom-Json
$sandboxId = $sandboxResult.Id
Remove-Item $tempWsb -Force -ErrorAction SilentlyContinue
Write-Host "Sandbox started with ID: $sandboxId"

# Step 3: Launch FlaUI-MCP inside sandbox
Write-Host "Launching FlaUI-MCP on port $Port inside sandbox..."
wsb exec --id $sandboxId --command "C:\FlaUI-MCP\FlaUI.Mcp.exe --port $Port" --run-as ExistingLogin

# Step 4: Get sandbox IP
$ipResult = wsb ip --id $sandboxId --raw | ConvertFrom-Json
$sandboxIp = $ipResult.IpAddress
Write-Host "Sandbox IP: $sandboxIp"

# Step 5: Poll until ready
$url = "http://${sandboxIp}:${Port}"
Write-Host "Waiting for FlaUI-MCP to be ready at $url..."
$maxAttempts = 30
$attempt = 0
$ready = $false

while ($attempt -lt $maxAttempts -and -not $ready) {
    $attempt++
    try {
        $response = Invoke-WebRequest -Uri "$url/mcp" -Method GET -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
            $ready = $true
        }
    }
    catch {
        # Connection refused or timeout — server not ready yet
    }
    if (-not $ready) {
        Write-Host "  Attempt $attempt/$maxAttempts — not ready yet..."
        Start-Sleep -Seconds 2
    }
}

if (-not $ready) {
    Write-Error "FlaUI-MCP did not become ready within $($maxAttempts * 2) seconds."
    Write-Host "Stopping sandbox..."
    wsb stop --id $sandboxId
    exit 1
}

Write-Host "FlaUI-MCP is ready!"

# Step 6: Update mcp.json
Write-Host "Updating $McpJsonPath..."
if (Test-Path $McpJsonPath) {
    $mcpConfig = Get-Content $McpJsonPath -Raw | ConvertFrom-Json
}
else {
    $mcpConfig = @{}
}

# Add or update the sandbox entry
$mcpConfig | Add-Member -NotePropertyName "windows-automation-sandbox" -NotePropertyValue @{
    url = $url
} -Force

# Ensure .amplifier directory exists (new Amplifier installs may not have it yet)
$mcpDir = Split-Path $McpJsonPath -Parent
if (-not (Test-Path $mcpDir)) {
    New-Item -ItemType Directory -Force -Path $mcpDir | Out-Null
}

$mcpConfig | ConvertTo-Json -Depth 10 | Set-Content $McpJsonPath -Encoding UTF8
Write-Host "Added 'windows-automation-sandbox' entry pointing to $url"

# Save sandbox ID for stop script
$stateFile = Join-Path $PSScriptRoot ".sandbox-state.json"
@{ Id = $sandboxId; Ip = $sandboxIp; Port = $Port } | ConvertTo-Json | Set-Content $stateFile

Write-Host ""
Write-Host "=== Sandbox Ready ==="
Write-Host "  Sandbox ID: $sandboxId"
Write-Host "  FlaUI-MCP:  $url"
Write-Host "  mcp.json:   $McpJsonPath"
Write-Host "  To stop:    .\Stop-FlaUISandbox.ps1"
