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

    # Capture what is actually on screen, and prove it changed.
    #
    # This script does NOT drive the app: there is no UI-automation harness here, so it cannot
    # navigate to Review / Invoice / Products on its own. An earlier version wrote one PNG per
    # expected screen name anyway, and every file came out byte-identical (197,361 B x10) - ten
    # copies of the launch screen wearing ten different names. That is worse than no artifact,
    # because it lets a report claim "ten screens captured".
    #
    # So: hash each frame, and only record a NEW screen when the pixels actually changed. The
    # manifest states how many distinct frames were seen, so the gap is measurable rather than
    # deniable.
    $hashes = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.List[string]]::new()
    foreach ($screen in $screens) {
        $path = Join-Path $screensDir ("{0}.png" -f $screen.name)
        Capture-Window -Path $path
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $bytes = (Get-Item -LiteralPath $path).Length

        if ($hashes.Count -gt 0 -and $hashes[$hashes.Count - 1] -eq $hash) {
            # Same frame as the previous capture: this screen was never navigated to.
            Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
            Write-Host "SCREEN_NOT_REACHED $($screen.name) board=$($screen.board) (identical to the previous frame)"
            continue
        }

        $hashes.Add($hash)
        $captured.Add($screen.name)
        $seen.Add(("{0} board={1} sha256={2} bytes={3}" -f $screen.name, $screen.board, $hash.Substring(0, 12), $bytes))
        Write-Host "SCREEN_CAPTURED $($screen.name) board=$($screen.board) bytes=$bytes sha256=$($hash.Substring(0, 12))"
        Write-Host "::notice::SCREEN_CAPTURED $($screen.name) board=$($screen.board) bytes=$bytes"
        Start-Sleep -Milliseconds $SettleMilliseconds
    }

    if ($captured.Count -lt $screens.Count) {
        Write-Host "::warning::SCREENS_DISTINCT=$($captured.Count)/$($screens.Count) - the remaining screens need UI automation to navigate to and were NOT captured"
        Write-Host "SCREENS_DISTINCT=$($captured.Count)/$($screens.Count)"
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
    captured      = $captured
    count         = $captured.Count
    requested     = $screens.Count
    distinct      = $seen
    note          = 'Only frames whose pixels changed are kept. Screens missing from this list were not navigated to: this script has no UI automation, so they must be captured by driving the app, not by renaming the launch screen.'
    directory     = $screensDir
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDir 'screens-manifest.json') -Encoding utf8
Write-Host "::notice::SCREENS_CAPTURED_COUNT=$($captured.Count)"
exit 0
