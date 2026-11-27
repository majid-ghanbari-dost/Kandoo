<#
Decides, from the changed-file list of one commit/push, which CI jobs actually have work.

M-013 §4 asks for per-lane path filters and for the installer smoke to run only when the
app, packaging or scripts change. Getting this wrong in the "skip" direction is much worse
than getting it wrong in the "run" direction: a skipped job is invisible, while a needless
job only costs minutes. So the default for every flag is **true** and it is only turned off
by evidence:

  - build    : off only when EVERY changed file is documentation
  - installer: off only when nothing under src/Kandoo.App, packaging, scripts or the
               workflows changed (the installer embeds the app + Hub, and its .iss and smoke
               script live under packaging/ and scripts/). On main both lanes always run, so a
               break introduced by an unrelated merge is still caught within one merge.

Writes `build` and `installer` (true/false) to GITHUB_OUTPUT so jobs can branch on
`needs.changes.outputs.<flag> == 'true'`. The app lane and the installer/smoke lanes share the
`installer` flag on purpose: the installer stages the MAUI publish, so one cannot run without
the other, and the file set that makes the installer worth rebuilding is the same file set that
can break the app.

The file list comes from the GitHub API so it works the same on push and pull_request,
where the local git base differs.
#>
param(
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Sha = $env:GITHUB_SHA,
    [string]$BaseSha = '',
    [string]$EventName = $env:GITHUB_EVENT_NAME,
    [string]$OutputFile = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = 'Stop'

function Get-ChangedFiles {
    param([string]$Repo, [string]$Head, [string]$Base, [string]$Event)

    $files = [System.Collections.Generic.List[string]]::new()

    if ($Env:GITHUB_EVENT_PATH -and (Test-Path -LiteralPath $Env:GITHUB_EVENT_PATH)) {
        $event = Get-Content -LiteralPath $Env:GITHUB_EVENT_PATH -Raw | ConvertFrom-Json
        if ($Event -eq 'pull_request' -and $null -ne $event.pull_request) {
            # The PR base..head diff is what reviewers care about.
            $Base = [string]$event.pull_request.base.sha
            $Head = [string]$event.pull_request.head.sha
        }
    }

    if ($Base -and $Head -and $Base -ne '0000000000000000000000000000000000000000') {
        $uri = "https://api.github.com/repos/$Repo/compare/$Base...$Head"
        try {
            $response = Invoke-RestMethod -Uri $uri -Headers @{
                Authorization = "Bearer $env:GITHUB_TOKEN"
                Accept        = 'application/vnd.github+json'
            } -TimeoutSec 30
            foreach ($file in $response.files) { $files.Add([string]$file.filename) }
            if ($response.files.Count -ge 300) {
                # The compare endpoint caps at 300 files; treat a capped answer as "everything".
                Write-Host "CHANGESET_TRUNCATED at 300 files - assuming every job has work"
                return @('**')
            }
            return $files.ToArray()
        }
        catch {
            Write-Host "CHANGESET_API_FAILED $($_.Exception.Message) - assuming every job has work"
            return @('**')
        }
    }

    # No usable base (e.g. a force-push to an existing branch): be conservative.
    Write-Host 'CHANGESET_NO_BASE - assuming every job has work'
    return @('**')
}

# @() is not cosmetic: PowerShell unrolls a one-element array to a bare string on return, and
# "**"[0] is '*', which would silently turn the "assume everything" answer into "nothing".
$changed = @(Get-ChangedFiles -Repo $Repository -Head $Sha -Base $BaseSha -Event $EventName)
Write-Host "CHANGESET_COUNT=$($changed.Count)"
foreach ($f in $changed) { Write-Host "CHANGED $f" }

if ($changed.Count -eq 1 -and $changed[0] -eq '**') {
    $build = 'true'; $installer = 'true'
}
else {
    $docsOnly = $true
    $appTouched = $false
    foreach ($path in $changed) {
        $normalized = $path -replace '\\', '/'

        # Documentation that cannot affect a build. Anything not matching stays "code".
        # .md only. A .txt here is usually packaging payload (HubPilot/README.txt), which is
        # staged into the installer and therefore not documentation.
        $isDoc = $normalized -match '^(docs/|TASKS/)' -or $normalized -match '\.md$'
        if (-not $isDoc) { $docsOnly = $false }

        # Inputs of the installer and of the installer smoke. The .iss is under packaging/,
        # the smoke script and demo-check under scripts/, and the workflow itself.
        if ($normalized -match '^(src/Kandoo\.App/|packaging/|scripts/|\.github/workflows/)') {
            $appTouched = $true
        }
    }

    if ($docsOnly) { $build = 'false' } else { $build = 'true' }
    if ($appTouched) { $installer = 'true' } else { $installer = 'false' }
}

Write-Host "CI_FLAGS build=$build installer=$installer"
if ($OutputFile) {
    "build=$build"         | Out-File -FilePath $OutputFile -Append -Encoding utf8
    "installer=$installer" | Out-File -FilePath $OutputFile -Append -Encoding utf8
}
