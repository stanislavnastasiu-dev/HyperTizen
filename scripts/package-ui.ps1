<#
Packages the HyperTizen UI as an unsigned .wgt. Sign it with your certificate profile before
installing it on a TV.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'HyperTizenUI'
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

New-Item -ItemType Directory -Force (Split-Path -Parent $Output) | Out-Null
if (Test-Path $Output) { Remove-Item $Output }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [IO.Compression.ZipFile]::Open($Output, 'Create')
try {
    # Only what the app needs on the TV: no tests, no build leftovers.
    foreach ($item in 'index.html', 'main.css', 'config.xml', 'icon.png', 'js') {
        $path = Join-Path $source $item
        # -Recurse on a file would also find files of the same name in other folders.
        if (Test-Path $path -PathType Container) { $files = Get-ChildItem $path -Recurse -File | Sort-Object FullName }
        else { $files = Get-Item $path }

        foreach ($file in $files) {
            # Entry names need forward slashes to be valid inside the package.
            $entry = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry) | Out-Null
        }
    }
} finally {
    $zip.Dispose()
}

Write-Host "Wrote $Output"
