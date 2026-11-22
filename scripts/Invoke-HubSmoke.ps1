param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$bytes = [int64]((Get-ChildItem -LiteralPath $publish -Recurse -File | Measure-Object -Property Length -Sum).Sum)
$mebibytes = [math]::Round($bytes / 1MB, 2)
$sizeText = "C-14 self-contained win-x64 publish: $bytes bytes ($mebibytes MiB, uncompressed)"
Set-Content -LiteralPath (Join-Path $artifacts 'c14-artifact-size.txt') -Value $sizeText -Encoding utf8
Write-Host $sizeText
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "`n## C-14 Hub artifact`n`n$sizeText`n"
}

$work = Join-Path $env:RUNNER_TEMP ("kandoo-hub-smoke-" + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $work
$stdout = Join-Path $work 'stdout.log'
$stderr = Join-Path $work 'stderr.log'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"

$env:ASPNETCORE_URLS = $baseUrl
$env:ASPNETCORE_ENVIRONMENT = 'Testing'
$env:ConnectionStrings__HubSqlite = "Data Source=$(Join-Path $work 'hub.db');Pooling=False"
$env:Hub__OwnerBootstrapProof = 'ci-smoke-owner-proof'
$env:Hub__LocalShutdownEnabled = 'true'

function Invoke-HubJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null
    )
    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $parameters = @{
        Uri = "$baseUrl$Path"
        Method = $Method
        Headers = $headers
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    return Invoke-RestMethod @parameters
}

function Submit-HubOperation {
    param(
        [string]$StoreId,
        [string]$DeviceId,
        [string]$SessionId,
        [string]$Token,
        [string]$OperationId,
        [string]$OperationType,
        [string]$CanonicalPayloadJson
    )
    $canonical = '{"base_version":null,"device_id":"' + $DeviceId + '","operation_id":"' + $OperationId +
        '","operation_type":"' + $OperationType + '","payload":' + $CanonicalPayloadJson + ',"store_id":"' + $StoreId + '"}'
    $hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonical))
    $requestHash = [Convert]::ToHexString($hashBytes).ToLowerInvariant()
    $body = @{
        protocol_version = '0'
        session_id = $SessionId
        store_id = $StoreId
        device_id = $DeviceId
        operation_id = $OperationId
        operation_type = $OperationType
        base_version = $null
        payload = ($CanonicalPayloadJson | ConvertFrom-Json)
        request_hash = $requestHash
    }
    return Invoke-HubJson -Method POST -Path "/v1/stores/$StoreId/operations" -Body $body -Token $Token
}

$process = $null
try {
    $process = Start-Process -FilePath (Join-Path $publish 'Kandoo.Hub.exe') -WorkingDirectory $work `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $ready = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($process.HasExited) {
            throw "Hub exited before health became ready. Exit code: $($process.ExitCode)"
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
    if (-not $ready) { throw 'Hub health endpoint did not become ready.' }

    $storeId = [guid]::NewGuid().ToString('D')
    $hubDeviceId = [guid]::NewGuid().ToString('D')
    $ownerUserId = [guid]::NewGuid().ToString('D')
    $deviceId = [guid]::NewGuid().ToString('D')
    $owner = Invoke-HubJson -Method POST -Path "/v1/stores/$storeId/owner-sessions" -Body @{
        hub_device_id = $hubDeviceId
        owner_user_id = $ownerUserId
        bootstrap_proof = 'ci-smoke-owner-proof'
    }
    if ($owner.status -ne 'SESSION_ACTIVE') { throw 'Owner session failed.' }

    $grantResponse = Invoke-HubJson -Method POST -Path "/v1/stores/$storeId/pairing-grants" -Token $owner.session_token -Body @{
        hub_device_id = $hubDeviceId
        hub_endpoint = 'https://hub.lan'
        requested_device_role = 'COMPANION'
    }
    $grant = $grantResponse.qr
    $localPage = Invoke-WebRequest -Uri "$baseUrl/" -UseBasicParsing
    if (-not $localPage.Content.Contains($grant.nonce)) { throw 'Local pairing page does not contain the grant payload.' }
    $localGrant = Invoke-RestMethod -Uri "$baseUrl/pairing-grant"
    if ($localGrant.nonce -ne $grant.nonce) { throw 'Local pairing payload differs from the issued grant.' }

    $paired = Invoke-HubJson -Method POST -Path '/v1/pairing-grants/consume' -Body @{
        pairing_id = $grant.pairing_id
        nonce = $grant.nonce
        device_id = $deviceId
        label = 'CI smoke counter'
        platform = 'WINDOWS'
        device_public_key = 'ci-smoke-public-key'
    }
    if ($paired.status -ne 'DEVICE_PAIRED') { throw 'Device pairing failed.' }

    $product = Invoke-HubJson -Method POST -Path "/v1/stores/$storeId/products" -Token $paired.session_token -Body @{
        operation_id = 'ci-smoke-product-1'
        name = 'CI smoke product'
        unit_price_rials = 12500
        initial_stock_millis = 10000
        category = 'Smoke'
    }
    if ($product.status -ne 'CREATED' -or $product.product.product_tag_number -ne 1) {
        throw 'Product creation failed.'
    }

    $sessionToken = $paired.session_token
    $sessionId = $paired.session.session_id
    $draft = Submit-HubOperation -StoreId $storeId -DeviceId $deviceId -SessionId $sessionId -Token $sessionToken `
        -OperationId 'ci-smoke-draft-1' -OperationType 'invoice.draft_create' `
        -CanonicalPayloadJson '{"customer_phone":"+989120000099","store_name":"CI Smoke"}'
    if ($draft.status -ne 'SUCCEEDED' -or $null -ne $draft.response.invoice.display_number) {
        throw 'Draft creation failed or allocated a display number early.'
    }
    $invoiceId = $draft.response.invoice.invoice_id
    $productId = $product.product.product_id
    $addPayload = '{"description":"CI smoke product","invoice_id":"' + $invoiceId + '","line_total_rials":25000,"product_id":"' +
        $productId + '","quantity_millis":2000,"unit_price_rials":12500}'
    $added = Submit-HubOperation -StoreId $storeId -DeviceId $deviceId -SessionId $sessionId -Token $sessionToken `
        -OperationId 'ci-smoke-item-1' -OperationType 'invoice.item_add' -CanonicalPayloadJson $addPayload
    if ($added.status -ne 'SUCCEEDED') { throw 'Adding the invoice item failed.' }

    $issuePayload = '{"invoice_id":"' + $invoiceId + '"}'
    $issued = Submit-HubOperation -StoreId $storeId -DeviceId $deviceId -SessionId $sessionId -Token $sessionToken `
        -OperationId 'ci-smoke-issue-1' -OperationType 'invoice.issue' -CanonicalPayloadJson $issuePayload
    if ($issued.status -ne 'SUCCEEDED' -or $issued.response.invoice.display_number -ne 1) {
        throw 'Invoice issue failed or did not allocate display number 1.'
    }

    $stopping = Invoke-HubJson -Method POST -Path '/host/shutdown'
    if ($stopping.status -ne 'HOST_STOPPING') { throw 'Graceful shutdown was not accepted.' }
    if (-not $process.WaitForExit(15000)) { throw 'Hub did not stop gracefully within 15 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Hub graceful shutdown exit code was $($process.ExitCode)." }

    $result = @{
        status = 'C14_SMOKE_PASSED'
        store_id = $storeId
        invoice_id = $invoiceId
        display_number = 1
        artifact_bytes = $bytes
        artifact_mib = $mebibytes
        graceful_shutdown_exit_code = $process.ExitCode
    }
    $result | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $artifacts 'c14-smoke-result.json') -Encoding utf8
    Write-Host "C14_SMOKE_PASSED display_number=1 artifact_bytes=$bytes"
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
