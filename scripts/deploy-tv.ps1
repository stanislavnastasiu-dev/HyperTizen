<#
Builds the HyperTizen service, signs it, installs it on the TV and starts it.
Reads the TV address and certificate profile from tv.local.json in the repository root.
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$packageId = 'io.gh.reisxd.HyperTizen'
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

Invoke-Step "Building ($Configuration)" {
    dotnet build (Join-Path $root 'HyperTizen\HyperTizen.csproj') -c $Configuration
}

$tpk = Get-ChildItem (Join-Path $root "HyperTizen\bin\$Configuration\tizen90\*.tpk") |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $tpk) { throw "No .tpk found in HyperTizen\bin\$Configuration\tizen90." }

Invoke-Step "Signing $($tpk.Name) with profile '$($config.signingProfile)'" {
    if ($useTz) { & $tizen pack -t tpk -s $config.signingProfile -b $tpk.FullName }
    else { & $tizen package -t tpk -s $config.signingProfile -- $tpk.FullName }
}

Invoke-Step "Connecting to $target" {
    & $sdb connect $config.tvIp
}

Invoke-Step "Installing on the TV" {
    if ($useTz) { & $tizen install -p $tpk.FullName -e $target }
    else { & $tizen install -n $tpk.Name -s $target -- $tpk.DirectoryName }
}

Invoke-Step "Starting the service" {
    if ($useTz) { & $tizen run -p $packageId -e $target }
    else { & $tizen run -p $packageId -s $target }
}

Write-Host "==> Waiting for the control server on $($config.tvIp):8086"
$listening = $false
for ($attempt = 0; $attempt -lt 15 -and -not $listening; $attempt++) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect($config.tvIp, 8086)
        $listening = $true
    } catch {
        Start-Sleep -Seconds 1
    } finally {
        $client.Close()
    }
}
if ($listening) {
    Write-Host "Control server is listening on port 8086."
} else {
    Write-Warning "Port 8086 did not answer within 15 seconds. Check the log below."
}

Write-Host "==> TV log (Ctrl+C to stop)"
& $sdb -s $target dlog HyperTizen:V *:S
