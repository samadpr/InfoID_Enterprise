; ============================================================
;  InfoID installer (Inno Setup 6)
; ============================================================
#define AppName      "InfoID"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Infome Technologies"
#define AppURL       "https://infomeuae.com"
#define AppExe       "InfoID.Desktop.exe"
#define SourceDir    "..\Release\app"

[Setup]
; Never change AppId after the first release (it links upgrades together)
AppId={{500c8a7a-e2c6-4d2b-9121-32804e4f56c7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=InfoID_Setup_{#AppVersion}
SetupIconFile=..\InfoID.Desktop\Assets\Icons\InfoID.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 64-bit Windows 10 or later only
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
; Ask to close InfoID if it is running during install/upgrade
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; The whole published app; .pdb debug files are left out of the installer (archive them separately)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

; NOTE: customer data lives in %APPDATA%\InfoID and is deliberately NOT removed on uninstall,
; so designs, cardholders and licences survive upgrades and reinstalls.