[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('MauiWorkload', 'MauiWindows', 'MauiAndroid', 'MauiProbe', 'AvaloniaWorkload', 'AvaloniaWindows', 'AvaloniaAndroid', 'AvaloniaProbe')]
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
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "C30_EXEC mode=$Mode command=dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    Write-Host "C30_MEASURE mode=$Mode elapsed_ms=$($stopwatch.ElapsedMilliseconds) exit_code=$exitCode"
    if ($exitCode -ne 0) {
        throw "C30 command failed: mode=$Mode exit_code=$exitCode"
    }
}

function Write-BuildEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Technology,
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )
    $resolved = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $files = @(Get-ChildItem -LiteralPath $resolved -Recurse -File)
    if ($files.Count -eq 0) {
        throw "C30 build produced no files: $OutputDirectory"
    }
    $outputs = @($files | Sort-Object FullName | Select-Object -First 12 | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($resolved.Length).TrimStart('\', '/')
            bytes = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $evidence = [ordered]@{
        status = 'PASS'
        technology = $Technology
        target = $Target
        mode = $Mode
        file_count = $files.Count
        total_bytes = ($files | Measure-Object -Property Length -Sum).Sum
        sample_outputs = $outputs
    }
    $path = Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-$($Target.ToLowerInvariant())-build.json"
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
    Write-Host "C30_BUILD PASS technology=$Technology target=$Target files=$($files.Count) bytes=$($evidence.total_bytes) evidence=$path"
}

function Invoke-HubProbe {
    param([Parameter(Mandatory = $true)][string]$Technology)
    $hubDirectory = Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-hub"
    Invoke-DotNet @('publish', '../src/Kandoo.Hub/Kandoo.Hub.csproj', '--configuration', 'Release', '--output', $hubDirectory)

    if ($Technology -eq 'MAUI') {
        Invoke-DotNet @('build', 'c30-maui/Kandoo.C30.MauiProbe/Kandoo.C30.MauiProbe.csproj', '--configuration', 'Release')
        $probeDll = (Resolve-Path -LiteralPath 'c30-maui/Kandoo.C30.MauiProbe/bin/Release/net8.0/Kandoo.C30.MauiProbe.dll').Path
        $probePrefix = @()
    }
    else {
        $probeDll = (Resolve-Path -LiteralPath 'c30-avalonia/Kandoo.C30.Avalonia.Desktop/bin/Release/net8.0/Kandoo.C30.Avalonia.Desktop.dll').Path
        $probePrefix = @('--probe')
    }

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    $baseUrl = "http://127.0.0.1:$port"
    $endpoint = "$baseUrl/openapi/v1.json"
    $probeOutput = Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-probe.json"
    $hubOut = Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-hub.stdout.log"
    $hubError = Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-hub.stderr.log"
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__HubSqlite = "Data Source=$(Join-Path $artifactRoot "c30-$($Technology.ToLowerInvariant())-hub.db");Pooling=False"
    $hubDll = (Resolve-Path -LiteralPath (Join-Path $hubDirectory 'Kandoo.Hub.dll')).Path
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @($hubDll) -WorkingDirectory $hubDirectory -RedirectStandardOutput $hubOut -RedirectStandardError $hubError -PassThru
    try {
        $ready = $false
        for ($attempt = 0; $attempt -lt 45; $attempt++) {
            if ($process.HasExited) {
                throw "C30 Hub exited before readiness: exit_code=$($process.ExitCode)"
            }
            try {
                $response = Invoke-WebRequest -Uri "$baseUrl/health" -TimeoutSec 2
                if ($response.StatusCode -eq 200) {
                    $ready = $true
                    break
                }
            }
            catch {
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $ready) {
            throw 'C30 Hub did not become ready within the bounded probe interval.'
        }

        Invoke-DotNet (@($probeDll) + $probePrefix + @($endpoint, $probeOutput))
        $evidence = Get-Content -LiteralPath $probeOutput -Raw | ConvertFrom-Json
        if ($evidence.status -ne 'PASS' -or $evidence.technology -ne $Technology -or $evidence.PathCount -lt 1) {
            throw "C30 probe evidence is invalid for $Technology."
        }
        Write-Host "C30_REAL_HUB_PROBE PASS technology=$Technology endpoint=$endpoint paths=$($evidence.PathCount) elapsed_ms=$($evidence.ElapsedMilliseconds)"
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(10000) | Out-Null
        }
        foreach ($log in @($hubOut, $hubError)) {
            if (Test-Path -LiteralPath $log) {
                Write-Host "C30_HUB_LOG file=$log"
                Get-Content -LiteralPath $log -Tail 30 | ForEach-Object { Write-Host $_ }
            }
        }
    }
}

Push-Location -LiteralPath (Join-Path $repoRoot 'spikes')
try {
switch ($Mode) {
    'MauiWorkload' { Invoke-DotNet @('--version'); Invoke-DotNet @('workload', 'install', 'maui') }
    'MauiWindows' {
        Invoke-DotNet @('build', 'c30-maui/Kandoo.C30.Maui/Kandoo.C30.Maui.csproj', '--configuration', 'Release', '--framework', 'net8.0-windows10.0.19041.0')
        Write-BuildEvidence -Technology 'MAUI' -Target 'Windows' -OutputDirectory 'c30-maui/Kandoo.C30.Maui/bin/Release/net8.0-windows10.0.19041.0'
    }
    'MauiAndroid' {
        Invoke-DotNet @('build', 'c30-maui/Kandoo.C30.Maui/Kandoo.C30.Maui.csproj', '--configuration', 'Release', '--framework', 'net8.0-android')
        Write-BuildEvidence -Technology 'MAUI' -Target 'Android' -OutputDirectory 'c30-maui/Kandoo.C30.Maui/bin/Release/net8.0-android'
    }
    'MauiProbe' { Invoke-HubProbe -Technology 'MAUI' }
    'AvaloniaWorkload' { Invoke-DotNet @('--version'); Invoke-DotNet @('workload', 'install', 'android') }
    'AvaloniaWindows' {
        Invoke-DotNet @('build', 'c30-avalonia/Kandoo.C30.Avalonia.Desktop/Kandoo.C30.Avalonia.Desktop.csproj', '--configuration', 'Release')
        Write-BuildEvidence -Technology 'Avalonia' -Target 'Windows' -OutputDirectory 'c30-avalonia/Kandoo.C30.Avalonia.Desktop/bin/Release/net8.0'
    }
    'AvaloniaAndroid' {
        Invoke-DotNet @('build', 'c30-avalonia/Kandoo.C30.Avalonia.Android/Kandoo.C30.Avalonia.Android.csproj', '--configuration', 'Release')
        Write-BuildEvidence -Technology 'Avalonia' -Target 'Android' -OutputDirectory 'c30-avalonia/Kandoo.C30.Avalonia.Android/bin/Release/net8.0-android'
    }
    'AvaloniaProbe' { Invoke-HubProbe -Technology 'Avalonia' }
}
}
finally {
    Pop-Location
}
