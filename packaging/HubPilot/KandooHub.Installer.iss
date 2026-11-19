; Kandoo - Windows Installer (C-87)
; ======================================
; Builds a self-contained setup.exe for the Kandoo Hub Windows host and the
; Kandoo Windows app. No .NET runtime is required on the target machine.
;
; All Source paths are relative to THIS script's folder (packaging\HubPilot).
; CI (and the manual steps below) stage the inputs under packaging\HubPilot\publish:
;   publish\hub  = self-contained Hub host publish output
;   publish\app  = Windows app publish output + Kandoo.SetupLauncher build output
; Compile:
;   iscc packaging\HubPilot\KandooHub.Installer.iss
; Result: packaging\HubPilot\Output\Kandoo-Setup-x64.exe

#define MyAppName "Kandoo"
#define MyAppPublisher "Kandoo"
#define MyAppURL "https://kandoo.ir"
#define MyHubExeName "Kandoo.Hub.exe"
#define MyAppExeName "Kandoo.SetupLauncher.exe"
#define MyAppVersion "1.1.0"
#define InstallerName "Kandoo-Setup-x64"

[Setup]
; Fixed AppId keeps upgrades replacing the same installation.
; The doubled opening brace is Inno Setup's escape for a literal brace.
AppId={{4F1C2A3E-8B6D-4E1F-9C2D-7A3B5E1D9F40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
Uninstallable=yes
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
OutputDir=Output
OutputBaseFilename={#InstallerName}

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-contained Hub publish output.
Source: "publish\hub\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Windows app + Setup Launcher output.
Source: "publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Launcher script and readme shipped with the installer.
Source: "Start-KandooHub.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
