param(
    [string]$DataDirectory = $(Join-Path $env:ProgramData 'Kandoo')
)

$ErrorActionPreference = 'Stop'
$install = [IO.Path]::GetFullPath($PSScriptRoot)
$data = [IO.Path]::GetFullPath($DataDirectory)
if ($data.Equals($install, [StringComparison]::OrdinalIgnoreCase) -or
    $data.StartsWith($install + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Kandoo data directory must be outside the install folder.'
}

$database = Join-Path $data 'hub.db'
$env:ConnectionStrings__HubSqlite = "Data Source=$database;Pooling=False"
$env:Hub__LogDirectory = Join-Path $data 'logs'
$env:Hub__Backup__DirectoryPath = Join-Path $data 'backups'
$env:Hub__Capture__CaptureDirectory = Join-Path $data 'captures'
foreach ($directory in @($data, $env:Hub__LogDirectory, $env:Hub__Backup__DirectoryPath, $env:Hub__Capture__CaptureDirectory)) {
    $null = New-Item -ItemType Directory -Path $directory -Force
}

& (Join-Path $PSScriptRoot 'Kandoo.Hub.exe')
exit $LASTEXITCODE
