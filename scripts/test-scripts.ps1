<#
Tests for package-ui.ps1 and check-version.ps1. Exits with 1 when any check fails.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('hypertizen-scripts-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$failures = 0

function Check([string]$name, [scriptblock]$test) {
    try {
        & $test
        Write-Host "ok   $name"
    } catch {
        Write-Host "FAIL ${name}: $_"
        $script:failures++
    }
}

function Assert-Equal($expected, $actual) {
    if ("$expected" -cne "$actual") { throw "expected '$expected', got '$actual'" }
}

function Get-Entries([string]$package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        return @($zip.Entries | ForEach-Object { $_.FullName } | Sort-Object)
    } finally {
        $zip.Dispose()
    }
}

$packageUi = Join-Path $PSScriptRoot 'package-ui.ps1'

try {
    Check 'package-ui writes exactly the files the TV needs, with forward slashes' {
        $wgt = Join-Path $work 'ui.wgt'
        & $packageUi -Output $wgt

        $scripts = Join-Path (Join-Path $root 'HyperTizenUI') 'js'
        $expected = @('index.html', 'main.css', 'config.xml', 'icon.png')
        $expected += Get-ChildItem $scripts -Recurse -File |
            ForEach-Object { 'js/' + $_.FullName.Substring($scripts.Length + 1).Replace('\', '/') }
        Assert-Equal (($expected | Sort-Object) -join '|') ((Get-Entries $wgt) -join '|')
    }

    Check 'package-ui leaves out tests and build leftovers' {
        $wgt = Join-Path $work 'ui.wgt'
        & $packageUi -Output $wgt
        $unwanted = @(Get-Entries $wgt | Where-Object { $_ -match '^(tests/|package\.json|tizen_|Debug/|Release/|\.)' })
        Assert-Equal 0 $unwanted.Count
    }

    Check 'package-ui replaces an existing file' {
        $wgt = Join-Path $work 'twice.wgt'
        & $packageUi -Output $wgt
        & $packageUi -Output $wgt
        if ((Get-Item $wgt).Length -le 0) { throw 'the package is empty' }
    }

    Check 'package-ui creates the output folder and accepts a relative path' {
        Push-Location $work
        try {
            & $packageUi -Output 'new-folder/deeper/ui.wgt'
        } finally {
            Pop-Location
        }
        if (-not (Test-Path (Join-Path $work 'new-folder/deeper/ui.wgt'))) { throw 'the package was not written' }
    }

    # --- check-version
    $checkVersion = Join-Path $PSScriptRoot 'check-version.ps1'

    # A stand-in repository holding only the two files that carry the version.
    function New-Source([string]$manifestVersion, [string]$coreVersion) {
        $folder = Join-Path $work ('source-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force (Join-Path $folder 'HyperTizen'), (Join-Path $folder 'HyperTizen.Core') | Out-Null
        Set-Content (Join-Path $folder 'HyperTizen/tizen-manifest.xml') -Encoding UTF8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns="http://tizen.org/ns/packages" package="io.gh.reisxd.HyperTizen" version="$manifestVersion" api-version="6.5">
    <profile name="tv" />
</manifest>
"@
        Set-Content (Join-Path $folder 'HyperTizen.Core/HyperTizen.Core.csproj') -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <PropertyGroup>
    <Version>$coreVersion</Version>
  </PropertyGroup>
</Project>
"@
        return $folder
    }

    function Assert-Rejected([string]$tag, [string]$source, [string]$mustMention) {
        $message = $null
        try {
            & $checkVersion -Tag $tag -Root $source | Out-Null
        } catch {
            $message = "$_"
        }
        if ($null -eq $message) { throw "the tag '$tag' was accepted" }
        if ($message -notlike "*$mustMention*") { throw "the message does not mention '$mustMention': $message" }
    }

    Check 'check-version accepts a matching tag and prints the version' {
        Assert-Equal '1.1.0' (& $checkVersion -Tag 'v1.1.0' -Root (New-Source '1.1.0' '1.1.0'))
    }

    Check 'check-version rejects a tag for another version and names what it found' {
        $source = New-Source '1.1.0' '1.1.0'
        Assert-Rejected 'v1.2.0' $source 'v1.2.0'
        Assert-Rejected 'v1.2.0' $source '1.1.0'
    }

    Check 'check-version rejects near-miss tags' {
        $source = New-Source '1.1.0' '1.1.0'
        Assert-Rejected '1.1.0' $source '1.1.0'
        Assert-Rejected 'V1.1.0' $source 'V1.1.0'
        Assert-Rejected 'v1.1.0-beta' $source 'v1.1.0-beta'
        Assert-Rejected 'v1.1' $source 'v1.1'
    }

    Check 'check-version rejects source versions that disagree with each other' {
        Assert-Rejected 'v1.1.0' (New-Source '1.0.0' '1.1.0') '1.0.0'
        Assert-Rejected 'v1.1.0' (New-Source '1.1.0' '1.2.0') '1.2.0'
    }

    Check 'the manifest and the core project of this repository carry the same version' {
        $version = ([xml](Get-Content (Join-Path $root 'HyperTizen/tizen-manifest.xml') -Raw)).manifest.version
        Assert-Equal $version (& $checkVersion -Tag "v$version")
    }

    Check 'check-version works when started as a script of its own, as the workflow does' {
        $shell = (Get-Process -Id $PID).Path
        $version = ([xml](Get-Content (Join-Path $root 'HyperTizen/tizen-manifest.xml') -Raw)).manifest.version
        # The error text of the second run must not stop this script.
        $ErrorActionPreference = 'Continue'

        $printed = & $shell -NoProfile -File $checkVersion -Tag "v$version"
        Assert-Equal 0 $LASTEXITCODE
        Assert-Equal $version $printed

        & $shell -NoProfile -File $checkVersion -Tag 'v0.0.0-never' 2>&1 | Out-Null
        Assert-Equal 1 $LASTEXITCODE
    }
    # --- end
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed."
    exit 1
}
Write-Host 'All checks passed.'
# A check above runs a command that fails on purpose; do not hand its exit code on.
exit 0
