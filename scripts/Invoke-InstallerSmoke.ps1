<#
Kandoo installer smoke test (CI, windows-latest). Layout v2 (C-95): NO separate launcher.

Installs the freshly built setup.exe silently and checks the result. It runs ALL checks even if one
fails, writes a per-check outcome file, emits ::error:: / ::warning:: annotations, and exits 1 at the end
if any HARD check failed.

Checks:
  install            silent install, exit code 0
  layout             hub\Kandoo.Hub.exe and app\Kandoo.App.exe exist, each folder has its own hostfxr.dll + coreclr.dll,
                     no exe in the install root, no leftover Kandoo.SetupLauncher.exe
  shortcuts          Start Menu shortcut "Kandoo" exists and its target file exists
  app-selfcontained  app\Kandoo.App.runtimeconfig.json declares includedFrameworks (no machine .NET needed)
  first-run-selfcheck  app\Kandoo.App.exe --self-check <file>: the INSTALLED app resolves the installed Hub, starts it,
                     creates the owner session and writes KANDOO_SETUP_OK (catches path/layout/bootstrap bugs)
  hub-health         installed hub\Kandoo.Hub.exe answers GET /health with 200
  maui-app           (SOFT) installed app\Kandoo.App.exe stays alive for 15 s
#>
param(
    [string]$SetupPath = 'packaging/HubPilot/Output/Kandoo-Setup-x64.exe',
    [string]$InstallDir = 'C:\KandooInstallTest',
    [string]$ArtifactDir = 'artifacts'
)

$ErrorActionPreference = 'Stop'

$workspace = if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { (Get-Location).Path }
$setup = if ([System.IO.Path]::IsPathRooted($SetupPath)) { $SetupPath } else { Join-Path $workspace $SetupPath }
$artifactDirFull = if ([System.IO.Path]::IsPathRooted($ArtifactDir)) { $ArtifactDir } else { Join-Path $workspace $ArtifactDir }
New-Item -ItemType Directory -Force -Path $artifactDirFull | Out-Null

# NOTE: a plain @() array is fixed-size and .Add() throws "Collection was of a fixed size".
$outcomes = [System.Collections.Generic.List[string]]::new()
$script:hardFailures = 0
$script:installed = $false

function Write-Outcome {
    param([string]$Name, [string]$Status, [string]$Detail)
    $line = "$Status $Name :: $Detail"
    $outcomes.Add($line)
    Write-Host "SMOKE_$Status $Name :: $Detail"
}

function Invoke-Check {
    param([string]$Name, [scriptblock]$Body, [switch]$Soft)
    try {
        $detail = & $Body
        Write-Outcome -Name $Name -Status 'PASS' -Detail ([string]$detail)
    }
    catch {
        $msg = ($_.Exception.Message -replace '[\r\n]+', ' ')
        if ($Soft) {
            Write-Host "::warning::SMOKE $Name (soft) $msg"
            Write-Outcome -Name $Name -Status 'WARN' -Detail $msg
        }
        else {
            Write-Host "::error::SMOKE $Name FAILED: $msg"
            Write-Outcome -Name $Name -Status 'FAIL' -Detail $msg
            $script:hardFailures++
        }
    }
}

function Start-Proc {
    # ProcessStartInfo is used instead of Start-Process so environment variables can be set per process.
    param(
        [string]$File,
        [string[]]$Arguments = @(),
        [string]$WorkDir = '',
        [hashtable]$EnvVars = @{}
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new($File)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    if ($WorkDir) { $psi.WorkingDirectory = $WorkDir }
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    foreach ($k in $EnvVars.Keys) { $psi.Environment[[string]$k] = [string]$EnvVars[$k] }
    return [System.Diagnostics.Process]::Start($psi)
}

function Assert-Installed {
    if (-not $script:installed) { throw 'skipped because the install check failed' }
}

# 1. Silent install ------------------------------------------------------------------------------
Invoke-Check 'install' {
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "setup.exe not found at $setup" }
    Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $setupLog = Join-Path $InstallDir 'setup.log'
    $p = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$InstallDir", "/LOG=$setupLog") -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "installer exit code $($p.ExitCode)" }
    $script:installed = $true
    'installer exit code 0'
}

# 2. Layout --------------------------------------------------------------------------------------
Invoke-Check 'layout' {
    Assert-Installed
    foreach ($f in @('hub\Kandoo.Hub.exe', 'hub\hostfxr.dll', 'hub\coreclr.dll', 'app\Kandoo.App.exe', 'app\hostfxr.dll', 'app\coreclr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $InstallDir $f) -PathType Leaf)) { throw "MISSING $f" }
    }
    foreach ($f in @('Kandoo.Hub.exe', 'Kandoo.App.exe', 'Kandoo.SetupLauncher.exe')) {
        if (Test-Path -LiteralPath (Join-Path $InstallDir $f) -PathType Leaf) { throw "UNEXPECTED $f in the install root" }
    }
    $hubCoreclr = (Get-Item -LiteralPath (Join-Path $InstallDir 'hub\coreclr.dll')).VersionInfo.FileVersion
    $appCoreclr = (Get-Item -LiteralPath (Join-Path $InstallDir 'app\coreclr.dll')).VersionInfo.FileVersion
    "hub coreclr=$hubCoreclr, app coreclr=$appCoreclr, two separate runtimes"
}

# 3. Shortcuts must point at files that exist -------------------------------------------------------
Invoke-Check 'shortcuts' {
    Assert-Installed
    $lnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Kandoo\Kandoo.lnk'
    if (-not (Test-Path -LiteralPath $lnk -PathType Leaf)) { throw "Start Menu shortcut not found: $lnk" }
    $shell = New-Object -ComObject WScript.Shell
    $target = $shell.CreateShortcut($lnk).TargetPath
    if (-not $target) { throw 'shortcut has an empty target' }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "shortcut target does not exist: $target" }
    "shortcut target = $target"
}

# 4. The MAUI app must not need a machine-wide .NET -------------------------------------------------
Invoke-Check 'app-selfcontained' {
    Assert-Installed
    $rc = Join-Path $InstallDir 'app\Kandoo.App.runtimeconfig.json'
    if (-not (Test-Path -LiteralPath $rc -PathType Leaf)) { throw 'MISSING app\Kandoo.App.runtimeconfig.json' }
    if ((Get-Content -LiteralPath $rc -Raw) -notmatch 'includedFrameworks') { throw 'app runtimeconfig has no includedFrameworks (not self-contained)' }
    'app runtimeconfig declares includedFrameworks'
}

# 5. First-run flow of the INSTALLED app (headless self-check) ---------------------------------------
Invoke-Check 'first-run-selfcheck' {
    Assert-Installed
    $appDir = Join-Path $InstallDir 'app'
    $resultFile = Join-Path $InstallDir 'selfcheck.txt'
    Remove-Item -LiteralPath $resultFile -ErrorAction SilentlyContinue
    $p = Start-Proc -File (Join-Path $appDir 'Kandoo.App.exe') -Arguments @('--self-check', $resultFile) -WorkDir $appDir
    try {
        if (-not $p.WaitForExit(120000)) {
            try { $p.Kill($true) } catch { }
            throw 'app --self-check did not exit within 120 s'
        }
        $code = $p.ExitCode
        $line = if (Test-Path -LiteralPath $resultFile -PathType Leaf) { (Get-Content -LiteralPath $resultFile -TotalCount 1) } else { '(no result file written)' }
        Copy-Item -LiteralPath $resultFile -Destination (Join-Path $artifactDirFull 'selfcheck.txt') -Force -ErrorAction SilentlyContinue
        if ($code -ne 0) { throw "app --self-check exit code $code; result: $line" }
        if ($line -notmatch '^KANDOO_SETUP_OK') { throw "unexpected self-check output: $line" }
        $line
    }
    finally {
        # The self-check must stop the Hub it started; make sure nothing is left behind for the next check.
        Get-Process -Name 'Kandoo.Hub' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill($true) } catch { } }
    }
}

# 6. Hub from the INSTALLED folder -----------------------------------------------------------------
Invoke-Check 'hub-health' {
    Assert-Installed
    $hubDir = Join-Path $InstallDir 'hub'
    $guid = [guid]::NewGuid().ToString()
    $p = Start-Proc -File (Join-Path $hubDir 'Kandoo.Hub.exe') -WorkDir $hubDir -EnvVars @{ 'ASPNETCORE_URLS' = 'http://localhost:5000'; 'Hub__OwnerBootstrapProof' = $guid }
    try {
        $ok = $false
        $last = ''
        $i = 0
        for ($i = 1; $i -le 40; $i++) {
            Start-Sleep -Seconds 1
            if ($p.HasExited) { throw "hub exited early with code $($p.ExitCode)" }
            try {
                $r = Invoke-WebRequest -Uri 'http://localhost:5000/health' -UseBasicParsing -TimeoutSec 2
                if ($r.StatusCode -eq 200) { $ok = $true; break }
            }
            catch { $last = $_.Exception.Message }
        }
        if (-not $ok) { throw "hub /health did not return 200 within 40 s (last error: $last)" }
        "GET /health = 200 after $i s"
    }
    finally {
        if ($p -and -not $p.HasExited) { try { $p.Kill($true); [void]$p.WaitForExit(5000) } catch { } }
    }
}

# 7. MAUI app (SOFT: reported as a warning, does not fail the job) ----------------------------------
Invoke-Check 'maui-app' -Soft {
    Assert-Installed
    $appDir = Join-Path $InstallDir 'app'
    $p = Start-Proc -File (Join-Path $appDir 'Kandoo.App.exe') -WorkDir $appDir
    try {
        Start-Sleep -Seconds 15
        if ($p.HasExited) { throw "app exited within 15 s with code $($p.ExitCode)" }
        'app still running after 15 s'
    }
    finally {
        if ($p -and -not $p.HasExited) { try { $p.Kill($true); [void]$p.WaitForExit(5000) } catch { } }
    }
}

# Outputs ----------------------------------------------------------------------------------------
$outcomeFile = Join-Path $artifactDirFull 'smoke-outcome.txt'
$outcomes | Out-File -FilePath $outcomeFile -Encoding utf8
Copy-Item -LiteralPath (Join-Path $InstallDir 'setup.log') -Destination (Join-Path $artifactDirFull 'setup.log') -Force -ErrorAction SilentlyContinue

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = [System.Collections.Generic.List[string]]::new()
    $summary.Add('## Installer smoke test')
    $summary.Add('')
    $summary.Add('```text')
    foreach ($o in $outcomes) { $summary.Add($o) }
    $summary.Add('```')
    $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

if ($script:hardFailures -gt 0) {
    Write-Host "::error::SMOKE total hard failures: $($script:hardFailures)"
    exit 1
}
Write-Host 'SMOKE_ALL_HARD_CHECKS_PASSED'
exit 0
