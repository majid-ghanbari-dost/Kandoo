[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$appRoot = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'src/Kandoo.App')).Path
$backupRoot = Join-Path $env:RUNNER_TEMP ('c40-xaml-backup-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
$sourceFiles = @('App.xaml', 'MainPage.xaml', 'MainPage.xaml.cs')
foreach ($file in $sourceFiles) {
    Copy-Item -LiteralPath (Join-Path $appRoot $file) -Destination (Join-Path $backupRoot $file)
}

$minimalApp = @'
<?xml version="1.0" encoding="utf-8" ?>
<Application xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Kandoo.App.App" />
'@
$minimalPage = @'
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="Kandoo.App.MainPage"
             Title="فاکتور دیجیتال کندو">
    <Label Text="فاکتور دیجیتال کندو" />
</ContentPage>
'@
$minimalCode = @'
namespace Kandoo.App;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();
}
'@

function Invoke-Variant {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$AppXml
    )
    Set-Content -LiteralPath (Join-Path $appRoot 'App.xaml') -Value $AppXml -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml') -Value $minimalPage -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml.cs') -Value $minimalCode -Encoding utf8NoBOM
    foreach ($generated in @('bin', 'obj')) {
        $path = Join-Path $appRoot $generated
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }

    $output = Join-Path $env:RUNNER_TEMP ('c40-xaml-' + $Name)
    & dotnet publish (Join-Path $appRoot 'Kandoo.App.csproj') --configuration Release --framework net8.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win10-x64 --self-contained true -p:WindowsAppSDKSelfContained=true --output $output
    $code = $LASTEXITCODE
    Write-Host "::warning::C40_XAML_BISECT variant=$Name exit_code=$code"
}

Push-Location -LiteralPath $appRoot
try {
    Invoke-Variant -Name 'minimal_without_tokens' -AppXml $minimalApp
    Invoke-Variant -Name 'minimal_with_tokens' -AppXml (Get-Content -LiteralPath (Join-Path $backupRoot 'App.xaml') -Raw)
}
finally {
    foreach ($file in $sourceFiles) {
        Copy-Item -LiteralPath (Join-Path $backupRoot $file) -Destination (Join-Path $appRoot $file) -Force
    }
    foreach ($generated in @('bin', 'obj')) {
        $path = Join-Path $appRoot $generated
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    Pop-Location
}
