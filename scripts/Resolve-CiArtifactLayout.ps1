<#
Puts one downloaded CI artifact where the next step expects it.

`actions/upload-artifact` roots an artifact at the least-common-ancestor of its `path:` entries,
and `download-artifact` expands the result under the `path:` you give. So the on-disk shape
depends on how many paths the producer listed:

  producer lists artifacts/X/**           -> LCA is artifacts/X
  producer lists artifacts/X/** + f.json  -> LCA is artifacts

That produced two different shapes for the same download target in this repo (the Hub publish
carries C-14 evidence files next to its folder; the app publish carries only its folder).

The lesson from two failed attempts is not to compute that arithmetic, and not to search a
shared directory either: searching `artifacts` for Kandoo.App.exe after both publishes landed
there would sweep Hub files into the app folder. Instead each artifact is downloaded into its
own private directory and normalised from there, so a repair can only ever move that artifact's
own files.

  -ExpectedDirectory  where the consumer wants the publish (e.g. artifacts/Kandoo.Hub-win-x64)
  -ProbeFile          a file that proves the layout is right (e.g. Kandoo.Hub.exe)
  -DownloadDirectory  the private directory this artifact was downloaded into
#>
param(
    [Parameter(Mandatory = $true)][string]$ExpectedDirectory,
    [Parameter(Mandatory = $true)][string]$ProbeFile,
    [Parameter(Mandatory = $true)][string]$DownloadDirectory
)

$ErrorActionPreference = 'Stop'

$expected = Join-Path $ExpectedDirectory $ProbeFile
if (Test-Path -LiteralPath $expected -PathType Leaf) {
    Write-Host "LAYOUT_OK $expected"
    return
}

if (-not (Test-Path -LiteralPath $DownloadDirectory -PathType Container)) {
    Write-Host "::error::LAYOUT_FAILED download directory missing: $DownloadDirectory"
    throw "LAYOUT_FAILED download directory missing: $DownloadDirectory"
}

$matches = @(Get-ChildItem -LiteralPath $DownloadDirectory -Filter $ProbeFile -Recurse -File -ErrorAction SilentlyContinue)
if ($matches.Count -eq 0) {
    # ::error:: annotations because workflow step logs are not readable from the sandbox this
    # repo is audited from, while check-run annotations are.
    Write-Host "::error::LAYOUT_FAILED $ProbeFile not found under $DownloadDirectory"
    Get-ChildItem -LiteralPath $DownloadDirectory -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 40 |
        ForEach-Object { Write-Host "::error::LAYOUT_ENTRY $($_.FullName)" }
    throw "LAYOUT_FAILED $ProbeFile was not found anywhere under $DownloadDirectory"
}

# The directory that actually holds the publish is the probe's parent. Moving its children is
# correct whether the artifact arrived flat or nested one level deep.
$source = Split-Path -Parent $matches[0].FullName
Write-Host "LAYOUT_MISMATCH expected $expected - flattening $source"
New-Item -ItemType Directory -Force -Path $ExpectedDirectory | Out-Null

# Resolve once: if source IS the target we are already done and moving would be a no-op loop.
$sourceFull = (Resolve-Path -LiteralPath $source).ProviderPath
$targetFull = (Resolve-Path -LiteralPath $ExpectedDirectory).ProviderPath
if ($sourceFull -eq $targetFull) {
    if (Test-Path -LiteralPath $expected -PathType Leaf) {
        Write-Host "LAYOUT_OK $expected"
        return
    }
    throw "LAYOUT_FAILED $source is the target but $ProbeFile is not there"
}

foreach ($item in @(Get-ChildItem -LiteralPath $source -Force)) {
    # Never move the target into itself: the target can be a child of the source when a caller
    # passes a download directory inside the artifacts tree.
    if ($item.FullName -eq $targetFull) { continue }
    Move-Item -LiteralPath $item.FullName -Destination $ExpectedDirectory -Force
}

if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
    Write-Host "::error::LAYOUT_FAILED after flattening, $expected still missing"
    throw "LAYOUT_FAILED after flattening, $expected still missing"
}
Write-Host "LAYOUT_REPAIRED $expected"
