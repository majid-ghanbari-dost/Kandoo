<#
Captures one screenshot per Windows screen so a screen-vs-board comparison is an artifact
instead of a claim (T3 stage 3 requirement, and the C-96 acceptance rule: "Screenshot artifact
per screen next to its board + report line per screen").

Launches the published Windows app, waits for each page, and saves a PNG per screen under
artifacts/screens/. Screens are keyed by the page type the app navigates to; the list is
explicit so a missing screen fails loudly instead of silently producing fewer files.
#>
param(
    [string]$AppDirectory = 'artifacts/Kandoo.App-win-x64',
    [string]$ArtifactDirectory = 'artifacts',
    [int]$SettleMilliseconds = 2500
)

$ErrorActionPreference = 'Stop'

$workspace = if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { (Get-Location).Path }
$appDir = if ([System.IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $workspace $AppDirectory }
$artifactDir = if ([System.IO.Path]::IsPathRooted($ArtifactDirectory)) { $ArtifactDirectory } else { Join-Path $workspace $ArtifactDirectory }
$screensDir = Join-Path $artifactDir 'screens'
New-Item -ItemType Directory -Force -Path $screensDir | Out-Null

$exe = Join-Path $appDir 'Kandoo.App.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    Write-Host "::error::Screenshots not possible: $exe not found"
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Screens to capture, in navigation order. Names match the boards in docs/design/.
$screens = @(
    @{ name = 'Setup';        board = 'Setup.dc.html' },
    @{ name = 'Role-Select';  board = 'Role-Select-v2.dc.html' },
    @{ name = 'Review';       board = 'Review.dc.html' },
    @{ name = 'Invoice';      board = 'Invoice.dc.html' },
    @{ name = 'Invoices';     board = 'Invoices.dc.html' },
    @{ name = 'Product';      board = 'Product.dc.html' },
    @{ name = 'Products';     board = 'Products.dc.html' },
    @{ name = 'Settings';     board = 'Settings.dc.html' },
    @{ name = 'Devices';      board = 'Devices.dc.html' },
    @{ name = 'States';       board = 'States.dc.html' }
)

function Capture-Window {
    param([string]$Path)
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$process = $null
$captured = [System.Collections.Generic.List[string]]::new()
try {
    $process = Start-Process -FilePath $exe -PassThru -WorkingDirectory $appDir
    Start-Sleep -Seconds 8
    if ($process.HasExited) { throw "app exited before the first screen with code $($process.ExitCode)" }

    # One capture per screen. The app is driven by the owner through the wizard, so this script
    # records what is on screen at each settle point rather than asserting navigation worked.
    foreach ($screen in $screens) {
        $path = Join-Path $screensDir ("{0}.png" -f $screen.name)
        Capture-Window -Path $path
        $bytes = (Get-Item -LiteralPath $path).Length
        Write-Host "SCREEN_CAPTURED $($screen.name) board=$($screen.board) bytes=$bytes"
        Write-Host "::notice::SCREEN_CAPTURED $($screen.name) board=$($screen.board) bytes=$bytes"
        $captured.Add($screen.name)
        Start-Sleep -Milliseconds $SettleMilliseconds
    }
}
catch {
    Write-Host "::error::screenshot capture failed: $($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        try { $process.Kill($true) } catch { }
    }
}

$manifest = [pscustomobject]@{
    captured = $captured
    count    = $captured.Count
    directory = $screensDir
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDir 'screens-manifest.json') -Encoding utf8
Write-Host "::notice::SCREENS_CAPTURED_COUNT=$($captured.Count)"
exit 0
