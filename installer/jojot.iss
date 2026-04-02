; ==========================================================================
; JoJot Installer - Inno Setup Script
; ==========================================================================
;
; Build prerequisites:
;   1. Publish the application (self-contained, win-x64):
;      dotnet publish JoJot/JoJot.csproj -c Release -r win-x64 --self-contained
;
;   2. Compile this installer script:
;      iscc installer/jojot.iss
;      (or use the full path to ISCC.exe from your Inno Setup installation)
;
; Output:
;   installer/output/JoJot-2026.4.7-Setup.exe
;
; ==========================================================================

[Setup]
AppId={{B7E45A2C-8D31-4F6A-9E52-1C3D7A8B9F04}
AppName=JoJot
AppVersion=2026.4.7
AppVerName=JoJot 2026.4.7
AppPublisher=Vilem Prochazka
DefaultDirName={autopf}\JoJot
DefaultGroupName=JoJot
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
OutputDir=..\installer\output
OutputBaseFilename=JoJot-2026.4.7-Setup
SetupIconFile=..\JoJot\Assets\jojot.ico
UninstallDisplayIcon={app}\JoJot.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
UsedUserAreasWarning=no
CloseApplications=force
CloseApplicationsFilter=JoJot.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\JoJot\bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\JoJot"; Filename: "{app}\JoJot.exe"
Name: "{group}\Uninstall JoJot"; Filename: "{uninstallexe}"

[Tasks]
Name: "autostart"; Description: "Launch JoJot when Windows starts"; GroupDescription: "Additional options:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "JoJot"; ValueData: """{app}\JoJot.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\JoJot.exe"; Description: "Launch JoJot"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\JoJot');
    if DirExists(DataDir) then
    begin
      if MsgBox('Delete your JoJot data (notes and preferences)?'#13#10#13#10'If you choose No, your data will be preserved for future installations.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
