; Kandoo Hub — Windows Installer (C-65)
; ======================================
; Build a self-contained setup.exe for the Kandoo Hub Windows host.
; No .NET runtime is required on the target machine.
;
; Build:
;   1. Publish the self-contained host:
;        dotnet publish src/Kandoo.Hub/Kandoo.Hub.csproj `
;           --configuration Release --runtime win-x64 --self-contained true `
;           --output packaging\HubPilot\publish
;   2. Compile this script:
;        iscc packaging\HubPilot\KandooHub.Installer.iss
;   Result: packaging\HubPilot\Output\KandooHub-Setup.exe
;
#define MyAppName "Kandoo Hub"
#define MyAppPublisher "Kandoo"
#define MyAppURL "https://kandoo.ir"
#define MyAppExeName "Kandoo.Hub.exe"
#define MyAppVersion "1.1.0"
#define InstallerName "KandooHub-Setup"

[Setup]
; Fixed AppId keeps upgrades replacing the same installation.
AppId={4F1C2A3E-8B6D-4E1F-9C2D-7A3B5E1D9F40}
AppName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
Version={#MyAppVersion}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
Uninstallable=yes
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "fa"; MessagesFile: "compiler:Languages\Farsi.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained Hub publish output (built in step 1 above).
Source: "packaging\HubPilot\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
; Launcher and configuration shipped with the installer.
Source: "packaging\HubPilot\Start-KandooHub.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "packaging\HubPilot\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{desktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\Start-KandooHub.ps1"; \
    Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
