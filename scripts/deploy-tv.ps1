<#
Builds the HyperTizen service, signs it, installs it on the TV and starts it.
Reads the TV address and certificate profile from tv.local.json in the repository root.
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

. (Join-Path $PSScriptRoot 'tv-tools.ps1')
$packageId = 'io.gh.reisxd.HyperTizen'

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
