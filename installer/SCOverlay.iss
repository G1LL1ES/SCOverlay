#ifndef AppVersion
  #error AppVersion must be supplied by the packaging script.
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by the packaging script.
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by the packaging script.
#endif

#define AppName "SC Overlay"
#define AppExeName "SCOverlay.exe"

[Setup]
AppId={{2E7E52E9-A0E7-4C4D-B082-D0A479CFA87E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=G1LL1ES
AppPublisherURL=https://github.com/G1LL1ES/SCOverlay
AppSupportURL=https://github.com/G1LL1ES/SCOverlay/issues
AppUpdatesURL=https://github.com/G1LL1ES/SCOverlay/releases
AppCopyright=Copyright (c) SC Overlay Contributors
DefaultDirName={localappdata}\Programs\SCOverlay
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=SCOverlay-{#AppVersion}-win-x64-setup
SetupIconFile=..\src\SCOverlay.App\Assets\sc-overlay.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
AppMutex=Local\SCOverlay.App.SingleInstance
CloseApplications=no
RestartApplications=no
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
VersionInfoCompany=SC Overlay Contributors
VersionInfoDescription=SC Overlay Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "README-PORTABLE.txt"; Flags: ignoreversion recursesubdirs createallsubdirs

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Icons]
Name: "{autoprograms}\SC Overlay"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startmenu
Name: "{autodesktop}\SC Overlay"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch SC Overlay"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent shellexec
