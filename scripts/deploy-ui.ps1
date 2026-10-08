<#
Packages the HyperTizen UI as a TV web app, signs it, installs it on the TV and starts it.
Reads the TV address and certificate profile from tv.local.json in the repository root.
#>
. (Join-Path $PSScriptRoot 'tv-tools.ps1')
$appId = '6jwjAZfoVq.HyperTizenUI'
# `tz run` wants the package id; `tizen run` wants the application id.
$packageId = '6jwjAZfoVq'
$source = Join-Path $root 'HyperTizenUI'

$stage = Join-Path ([IO.Path]::GetTempPath()) ('hypertizen-ui-' + [Guid]::NewGuid().ToString('N'))
$content = Join-Path $stage 'content'
New-Item -ItemType Directory -Force $content | Out-Null

try {
    # Only what the app needs on the TV: no tests, no build leftovers.
    foreach ($item in 'index.html', 'main.css', 'config.xml', 'icon.png', 'js') {
        Copy-Item (Join-Path $source $item) $content -Recurse
    }

    $wgt = Join-Path $stage 'HyperTizenUI.wgt'
    if ($useTz) {
        Write-Host "==> Packaging"
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::Open($wgt, 'Create')
        try {
            foreach ($file in Get-ChildItem $content -Recurse -File) {
                # Entry names need forward slashes to be valid inside the package.
                $entry = $file.FullName.Substring($content.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry) | Out-Null
            }
        } finally {
            $zip.Dispose()
        }

        Invoke-Step "Signing with profile '$($config.signingProfile)'" {
            & $tizen pack -t wgt -s $config.signingProfile -b $wgt
        }
    } else {
        Invoke-Step "Packaging and signing with profile '$($config.signingProfile)'" {
            & $tizen package -t wgt -s $config.signingProfile -o $stage -- $content
        }
        $wgt = (Get-ChildItem $stage -Filter *.wgt | Select-Object -First 1).FullName
        if (-not $wgt) { throw "No .wgt was produced in $stage." }
    }

    Invoke-Step "Connecting to $target" {
        & $sdb connect $config.tvIp
    }

    Invoke-Step "Installing on the TV" {
        if ($useTz) { & $tizen install -p $wgt -e $target }
        else { & $tizen install -n (Split-Path $wgt -Leaf) -s $target -- (Split-Path $wgt -Parent) }
    }

    Invoke-Step "Starting the UI" {
        if ($useTz) { & $tizen run -p $packageId -e $target }
        else { & $tizen run -p $appId -s $target }
    }

    Write-Host "The HyperTizen UI is open on the TV."
} finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
