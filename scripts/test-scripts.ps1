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
    # --- end
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

if ($failures -gt 0) {
    Write-Host "$failures check(s) failed."
    exit 1
}
Write-Host 'All checks passed.'
