param(
    [Parameter(Mandatory = $true)][string]$StepName,
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [string[]]$ScriptArguments = @()
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:RUNNER_TEMP ("kandoo-ci-step-" + [guid]::NewGuid().ToString('N') + '.log')
$exitCode = 1
try {
    & $ScriptPath @ScriptArguments *>&1 | Tee-Object -FilePath $log
    $exitCode = ($null -eq $LASTEXITCODE) ? 0 : $LASTEXITCODE
}
catch {
    $_ | Out-String | Add-Content -LiteralPath $log
    $exitCode = ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) ? $LASTEXITCODE : 1
}

if ($exitCode -ne 0) {
    $allLines = @(Get-Content -LiteralPath $log)
    $errorLines = @($allLines |
        Select-String -Pattern 'error|exception|failed|failure|diff|throw' -CaseSensitive:$false |
        Select-Object -First 20 |
        ForEach-Object { $_.Line })
    if ($errorLines.Count -eq 0) {
        $errorLines = @($allLines | Select-Object -First 20)
    }

    $header = "CI_STEP_FAILURE step=$StepName exit_code=$exitCode"
    $diagnostic = @($header) + $errorLines
    $diagnostic | ForEach-Object { Write-Host $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        $summary = [System.Collections.Generic.List[string]]::new()
        $summary.Add("## $StepName")
        $summary.Add('')
        $summary.Add("Exit code: $exitCode")
        $summary.Add('')
        $summary.Add('```text')
        foreach ($line in $diagnostic) { $summary.Add($line) }
        $summary.Add('```')
        $summary | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
}

exit $exitCode
