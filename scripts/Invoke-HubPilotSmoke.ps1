param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$script:package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$work = Join-Path $env:RUNNER_TEMP ("kandoo-pilot-smoke-" + [guid]::NewGuid().ToString('N'))
$installA = Join-Path $work 'install-a'
$installB = Join-Path $work 'install-b'
$data = Join-Path $work 'data'
$null = New-Item -ItemType Directory -Path $work
$script:baseUrl = $null
$process = $null
$dumpLogs = @()

function Copy-PilotPackage([string]$Destination) {
    $null = New-Item -ItemType Directory -Path $Destination -Force
    Copy-Item -Path (Join-Path $script:package '*') -Destination $Destination -Recurse -Force
}

function Invoke-PilotJson {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null
    )
    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $parameters = @{ Uri = "$($script:baseUrl)$Path"; Method = $Method; Headers = $headers }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }
    return Invoke-RestMethod @parameters
}

function Wait-PilotHub([System.Diagnostics.Process]$HubProcess) {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        if ($HubProcess.HasExited) { throw "Hub exited before health became ready: $($HubProcess.ExitCode)" }
        try {
            $health = Invoke-RestMethod -Uri "$($script:baseUrl)/health" -TimeoutSec 2
            if ($health.status -eq 'ok') { return }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    throw 'Pilot Hub health did not become ready.'
}

function Stop-PilotHub([System.Diagnostics.Process]$HubProcess) {
    $stopping = Invoke-PilotJson -Method POST -Path '/host/shutdown'
    if ($stopping.status -ne 'HOST_STOPPING') { throw 'Pilot shutdown was not accepted.' }
    if (-not $HubProcess.WaitForExit(15000)) { throw 'Pilot Hub did not stop within 15 seconds.' }
    if ($HubProcess.ExitCode -ne 0) { throw "Pilot Hub exit code was $($HubProcess.ExitCode)." }
}

try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    $script:baseUrl = "http://127.0.0.1:$port"

    $env:ASPNETCORE_URLS = $script:baseUrl
    $env:ASPNETCORE_ENVIRONMENT = 'Testing'
    $env:ConnectionStrings__HubSqlite = "Data Source=$(Join-Path $data 'hub.db');Pooling=False"
    $env:Hub__OwnerBootstrapProof = 'c22-pilot-owner-proof'
    $env:Hub__LocalShutdownEnabled = 'true'
    $env:Hub__LogDirectory = Join-Path $data 'logs'
    $env:Hub__Backup__Enabled = 'true'
    $env:Hub__Backup__IntervalSeconds = '1'
    $env:Hub__Backup__RetentionFiles = '2'
    $env:Hub__Backup__DirectoryPath = Join-Path $data 'backups'
    $env:Hub__Capture__CaptureDirectory = Join-Path $data 'captures'

    Copy-PilotPackage $installA
    $stdoutA = Join-Path $work 'install-a.stdout.log'
    $stderrA = Join-Path $work 'install-a.stderr.log'
    $dumpLogs += @($stdoutA, $stderrA)
    $process = Start-Process -FilePath (Join-Path $installA 'Kandoo.Hub.exe') -WorkingDirectory $installA `
        -RedirectStandardOutput $stdoutA -RedirectStandardError $stderrA -PassThru
    Wait-PilotHub $process

    $storeId = [guid]::NewGuid().ToString('D')
    $hubDeviceId = [guid]::NewGuid().ToString('D')
    $owner = Invoke-PilotJson -Method POST -Path "/v1/stores/$storeId/owner-sessions" -Body @{
        hub_device_id = $hubDeviceId
        owner_user_id = [guid]::NewGuid().ToString('D')
        bootstrap_proof = 'c22-pilot-owner-proof'
    }
    $grantResponse = Invoke-PilotJson -Method POST -Path "/v1/stores/$storeId/pairing-grants" `
        -Token $owner.session_token -Body @{
            hub_device_id = $hubDeviceId
            hub_endpoint = 'https://hub.lan'
            requested_device_role = 'COMPANION'
        }
    $grant = $grantResponse.qr
    $localPage = Invoke-WebRequest -Uri "$($script:baseUrl)/" -UseBasicParsing
    if (-not $localPage.Content.Contains($grant.nonce)) { throw 'First-run pairing page is missing the grant.' }
    $consoleText = Get-Content -LiteralPath $stdoutA -Raw
    if (-not $consoleText.Contains('KANDOO_PAIRING_GRANT=') -or -not $consoleText.Contains($grant.nonce)) {
        throw 'First-run pairing console payload is missing.'
    }
    $paired = Invoke-PilotJson -Method POST -Path '/v1/pairing-grants/consume' -Body @{
        pairing_id = $grant.pairing_id
        nonce = $grant.nonce
        device_id = [guid]::NewGuid().ToString('D')
        label = 'C22 pilot counter'
        platform = 'WINDOWS'
        device_public_key = 'c22-pilot-public-key'
    }
    $productBody = @{
        operation_id = 'c22-pilot-product-1'
        name = 'C22 durable pilot product'
        unit_price_rials = 12500
        initial_stock_millis = 10000
    }
    $created = Invoke-PilotJson -Method POST -Path "/v1/stores/$storeId/products" `
        -Token $paired.session_token -Body $productBody
    if ($created.status -ne 'CREATED') { throw 'Initial pilot product creation failed.' }

    Stop-PilotHub $process
    $process = $null
    $database = Join-Path $data 'hub.db'
    if (-not (Test-Path -LiteralPath $database)) { throw 'The external SQLite database was not created.' }
    $hashBefore = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash

    Remove-Item -LiteralPath $installA -Recurse -Force
    Copy-PilotPackage $installB
    $hashAfter = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash
    if ($hashAfter -ne $hashBefore) { throw 'Program-folder replacement changed the external database.' }

    $stdoutB = Join-Path $work 'install-b.stdout.log'
    $stderrB = Join-Path $work 'install-b.stderr.log'
    $dumpLogs += @($stdoutB, $stderrB)
    $process = Start-Process -FilePath (Join-Path $installB 'Kandoo.Hub.exe') -WorkingDirectory $installB `
        -RedirectStandardOutput $stdoutB -RedirectStandardError $stderrB -PassThru
    Wait-PilotHub $process
    $replay = Invoke-PilotJson -Method POST -Path "/v1/stores/$storeId/products" `
        -Token $paired.session_token -Body $productBody
    if (-not $replay.idempotent_replay -or
        $replay.product.product_id -ne $created.product.product_id) {
        throw 'Durable product replay failed after program-folder replacement.'
    }
    Stop-PilotHub $process
    $process = $null

    $backups = @(Get-ChildItem -LiteralPath (Join-Path $data 'backups') -Filter 'kandoo-hub-*.db' -File)
    if ($backups.Count -lt 1) { throw 'No scheduled SQLite backup was produced.' }
    $logs = @(Get-ChildItem -LiteralPath (Join-Path $data 'logs') -Filter 'hub-*.log' -File)
    if ($logs.Count -lt 1) { throw 'No external operational log was produced.' }
    $result = @{
        status = 'C22_PILOT_SMOKE_PASSED'
        first_run_pairing_console_and_page = $true
        program_folder_replaced = $true
        data_hash_unchanged_by_program_replacement = $true
        durable_idempotent_replay_after_replacement = $true
        backup_count = $backups.Count
        operational_log_count = $logs.Count
    }
    $result | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $artifacts 'c22-pilot-smoke-result.json') -Encoding utf8
    Write-Host "C22_PILOT_SMOKE_PASSED backups=$($backups.Count) logs=$($logs.Count)"
}
catch {
    foreach ($log in $dumpLogs) {
        if (Test-Path -LiteralPath $log) {
            Write-Host "--- $log ---"
            Get-Content -LiteralPath $log
        }
    }
    throw
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
