param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$template = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../packaging/HubPilot')).Path
$package = [IO.Path]::GetFullPath($PackageDirectory)
if (Test-Path -LiteralPath $package) {
    throw "Pilot package already exists: $package"
}
if (-not (Test-Path -LiteralPath (Join-Path $publish 'Kandoo.Hub.exe'))) {
    throw 'The self-contained Hub executable is missing.'
}
if (-not (Test-Path -LiteralPath (Join-Path $publish 'Kandoo.Capture.dll'))) {
    throw 'The Capture assembly is missing from the self-contained publish.'
}

$null = New-Item -ItemType Directory -Path $package
Copy-Item -Path (Join-Path $publish '*') -Destination $package -Recurse -Force
Copy-Item -LiteralPath (Join-Path $template 'appsettings.json') -Destination (Join-Path $package 'appsettings.json') -Force
Copy-Item -LiteralPath (Join-Path $template 'Start-KandooHub.ps1') -Destination $package
Copy-Item -LiteralPath (Join-Path $template 'README.txt') -Destination $package
Write-Host "C-22 pilot package assembled at $package"
