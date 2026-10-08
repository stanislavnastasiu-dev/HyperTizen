<#
Checks that a release tag matches the version in the source, and prints that version.
A release is published only from a tag 'v<version>' where <version> is what both the TV
manifest and the core project say.
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

if ($Tag -cne "v$manifestVersion" -or $Tag -cne "v$coreVersion") {
    throw ("The tag '$Tag' does not match the source: tizen-manifest.xml has version '$manifestVersion' " +
        "and HyperTizen.Core.csproj has '$coreVersion'. A release tag is 'v' followed by that version, " +
        "and both files must agree.")
}

Write-Output $manifestVersion
