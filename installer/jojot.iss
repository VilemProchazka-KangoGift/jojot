; ==========================================================================
; JoJot Installer - Inno Setup Script
; ==========================================================================
;
; Build prerequisites:
;   1. Publish the application (self-contained, win-x64):
;      dotnet publish experiments/JoJot.Avalonia/JoJot.Avalonia.csproj -c Release -r win-x64 --self-contained -p:JoJotProduction=true
;
;   2. Compile this installer script:
;      iscc /DPublishDir=<absolute path to the production publish> installer/jojot.iss
;      (or use the full path to ISCC.exe from your Inno Setup installation)
;
; Output:
;   installer/output/JoJot-2026.11.5-Setup.exe
;
; ==========================================================================

#ifndef PublishDir
#error Pass /DPublishDir with the path to a publish built with JoJotProduction=true.
#endif

[Setup]
AppId={{B7E45A2C-8D31-4F6A-9E52-1C3D7A8B9F04}
AppName=JoJot
AppVersion=2026.11.5
AppVerName=JoJot 2026.11.5
AppPublisher=Vilem Prochazka
DefaultDirName={autopf}\JoJot
DefaultGroupName=JoJot
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
OutputDir=..\installer\output
OutputBaseFilename=JoJot-2026.11.5-Setup
SetupIconFile=..\experiments\JoJot.Avalonia\Assets\jojot.ico
UninstallDisplayIcon={app}\JoJot.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
; Install the 64-bit app under Program Files, not Program Files (x86). Existing installs stay
; where they are (Inno reuses the previous install dir) until uninstalled and reinstalled.
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UsedUserAreasWarning=no
CloseApplications=yes
CloseApplicationsFilter=JoJot.exe
RestartApplications=no
AppMutex=Local\JoJot_SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\JoJot.Avalonia.exe"; DestDir: "{app}"; DestName: "JoJot.exe"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "JoJot.Avalonia.exe,*.pdb,*.dbg"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\JoJot"; Filename: "{app}\JoJot.exe"
Name: "{group}\Uninstall JoJot"; Filename: "{uninstallexe}"

[Tasks]
Name: "autostart"; Description: "Launch JoJot when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "JoJot"; ValueData: """{app}\JoJot.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\JoJot.exe"; Description: "Launch JoJot"; Flags: nowait postinstall skipifsilent runasoriginaluser
