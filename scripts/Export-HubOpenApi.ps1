param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$null = New-Item -ItemType Directory -Path $ArtifactDirectory -Force
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$work = Join-Path $env:RUNNER_TEMP ("kandoo-openapi-" + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$stdout = Join-Path $work 'stdout.log'
$stderr = Join-Path $work 'stderr.log'
$output = Join-Path $artifacts 'kandoo-hub-openapi.json'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"

$env:ASPNETCORE_URLS = $baseUrl
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ConnectionStrings__HubSqlite = "Data Source=$(Join-Path $work 'hub.db');Pooling=False"
$env:Hub__OwnerBootstrapProof = 'ci-openapi-placeholder'
$env:Hub__Backup__Enabled = 'false'
$env:Hub__Capture__Enabled = 'false'

$process = $null
try {
    $process = Start-Process -FilePath (Get-Command dotnet).Source `
        -ArgumentList @((Join-Path $publish 'Kandoo.Hub.dll'), '--urls', $baseUrl) `
        -WorkingDirectory $work -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) {
            throw "Hub exited before OpenAPI export. Exit code: $($process.ExitCode)"
        }
        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 2
            if ($health.status -eq 'ok') {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { throw 'Hub health endpoint did not become ready for OpenAPI export.' }

    Invoke-WebRequest -Uri "$baseUrl/openapi/v1.json" -OutFile $output -TimeoutSec 30
    $document = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if ([string]$document.openapi -notlike '3.*') { throw 'The generated document is not OpenAPI 3.x.' }
    $requiredPaths = @(
        '/v1/stores/{store_id}/products',
        '/v1/stores/{store_id}/products/cascade-options',
        '/v1/stores/{store_id}/products/{product_id}',
        '/v1/stores/{store_id}/service-types',
        '/v1/stores/{store_id}/invoices/drafts',
        '/v1/stores/{store_id}/invoices/{invoice_id}/items',
        '/v1/stores/{store_id}/invoices/{invoice_id}/issue',
        '/v1/stores/{store_id}/invoices/{invoice_id}/sms/resend',
        '/v1/stores/{store_id}/invoices/{invoice_id}/sms/{operation_id}',
        '/v1/stores/{store_id}/reviews',
        '/v1/stores/{store_id}/reviews/{review_id}/issue'
    )
    foreach ($path in $requiredPaths) {
        if ($null -eq $document.paths.PSObject.Properties[$path]) {
            throw "Generated OpenAPI is missing $path"
        }
    }

    $validation = @{
        status = 'OPENAPI_GENERATED_AND_VALIDATED'
        openapi = [string]$document.openapi
        required_path_count = $requiredPaths.Count
        generated_path_count = @($document.paths.PSObject.Properties).Count
        artifact = 'kandoo-hub-openapi.json'
    }
    $validation | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $artifacts 'kandoo-hub-openapi-validation.json') -Encoding utf8
    Write-Host "OPENAPI_GENERATED_AND_VALIDATED paths=$($validation.generated_path_count) artifact=$output"
}
catch {
    Write-Host '--- Hub stdout ---'
    if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout }
    Write-Host '--- Hub stderr ---'
    if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr }
    throw
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
