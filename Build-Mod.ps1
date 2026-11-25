#requires -Version 5.1
<#
.SYNOPSIS
Builds and installs a mod selected by folder name, initials, or unique prefix.
.EXAMPLE
.\Build-Mod.ps1 GBO
.EXAMPLE
.\Build-Mod.ps1 GBO -Configuration Release -NoRestore
.EXAMPLE
.\Build-Mod.ps1 GBO -WhatIf
.NOTES
Builds and installation backups are kept under this folder's ignored bin directory.
Existing installed config.json is preserved; other packaged files are updated.
Use -GameRoot for a different Steam library or set the COI_ROOT environment variable.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$Abbreviation,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$GameRoot = $(if ($env:COI_ROOT) { $env:COI_ROOT } else {
        'C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry'
    }),

    [string]$ModsRoot = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Captain of Industry\Mods'),

    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$mods = @(foreach ($directory in Get-ChildItem -LiteralPath $PSScriptRoot -Directory) {
    $projects = @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File)
    if ($projects.Count -eq 0) { continue }
    $words = [regex]::Matches($directory.Name, '[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+')
    [pscustomobject]@{
        Name = $directory.Name
        Normalized = $directory.Name -replace '[^a-zA-Z0-9]', ''
        Initials = -join @($words | ForEach-Object { $_.Value.Substring(0, 1) })
        Projects = $projects
        Directory = $directory.FullName
    }
})

$query = $Abbreviation -replace '[^a-zA-Z0-9]', ''
if (!$query) { throw 'Enter a mod name, initials (such as GBO), or a unique folder-name prefix.' }
$matchingMods = @($mods | Where-Object { $_.Normalized -ieq $query })
if ($matchingMods.Count -eq 0) {
    $matchingMods = @($mods | Where-Object { $_.Initials -ieq $query })
}
if ($matchingMods.Count -eq 0) {
    $matchingMods = @($mods | Where-Object { $_.Normalized.StartsWith($query, [StringComparison]::OrdinalIgnoreCase) })
}
if ($matchingMods.Count -ne 1) {
    $choices = if ($matchingMods.Count -gt 1) { $matchingMods } else { $mods }
    $names = ($choices | ForEach-Object { "$($_.Name) [$($_.Initials)]" }) -join ', '
    if ($matchingMods.Count -gt 1) { throw "Ambiguous mod '$Abbreviation': $names. Use the full folder name." }
    throw "Unknown mod '$Abbreviation'. Available mods: $names"
}
$mod = $matchingMods[0]
if ($mod.Projects.Count -ne 1) {
    throw "Expected one top-level .csproj in '$($mod.Directory)', found $($mod.Projects.Count)."
}

$manifestPath = Join-Path $mod.Directory 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (!$manifest.PSObject.Properties['id'] -or $manifest.id -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$') {
    throw "Missing or invalid mod ID in '$manifestPath'."
}
$GameRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($GameRoot)
$ModsRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ModsRoot)
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'Captain of Industry_Data\Managed\Mafi.Core.dll') -PathType Leaf)) {
    throw "Game assemblies not found in '$GameRoot'. Pass -GameRoot or set COI_ROOT."
}
Get-Command dotnet -CommandType Application -ErrorAction Stop | Out-Null
$destination = Join-Path $ModsRoot $manifest.id
$buildDirectory = Join-Path $PSScriptRoot "bin\mod-build\$($manifest.id)\$Configuration"

Write-Host "Resolved '$Abbreviation' -> $($mod.Name)"
if (!$PSCmdlet.ShouldProcess($destination, "Build and install $($mod.Name) ($Configuration)")) { return }
if (Get-Process -Name 'Captain of Industry' -ErrorAction SilentlyContinue) {
    throw 'Close Captain of Industry before building and installing mods.'
}

$buildArguments = @(
    'build', $mod.Projects[0].FullName,
    '--configuration', $Configuration,
    "-p:COI_ROOT=$GameRoot",
    "-p:OutputPath=$buildDirectory",
    '-p:AppendTargetFrameworkToOutputPath=false',
    '--verbosity', 'minimal'
)
if ($NoRestore) { $buildArguments += '--no-restore' }
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE). Nothing was installed." }
if (!(Test-Path -LiteralPath (Join-Path $buildDirectory 'manifest.json') -PathType Leaf)) {
    throw 'Build did not package manifest.json. Nothing was installed.'
}
if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $buildDirectory 'manifest.json')).Hash) {
    throw 'Packaged manifest.json does not match the source. Nothing was installed.'
}
foreach ($dll in $manifest.primary_dlls) {
    if (!(Test-Path -LiteralPath (Join-Path $buildDirectory $dll) -PathType Leaf)) {
        throw "Build did not package '$dll'. Nothing was installed."
    }
}
if (Get-Process -Name 'Captain of Industry' -ErrorAction SilentlyContinue) {
    throw 'The game started during the build. Close it and run this script again to install.'
}

if (Test-Path -LiteralPath $destination) {
    $backup = Join-Path $PSScriptRoot ("bin\mod-backups\{0}\{1}" -f $manifest.id, (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    Copy-Item -LiteralPath $destination -Destination $backup -Recurse
    Write-Host "Backup: $backup"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $buildDirectory -Recurse -File) {
    $relative = $file.FullName.Substring($buildDirectory.Length + 1)
    $target = Join-Path $destination $relative
    if ($relative -ieq 'config.json' -and (Test-Path -LiteralPath $target)) { continue }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Write-Host "Installed $($mod.Name) to $destination"
