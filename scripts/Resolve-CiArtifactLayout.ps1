<#
Puts a downloaded CI artifact where the next step expects it, whatever nesting the upload used.

`actions/upload-artifact` roots an artifact at the least-common-ancestor of its `path:` entries
and `download-artifact` expands it under the `path:` you give, so the on-disk result depends on
how many paths the producer listed. When a producer gains or loses an entry (the Hub publish
already carries c14-artifact-size.txt and c14-smoke-result.json next to its publish folder), the
consumer silently gets one extra directory level and fails much later, inside the installer.

Rather than encode that arithmetic in two workflows, this asserts the expected layout and, if it
does not hold, flattens whatever directory actually holds the probe file into place. When it has
to repair anything it says so loudly, so the underlying mismatch is still visible.
#>
param(
    [Parameter(Mandatory = $true)][string]$ExpectedDirectory,
    [Parameter(Mandatory = $true)][string]$ProbeFile,
    [string]$SearchRoot = '.'
)

$ErrorActionPreference = 'Stop'

$expected = Join-Path $ExpectedDirectory $ProbeFile
if (Test-Path -LiteralPath $expected -PathType Leaf) {
    Write-Host "LAYOUT_OK $expected"
    return
}

Write-Host "LAYOUT_MISMATCH expected $expected - searching for $ProbeFile under $SearchRoot"
$matches = @(Get-ChildItem -LiteralPath $SearchRoot -Filter $ProbeFile -Recurse -File -ErrorAction SilentlyContinue)
if ($matches.Count -eq 0) {
    # Emitted as ::error:: annotations on purpose: workflow step logs are not readable from the
    # sandbox this repo is audited from, while check-run annotations are.
    Write-Host "::error::LAYOUT_FAILED $ProbeFile not found under $SearchRoot"
    Get-ChildItem -LiteralPath $SearchRoot -Recurse -Directory -ErrorAction SilentlyContinue |
        Select-Object -First 40 |
        ForEach-Object { Write-Host "::error::LAYOUT_DIR $($_.FullName)" }
    throw "LAYOUT_FAILED $ProbeFile was not found anywhere under $SearchRoot"
}

$source = Split-Path -Parent $matches[0].FullName
Write-Host "LAYOUT_FLATTENED from $source"
New-Item -ItemType Directory -Force -Path $ExpectedDirectory | Out-Null
Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination $ExpectedDirectory -Force
}

if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
    throw "LAYOUT_FAILED after flattening, $expected still missing"
}
Write-Host "LAYOUT_REPAIRED $expected"
