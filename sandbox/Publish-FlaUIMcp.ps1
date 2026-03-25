<#
.SYNOPSIS
    Publishes FlaUI-MCP as a self-contained exe for Windows Sandbox deployment.
.DESCRIPTION
    Produces a standalone Windows x64 binary at publish/ that runs without
    .NET SDK or runtime installed. Map publish/ into the sandbox and run directly.
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path (Join-Path $PSScriptRoot "..") "publish")
)

$ErrorActionPreference = "Stop"
$ProjectDir = Join-Path (Join-Path (Join-Path $PSScriptRoot "..") "src") "FlaUI.Mcp"

Write-Host "Publishing FlaUI-MCP (self-contained, win-x64)..."
Write-Host "  Project: $ProjectDir"
Write-Host "  Output:  $OutputDir"

dotnet publish $ProjectDir `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host ""
Write-Host "Published successfully to: $OutputDir"
Write-Host "Binary: $(Join-Path $OutputDir 'FlaUI.Mcp.exe')"
Write-Host ""
Write-Host "To test HTTP mode locally:"
Write-Host "  & '$(Join-Path $OutputDir 'FlaUI.Mcp.exe')' --port 8765"
