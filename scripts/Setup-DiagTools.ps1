#Requires -Version 7.0
<#
.SYNOPSIS
Bootstraps local diagnostic tooling for WinUI 3 crash dump analysis.

.DESCRIPTION
Creates symbol and dump folders, sets a persistent user-level symbol path,
installs ProcDump and WinDbg with winget when needed, installs the required
.NET diagnostic global tools when needed, verifies the commands are usable,
and prints a colorized summary of what was installed versus already present.
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'This script must be run on Windows.'
}

$symbolDirectory = 'C:\Symbols'
$dumpDirectory = 'C:\dumps'
$symbolPathValue = 'SRV*C:\Symbols*https://msdl.microsoft.com/download/symbols'
$dotnetToolPath = Join-Path $HOME '.dotnet\tools'
$commonExecutableRoots = @(
    (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'),
    (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps'),
    'C:\Program Files',
    'C:\Program Files (x86)'
) | Where-Object { Test-Path -LiteralPath $_ }

$summary = [ordered]@{
    Directories  = [System.Collections.Generic.List[object]]::new()
    Environment  = [System.Collections.Generic.List[object]]::new()
    Winget       = [System.Collections.Generic.List[object]]::new()
    DotNetTools  = [System.Collections.Generic.List[object]]::new()
    Verification = [System.Collections.Generic.List[object]]::new()
}

function Write-Status {
    param(
        [Parameter(Mandatory)]
        [string]$Message,

        [ValidateSet('Info', 'Success', 'Warning', 'Error')]
        [string]$Level = 'Info'
    )

    $color = switch ($Level) {
        'Info' { 'Cyan' }
        'Success' { 'Green' }
        'Warning' { 'Yellow' }
        'Error' { 'Red' }
    }

    Write-Host $Message -ForegroundColor $color
}

function Add-SummaryEntry {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Directories', 'Environment', 'Winget', 'DotNetTools', 'Verification')]
        [string]$Category,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Status,

        [string]$Details
    )

    $summary[$Category].Add([pscustomobject]@{
            Name    = $Name
            Status  = $Status
            Details = $Details
        })
}

function Get-ColorForStatus {
    param([string]$Status)

    switch ($Status) {
        'Created' { 'Green' }
        'Installed' { 'Green' }
        'Updated' { 'Green' }
        'Verified' { 'Green' }
        'Already present' { 'Yellow' }
        'Failed' { 'Red' }
        default { 'Gray' }
    }
}

function Refresh-ProcessPath {
    $segments = [System.Collections.Generic.List[string]]::new()

    foreach ($pathValue in @(
            [Environment]::GetEnvironmentVariable('Path', 'Machine'),
            [Environment]::GetEnvironmentVariable('Path', 'User'),
            $env:Path
        )) {
        foreach ($segment in ($pathValue -split ';')) {
            if (-not [string]::IsNullOrWhiteSpace($segment) -and $segments -notcontains $segment) {
                [void]$segments.Add($segment)
            }
        }
    }

    if ((Test-Path -LiteralPath $dotnetToolPath) -and $segments -notcontains $dotnetToolPath) {
        [void]$segments.Add($dotnetToolPath)
    }

    $env:Path = ($segments | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ';'
}

function Add-ProcessPathSegment {
    param([string]$PathSegment)

    if ([string]::IsNullOrWhiteSpace($PathSegment) -or -not (Test-Path -LiteralPath $PathSegment)) {
        return
    }

    $segments = $env:Path -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($segments -notcontains $PathSegment) {
        $env:Path = "$PathSegment;$env:Path"
    }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [string[]]$ArgumentList = @()
    )

    $output = & $FilePath @ArgumentList 2>&1
    $exitCode = $LASTEXITCODE

    [pscustomobject]@{
        ExitCode = $exitCode
        Output   = @($output | ForEach-Object { $_.ToString() })
    }
}

function Ensure-Directory {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path -PathType Container) {
        Write-Status "Directory already present: $Path" Warning
        Add-SummaryEntry -Category Directories -Name $Path -Status 'Already present'
        return
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Write-Status "Created directory: $Path" Success
    Add-SummaryEntry -Category Directories -Name $Path -Status 'Created'
}

function Ensure-UserEnvironmentVariable {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Value
    )

    $currentValue = [Environment]::GetEnvironmentVariable($Name, 'User')
    Set-Item -Path "Env:$Name" -Value $Value

    if ($currentValue -eq $Value) {
        Write-Status "User environment variable $Name already set." Warning
        Add-SummaryEntry -Category Environment -Name $Name -Status 'Already present' -Details $Value
        return
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, 'User')
    Write-Status "Set user environment variable $Name." Success
    Add-SummaryEntry -Category Environment -Name $Name -Status 'Updated' -Details $Value
}

function Resolve-CommandPath {
    param(
        [Parameter(Mandatory)][string]$CommandName,
        [string[]]$SearchRoots = @()
    )

    $command = Get-Command -Name $CommandName -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($command) {
        return $command.Source
    }

    foreach ($root in $SearchRoots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        $match = Get-ChildItem -Path $root -Filter "$CommandName.exe" -File -Recurse -Depth 5 -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName

        if ($match) {
            Add-ProcessPathSegment -PathSegment (Split-Path -Path $match -Parent)
            return $match
        }
    }

    return $null
}

function Test-WingetPackageInstalled {
    param([Parameter(Mandatory)][string[]]$PackageIds)

    foreach ($packageId in $PackageIds) {
        $result = Invoke-NativeCommand -FilePath 'winget' -ArgumentList @(
            'list',
            '--exact',
            '--id', $packageId,
            '--accept-source-agreements',
            '--disable-interactivity'
        )

        if ($result.ExitCode -eq 0 -and (($result.Output -join [Environment]::NewLine) -match [regex]::Escape($packageId))) {
            return $packageId
        }
    }

    return $null
}

function Resolve-WingetPackageId {
    param([Parameter(Mandatory)][string[]]$PackageIds)

    foreach ($packageId in $PackageIds) {
        $result = Invoke-NativeCommand -FilePath 'winget' -ArgumentList @(
            'search',
            '--exact',
            '--id', $packageId,
            '--accept-source-agreements',
            '--disable-interactivity'
        )

        if ($result.ExitCode -eq 0 -and (($result.Output -join [Environment]::NewLine) -match [regex]::Escape($packageId))) {
            return $packageId
        }
    }

    return $PackageIds[0]
}

function Get-ShortErrorText {
    param([string[]]$Lines)

    if (-not $Lines) {
        return $null
    }

    return (($Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 3) -join ' | ')
}

function Ensure-WingetTool {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$PackageIds,
        [string[]]$CommandNames = @(),
        [string[]]$SearchRoots = @()
    )

    if (-not (Get-Command -Name winget -CommandType Application -ErrorAction SilentlyContinue)) {
        Write-Status "winget is not available; skipping $Name." Error
        Add-SummaryEntry -Category Winget -Name $Name -Status 'Failed' -Details 'winget is not available in PATH.'
        return
    }

    foreach ($commandName in $CommandNames) {
        if (Resolve-CommandPath -CommandName $commandName -SearchRoots $SearchRoots) {
            Write-Status "$Name already present via command '$commandName'." Warning
            Add-SummaryEntry -Category Winget -Name $Name -Status 'Already present' -Details "Command '$commandName' is already available."
            return
        }
    }

    $installedPackageId = Test-WingetPackageInstalled -PackageIds $PackageIds
    if ($installedPackageId) {
        Write-Status "$Name already present via winget package $installedPackageId." Warning
        Add-SummaryEntry -Category Winget -Name $Name -Status 'Already present' -Details "Package ID: $installedPackageId"
        return
    }

    $packageId = Resolve-WingetPackageId -PackageIds $PackageIds
    Write-Status "Installing $Name via winget ($packageId)..." Info

    $result = Invoke-NativeCommand -FilePath 'winget' -ArgumentList @(
        'install',
        '--exact',
        '--id', $packageId,
        '--accept-package-agreements',
        '--accept-source-agreements',
        '--disable-interactivity'
    )

    $outputText = $result.Output -join [Environment]::NewLine
    if ($result.ExitCode -eq 0) {
        Write-Status "Installed $Name." Success
        Add-SummaryEntry -Category Winget -Name $Name -Status 'Installed' -Details "Package ID: $packageId"
        Refresh-ProcessPath
        foreach ($commandName in $CommandNames) {
            [void](Resolve-CommandPath -CommandName $commandName -SearchRoots $SearchRoots)
        }
        return
    }

    if ($outputText -match 'already installed|No applicable upgrade found') {
        Write-Status "$Name is already present according to winget." Warning
        Add-SummaryEntry -Category Winget -Name $Name -Status 'Already present' -Details "Package ID: $packageId"
        return
    }

    $details = Get-ShortErrorText -Lines $result.Output
    if ($outputText -match 'elevation|administrator|0x8a150042|0x80073d02') {
        $details = if ($details) { "May require elevation or app install permissions. $details" } else { 'May require elevation or app install permissions.' }
    }

    Write-Status "Failed to install $Name." Error
    Add-SummaryEntry -Category Winget -Name $Name -Status 'Failed' -Details $details
}

function Get-InstalledDotNetToolNames {
    if (-not (Get-Command -Name dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
        return @()
    }

    $result = Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList @('tool', 'list', '--global')
    if ($result.ExitCode -ne 0) {
        return @()
    }

    return @(
        $result.Output |
            Where-Object { $_ -match '^dotnet-[\w.-]+\s+' } |
            ForEach-Object { ($_ -split '\s+')[0] }
    )
}

function Ensure-DotNetTool {
    param([Parameter(Mandatory)][string]$PackageId)

    if (-not (Get-Command -Name dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
        Write-Status "dotnet is not available; skipping $PackageId." Error
        Add-SummaryEntry -Category DotNetTools -Name $PackageId -Status 'Failed' -Details 'dotnet is not available in PATH.'
        return
    }

    $installedTools = Get-InstalledDotNetToolNames
    if ($installedTools -contains $PackageId) {
        Write-Status "$PackageId already present." Warning
        Add-SummaryEntry -Category DotNetTools -Name $PackageId -Status 'Already present'
        return
    }

    Write-Status "Installing .NET global tool $PackageId..." Info
    $result = Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList @('tool', 'install', '--global', $PackageId)

    if ($result.ExitCode -eq 0) {
        Write-Status "Installed $PackageId." Success
        Add-SummaryEntry -Category DotNetTools -Name $PackageId -Status 'Installed'
        Refresh-ProcessPath
        return
    }

    $outputText = $result.Output -join [Environment]::NewLine
    if ($outputText -match 'is already installed') {
        Write-Status "$PackageId is already installed." Warning
        Add-SummaryEntry -Category DotNetTools -Name $PackageId -Status 'Already present'
        return
    }

    Write-Status "Failed to install $PackageId." Error
    Add-SummaryEntry -Category DotNetTools -Name $PackageId -Status 'Failed' -Details (Get-ShortErrorText -Lines $result.Output)
}

function Verify-Command {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Invocation,
        [Parameter(Mandatory)][scriptblock]$SuccessPredicate
    )

    try {
        $result = & $Invocation
        $value = & $SuccessPredicate $result
        if ($value) {
            Write-Status "Verified $Name." Success
            Add-SummaryEntry -Category Verification -Name $Name -Status 'Verified' -Details $value
            return
        }

        Write-Status "Verification failed for $Name." Error
        Add-SummaryEntry -Category Verification -Name $Name -Status 'Failed' -Details 'Unexpected output.'
    }
    catch {
        Write-Status "Verification failed for $Name." Error
        Add-SummaryEntry -Category Verification -Name $Name -Status 'Failed' -Details $_.Exception.Message
    }
}

function Show-SummarySection {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][System.Collections.IEnumerable]$Items
    )

    Write-Host "`n$Title" -ForegroundColor Cyan
    foreach ($item in $Items) {
        $line = "- $($item.Name): $($item.Status)"
        if ($item.Details) {
            $line += " ($($item.Details))"
        }

        Write-Host $line -ForegroundColor (Get-ColorForStatus -Status $item.Status)
    }
}

Write-Status 'Preparing WinUI 3 diagnostic tooling...' Info

Ensure-Directory -Path $symbolDirectory
Ensure-Directory -Path $dumpDirectory
Ensure-UserEnvironmentVariable -Name '_NT_SYMBOL_PATH' -Value $symbolPathValue
Refresh-ProcessPath

Ensure-WingetTool -Name 'ProcDump' -PackageIds @('Sysinternals.ProcDump', 'Microsoft.Sysinternals.ProcDump', 'Microsoft.ProcDump') -CommandNames @('procdump') -SearchRoots $commonExecutableRoots
Ensure-WingetTool -Name 'WinDbg' -PackageIds @('Microsoft.WinDbg') -CommandNames @('windbg') -SearchRoots $commonExecutableRoots

Ensure-DotNetTool -PackageId 'dotnet-dump'
Ensure-DotNetTool -PackageId 'dotnet-trace'
Ensure-DotNetTool -PackageId 'dotnet-counters'

Refresh-ProcessPath
[void](Resolve-CommandPath -CommandName 'procdump' -SearchRoots $commonExecutableRoots)

Verify-Command -Name 'procdump' -Invocation {
    & procdump '-?' 2>$null | Out-Null
    if ($LASTEXITCODE -in 0, 1) { 'help' } else { $null }
} -SuccessPredicate {
    param($result)
    if ($result) { 'help available' } else { $null }
}

Verify-Command -Name 'dotnet-dump' -Invocation {
    (& dotnet-dump '--version').Trim()
} -SuccessPredicate {
    param($result)
    if ($result) { $result } else { $null }
}

Verify-Command -Name 'dotnet-trace' -Invocation {
    (& dotnet-trace '--version').Trim()
} -SuccessPredicate {
    param($result)
    if ($result) { $result } else { $null }
}

Verify-Command -Name 'dotnet-counters' -Invocation {
    (& dotnet-counters '--version').Trim()
} -SuccessPredicate {
    param($result)
    if ($result) { $result } else { $null }
}

Show-SummarySection -Title 'Directory setup' -Items $summary.Directories
Show-SummarySection -Title 'Environment' -Items $summary.Environment
Show-SummarySection -Title 'Winget tools' -Items $summary.Winget
Show-SummarySection -Title '.NET global tools' -Items $summary.DotNetTools
Show-SummarySection -Title 'Verification' -Items $summary.Verification

$failedItems = @(
    $summary.Directories
    $summary.Environment
    $summary.Winget
    $summary.DotNetTools
    $summary.Verification
) | ForEach-Object { $_ } | Where-Object Status -eq 'Failed'

$failedCount = @($failedItems).Count

if ($failedCount -gt 0) {
    Write-Status "Completed with $failedCount issue(s). Review the summary above." Error
    exit 1
}

Write-Status 'Diagnostic tooling setup completed successfully.' Success
