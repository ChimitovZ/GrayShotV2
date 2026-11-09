#ifndef AppExe
#define AppExe AddBackslash(SourcePath) + "..\dist\GrayShotV2.exe"
#endif
#define AppVersion GetStringFileInfo(AppExe, "ProductVersion")

[Setup]
AppId={{6DCA2E79-3D6D-46D7-AE94-5AB7E6629D37}
AppName=GrayShotV2
AppVersion={#AppVersion}
DefaultDirName={localappdata}\GrayShotV2
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\GrayShotV2.exe
SetupIconFile=..\src\GrayShotV2.ico
OutputDir=..\dist
OutputBaseFilename=GrayShotV2-Setup
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; Flags: unchecked

[CustomMessages]
english.AutoStart=Start GrayShotV2 when I sign in to Windows
russian.AutoStart=Запускать GrayShotV2 при входе в Windows

[Files]
Source: "{#AppExe}"; DestDir: "{app}"

[InstallDelete]
Type: files; Name: "{userstartup}\GrayShotV2.lnk"; Tasks: not autostart

[Icons]
Name: "{userprograms}\GrayShotV2"; Filename: "{app}\GrayShotV2.exe"; WorkingDir: "{app}"
Name: "{userstartup}\GrayShotV2"; Filename: "{app}\GrayShotV2.exe"; WorkingDir: "{app}"; Tasks: autostart

[Run]
Filename: "{app}\GrayShotV2.exe"; Description: "{cm:LaunchProgram,GrayShotV2}"; Flags: nowait postinstall skipifsilent
