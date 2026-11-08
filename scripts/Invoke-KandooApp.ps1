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
    Write-Host "C50_EXEC mode=$Mode command=dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "C50 command failed: mode=$Mode exit_code=$LASTEXITCODE"
    }
}

function Write-ArtifactEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -le 0) {
        throw "C50 artifact is empty: $Path"
    }

    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "C50_ARTIFACT PASS mode=$Mode path=$($file.FullName) bytes=$($file.Length) sha256=$hash"
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
            $publish = Join-Path $artifactRoot 'kandoo-app-c50-win-x64'
            Invoke-DotNet @(
                'publish', 'Kandoo.App.csproj',
                '--configuration', 'Release',
                '--framework', 'net8.0-windows10.0.19041.0',
                '-p:RuntimeIdentifierOverride=win10-x64', '--self-contained', 'true',
                '-p:WindowsAppSDKSelfContained=true',
                '--output', $publish)
            $zip = Join-Path $artifactRoot 'kandoo-app-c50-win-x64.zip'
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
                throw 'C50 Android build produced no APK.'
            }

            $target = Join-Path $artifactRoot 'kandoo-app-c50-android-debug.apk'
            Copy-Item -LiteralPath $apk.FullName -Destination $target -Force
            Write-ArtifactEvidence -Path $target
        }
        'AndroidRelease' {
            $keystore = Join-Path $artifactRoot 'kandoo-release.keystore'
            $certReport = Join-Path $artifactRoot 'kandoo-app-c59-release-keystore-cert.txt'
            $keytool = Get-Command 'keytool' -ErrorAction SilentlyContinue
            if ($null -eq $keytool) {
                $candidates = @(
                    "${env:JAVA_HOME}\bin\keytool.exe",
                    "${env:JAVA_HOME}\bin\keytool",
                    'C:\Program Files\Android\jdk\bin\keytool.exe',
                    'C:\Program Files\Java\jdk-17\bin\keytool.exe',
                    'C:\Program Files\Java\jdk-21\bin\keytool.exe',
                    '/usr/bin/keytool'
                )
                foreach ($candidate in $candidates) {
                    if (Test-Path -LiteralPath $candidate) {
                        $keytool = Get-Item -LiteralPath $candidate
                        break
                    }
                }
            }
            if ($null -eq $keytool) {
                throw 'C59 AndroidRelease build requires keytool to generate a release signing keystore.'
            }

            $storePass = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
            $keyPass = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(18))

            & $keytool -genkeypair -alias kandoo-release -keyalg RSA -keysize 2048 -validity 3650 -keystore $keystore -storepass $storePass -keypass $keyPass -dname 'CN=Kandoo Digital Invoice, O=Kandoo' -noprompt
            if ($LASTEXITCODE -ne 0) { throw 'keytool keystore generation failed.' }

            $env:AndroidSigningKeyStore = $keystore
            $env:AndroidSigningKeyAlias = 'kandoo-release'
            $env:AndroidSigningKeyPass = $keyPass
            $env:AndroidSigningStorePass = $storePass

            Invoke-DotNet @(
                'build', 'Kandoo.App.csproj',
                '--configuration', 'Release', '--framework', 'net8.0-android')
            $apk = Get-ChildItem -LiteralPath 'bin/Release/net8.0-android' -Recurse -Filter '*.apk' |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($null -eq $apk) {
                throw 'C59 Android release build produced no APK.'
            }

            $signed = @(Get-ChildItem -LiteralPath 'bin/Release/net8.0-android' -Recurse -Filter '*-Signed.apk' | Sort-Object LastWriteTime -Descending)
            if ($signed.Count -gt 0) { $apk = $signed[0] }

            $target = Join-Path $artifactRoot 'kandoo-app-c59-android-release.apk'
            Copy-Item -LiteralPath $apk.FullName -Destination $target -Force
            Write-ArtifactEvidence -Path $target

            & $keytool -list -v -keystore $keystore -alias kandoo-release -storepass $storePass |
                Out-File -FilePath $certReport -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw 'C59 release certificate report failed.' }
            Write-ArtifactEvidence -Path $certReport
        }
    }
}
finally {
    Pop-Location
}
