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

function Clear-Generated {
    foreach ($generated in @('bin', 'obj')) {
        $path = Join-Path $appRoot $generated
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

Push-Location -LiteralPath $appRoot
try {
    Set-Content -LiteralPath (Join-Path $appRoot 'App.xaml') -Value $minimalApp -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml') -Value $minimalPage -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml.cs') -Value $minimalCode -Encoding utf8NoBOM

    Clear-Generated
    & dotnet build Kandoo.App.csproj --configuration Release --framework net8.0-windows10.0.19041.0
    $plainBuild = $LASTEXITCODE
    Write-Host "::warning::C40_XAML_BISECT variant=minimal_plain_build exit_code=$plainBuild"

    Clear-Generated
    & dotnet build Kandoo.App.csproj --configuration Release --framework net8.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win10-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true
    $selfContainedBuild = $LASTEXITCODE
    Write-Host "::warning::C40_XAML_BISECT variant=minimal_self_contained_build exit_code=$selfContainedBuild"
}
finally {
    foreach ($file in $sourceFiles) {
        Copy-Item -LiteralPath (Join-Path $backupRoot $file) -Destination (Join-Path $appRoot $file) -Force
    }
    Clear-Generated
    Pop-Location
}

# Diagnostics are not the publish gate. Let the real publish step run afterward.
exit 0
