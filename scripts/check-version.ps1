<#
Checks that a release tag matches the version in the source, and prints that version.
A release is published only from a tag 'v<version>' where <version> is what the TV manifest,
the core project and the UI all say.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    # The repository to check; this one when left out. The tests point it at a folder of their own.
    [string]$Root
)

$ErrorActionPreference = 'Stop'
# Not a parameter default: Windows PowerShell leaves $PSScriptRoot empty there when run with -File.
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }

[xml]$manifest = Get-Content (Join-Path $Root 'HyperTizen/tizen-manifest.xml') -Raw
$manifestVersion = $manifest.manifest.version

[xml]$project = Get-Content (Join-Path $Root 'HyperTizen.Core/HyperTizen.Core.csproj') -Raw
$coreVersion = $project.Project.PropertyGroup |
    ForEach-Object { $_.Version } |
    Where-Object { $_ } |
    Select-Object -First 1

# The UI says its version in three places: to TizenBrew, to the TV, and on its Settings screen.
$ui = Join-Path $Root 'HyperTizenUI'
$packageVersion = (Get-Content (Join-Path $ui 'package.json') -Raw | ConvertFrom-Json).version
$widgetVersion = ([xml](Get-Content (Join-Path $ui 'config.xml') -Raw)).widget.version
$shownVersion = $null
if ((Get-Content (Join-Path $ui 'js/app.js') -Raw) -match "HT\.uiVersion\s*=\s*'([^']*)'") { $shownVersion = $Matches[1] }

$found = [ordered]@{
    'tizen-manifest.xml' = $manifestVersion
    'HyperTizen.Core.csproj' = $coreVersion
    'HyperTizenUI/package.json' = $packageVersion
    'HyperTizenUI/config.xml' = $widgetVersion
    'HyperTizenUI/js/app.js' = $shownVersion
}

if (@($found.Values | Where-Object { $Tag -cne "v$_" }).Count -gt 0) {
    $list = ($found.GetEnumerator() | ForEach-Object { "$($_.Key) has '$($_.Value)'" }) -join ', '
    throw ("The tag '$Tag' does not match the source: $list. A release tag is 'v' followed by that " +
        "version, and all of these files must agree. Delete this tag, or the TizenBrew module, which " +
        "follows the newest version tag, starts serving this commit's UI: git push origin :refs/tags/$Tag")
}

Write-Output $manifestVersion
