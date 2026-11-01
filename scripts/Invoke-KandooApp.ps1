[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Workload', 'Test', 'Windows', 'Android')]
    [string]$Mode,
    [string]$ArtifactDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = if ([System.IO.Path]::IsPathRooted($ArtifactDirectory)) {
    [System.IO.Path]::GetFullPath($ArtifactDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactDirectory))
}
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Write-Host "C40_EXEC mode=$Mode command=dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "C40 command failed: mode=$Mode exit_code=$LASTEXITCODE"
    }
}

function Write-ArtifactEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -le 0) {
        throw "C40 artifact is empty: $Path"
    }

    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "C40_ARTIFACT PASS mode=$Mode path=$($file.FullName) bytes=$($file.Length) sha256=$hash"
}

Push-Location -LiteralPath (Join-Path $repoRoot 'src/Kandoo.App')
try {
    switch ($Mode) {
        'Workload' {
            Invoke-DotNet @('--version')
            Invoke-DotNet @('workload', 'install', 'maui')
        }
        'Test' {
            Invoke-DotNet @('test', '../../tests/Kandoo.App.Tests/Kandoo.App.Tests.csproj', '--configuration', 'Release')
        }
        'Windows' {
            $publish = Join-Path $artifactRoot 'kandoo-app-c40-win-x64'
            Invoke-DotNet @(
                'publish', 'Kandoo.App.csproj',
                '--configuration', 'Release',
                '--framework', 'net8.0-windows10.0.19041.0',
                '-p:RuntimeIdentifierOverride=win10-x64', '--self-contained', 'true',
                '-p:WindowsAppSDKSelfContained=true',
                '--verbosity', 'detailed',
                '--output', $publish)
            $zip = Join-Path $artifactRoot 'kandoo-app-c40-win-x64.zip'
            Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
            Write-ArtifactEvidence -Path $zip
        }
        'Android' {
            Invoke-DotNet @(
                'build', 'Kandoo.App.csproj',
                '--configuration', 'Debug', '--framework', 'net8.0-android')
            $apk = Get-ChildItem -LiteralPath 'bin/Debug/net8.0-android' -Recurse -Filter '*.apk' |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($null -eq $apk) {
                throw 'C40 Android build produced no APK.'
            }

            $target = Join-Path $artifactRoot 'kandoo-app-c40-android-debug.apk'
            Copy-Item -LiteralPath $apk.FullName -Destination $target -Force
            Write-ArtifactEvidence -Path $target
        }
    }
}
finally {
    Pop-Location
}
