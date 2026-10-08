<#
Gathers what a release holds into one folder: the built TV service package and the UI package.
Build the service first: dotnet build HyperTizen/HyperTizen.csproj -c Release
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Output,

    # The repository to take the service package from; this one when left out. The tests point it
    # at a folder of their own.
    [string]$Root
)

$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

$version = ([xml](Get-Content (Join-Path $Root 'HyperTizen/tizen-manifest.xml') -Raw)).manifest.version

# Nothing is written unless the service package is there: a release without it would be useless.
$built = Join-Path $Root 'HyperTizen/bin/Release'
$service = @()
if (Test-Path $built) { $service = @(Get-ChildItem $built -Recurse -File -Filter "*-$version.tpk") }
if ($service.Count -ne 1) {
    throw ("Expected exactly one service package for version $version under $built, found $($service.Count). " +
        "Build it first: dotnet build HyperTizen/HyperTizen.csproj -c Release")
}

New-Item -ItemType Directory -Force $Output | Out-Null
Copy-Item $service[0].FullName $Output
& (Join-Path $PSScriptRoot 'package-ui.ps1') -Output (Join-Path $Output "HyperTizenUI-$version.wgt")
