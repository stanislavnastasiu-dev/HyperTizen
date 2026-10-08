# Dot-sourced by deploy-tv.ps1 and deploy-ui.ps1: reads tv.local.json and finds the Tizen command line tools.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$configPath = Join-Path $root 'tv.local.json'
if (-not (Test-Path $configPath)) {
    throw "Missing tv.local.json. Copy tv.local.example.json to tv.local.json and set tvIp and signingProfile."
}
$config = Get-Content $configPath -Raw | ConvertFrom-Json
if (-not $config.tvIp) { throw "tv.local.json: 'tvIp' is not set." }
if (-not $config.signingProfile) { throw "tv.local.json: 'signingProfile' is not set." }

$studio = 'C:\tizen-studio'
if ($config.tizenStudioPath) { $studio = $config.tizenStudioPath }

# Tools installed by the Tizen extension for VS Code, used when Tizen Studio is absent.
$extensionTools = Join-Path $env:USERPROFILE '.tizen-extension-platform\server\sdktools\data\tools'

function Find-Tool([string[]]$names, [string[]]$fallbacks) {
    foreach ($name in $names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    foreach ($fallback in $fallbacks) {
        if (Test-Path $fallback) { return $fallback }
    }
    throw "'$($names -join "' / '")' was not found on PATH or at: $($fallbacks -join '; '). Install Tizen Studio or the Tizen extension for VS Code, or set tizenStudioPath in tv.local.json."
}

function Invoke-Step([string]$description, [scriptblock]$action) {
    Write-Host "==> $description"
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$description failed with exit code $LASTEXITCODE." }
}

$sdb = Find-Tool @('sdb') @((Join-Path $studio 'tools\sdb.exe'), (Join-Path $extensionTools 'sdb.exe'))
$tizen = Find-Tool @('tizen', 'tz') @((Join-Path $studio 'tools\ide\bin\tizen.bat'), (Join-Path $extensionTools 'tizen-core\tz.exe'))
# Tizen Studio ships the `tizen` CLI; the VS Code extension ships `tz`, which takes different arguments.
$useTz = [IO.Path]::GetFileNameWithoutExtension($tizen) -eq 'tz'
$target = "$($config.tvIp):26101"
