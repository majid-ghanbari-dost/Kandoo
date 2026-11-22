param(
    [Parameter(Mandatory = $true)][string]$PythonRepository,
    [Parameter(Mandatory = $true)][string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'
$sourceCommit = '29d842178cd39d3f54f14f3716c44c585911467b'
$repository = (Resolve-Path -LiteralPath $PythonRepository).Path
$artifact = [IO.Path]::GetFullPath($ArtifactPath)
$actualCommit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $sourceCommit) {
    throw "E-30 source commit mismatch: $actualCommit"
}
Write-Host "E30_SOURCE_COMMIT=$actualCommit"

$python = (Get-Command python -ErrorAction Stop).Source
if (-not [IO.Path]::IsPathRooted($python)) {
    $python = (Resolve-Path -LiteralPath $python).Path
}
& python -m pip install --disable-pip-version-check Pillow 'jsonschema>=4.18,<5'
if ($LASTEXITCODE -ne 0) { throw "Python dependency installation failed: $LASTEXITCODE" }

& dotnet run --project tools/Kandoo.ExtractionBridgeProbe/Kandoo.ExtractionBridgeProbe.csproj `
    --configuration Release --no-build -- `
    --python-repo $repository --python-executable $python --output $artifact
if ($LASTEXITCODE -ne 0) { throw "C-21 extraction bridge probe failed: $LASTEXITCODE" }

$report = Get-Content -LiteralPath $artifact -Raw | ConvertFrom-Json
if ($report.status -ne 'PASS' -or $report.source_commit -ne $sourceCommit) {
    throw 'C-21 probe report is missing a pinned PASS result.'
}
Write-Host "C21_GOLDEN_VERIFIED bytes=$($report.golden_bytes) sha256=$($report.golden_sha256)"
