; Publish with: dotnet publish experiments/JoJot.Avalonia/JoJot.Avalonia.csproj -c Release -r win-x64 --self-contained
; Compile with: ISCC.exe installer/jojot-avalonia.iss

#ifndef PublishDir
#define PublishDir "..\experiments\JoJot.Avalonia\bin\Release\net11.0\win-x64\publish"
#endif

[Setup]
AppId={{BA4A3932-C6CB-4360-A7A3-547B75D27673}
AppName=JoJot Avalonia Preview
AppVersion=2026.11.3
AppVerName=JoJot Avalonia Preview 2026.11.3
AppPublisher=Vilem Prochazka
DefaultDirName={localappdata}\Programs\JoJot Avalonia Preview
DefaultGroupName=JoJot Avalonia Preview
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=JoJot-Avalonia-2026.11.3-Setup
SetupIconFile=..\JoJot\Assets\jojot.ico
UninstallDisplayIcon={app}\JoJot.Avalonia.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=JoJot.Avalonia.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.dbg"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\JoJot Avalonia Preview"; Filename: "{app}\JoJot.Avalonia.exe"
Name: "{group}\Uninstall JoJot Avalonia Preview"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\JoJot.Avalonia.exe"; Description: "Launch JoJot Avalonia Preview"; Flags: nowait postinstall skipifsilent
