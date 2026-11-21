<# C-85: start-all — شروع همه سرویس‌های کاندو برای نمایش محلی #>
param(
    [string]$DataDirectory = (Join-Path $PWD 'data'),
    [string]$HubUrl = 'http://localhost:5000'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($PSScriptRoot\..)
$dataDir = [IO.Path]::GetFullPath($DataDirectory)
$publishDir = Join-Path $repoRoot 'publish\win-x64'
$hubExe = Join-Path $publishDir 'Kandoo.Hub.exe'

Write-Host "Kandoo start-all — شروع همه سرویس‌ها" -ForegroundColor Cyan
Write-Host "دیتابین: $dataDir" -ForegroundColor Gray
Write-Host "Hub: $HubUrl" -ForegroundColor Gray

# دایرکتوری‌های مورد نیاز
foreach ($dir in @($dataDir, (Join-Path $dataDir 'logs'), (Join-Path $dataDir 'backups'), (Join-Path $dataDir 'captures'))) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# متغیرهای محیطی
$env:ConnectionStrings__HubSqlite = "Data Source=$(Join-Path $dataDir 'hub.db');Pooling=False"
$env:Hub__Demo__Mode = 'true'
$env:Hub__Demo__BaseUrl = $HubUrl
$env:Hub__LogDirectory = Join-Path $dataDir 'logs'
$env:Hub__Backup__DirectoryPath = Join-Path $dataDir 'backups'
$env:Hub__Capture__CaptureDirectory = Join-Path $dataDir 'captures'

if (-not (Test-Path $hubExe)) {
    Write-Warning "نشر خودکافی هب یافت نشد: $hubExe"
    Write-Host "ابتدا اجرا کنید: dotnet publish src/Kandoo.Hub/Kandoo.Hub.csproj --configuration Release --runtime win-x64 --self-contained true --output publish\win-x64" -ForegroundColor Yellow
    exit 1
}

Write-Host "شروع Kandoo.Hub..." -ForegroundColor Cyan
Start-Process -FilePath $hubExe -ArgumentList '--urls', $HubUrl -WorkingDirectory $publishDir -PassThru | Out-Null

Write-Host "هاهوب شروع شد. برای قطع کردن، Ctrl+C بزنید." -ForegroundColor Green
Write-Host "برنامه ویندوز را از publications/net8.0-windows10.0.19041.0/win10-x64/ اجرا کنید." -ForegroundColor Gray

# انتظار تا کاربر Ctrl+C بزن�
try {
    while ($true) { Start-Sleep -Seconds 5 }
} catch {
    Write-Host "توقف توسط کاربر." -ForegroundColor Yellow
}
exit 0