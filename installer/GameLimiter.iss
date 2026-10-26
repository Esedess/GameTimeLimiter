#ifndef AppVersion
  #define AppVersion "1.1.0-beta.1"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\publish\win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir SourcePath + "..\artifacts\release"
#endif

[Setup]
AppId={{EEEB9AF5-5C83-4304-9E84-826BEBC99FD0}
AppName=GameLimiter Beta
AppVersion={#AppVersion}
AppVerName=GameLimiter Beta {#AppVersion}
DefaultDirName={localappdata}\Programs\GameLimiter-Beta
DefaultGroupName=GameLimiter Beta
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#ReleaseDir}
OutputBaseFilename=GameLimiter-{#AppVersion}-win-x64-Setup
SetupIconFile=..\assets\GameLimiter.ico
UninstallDisplayIcon={app}\GameLimiter.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\GameLimiter-Application
CloseApplications=no
RestartApplications=no
Uninstallable=yes
UsePreviousAppDir=yes
VersionInfoVersion=1.1.0.1

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\GameLimiter Beta"; Filename: "{app}\GameLimiter.exe"; WorkingDir: "{app}"; Comment: "GameLimiter — настройки игрового времени"
Name: "{autodesktop}\GameLimiter Beta"; Filename: "{app}\GameLimiter.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\GameLimiter.exe"; Description: "{cm:LaunchProgram,GameLimiter}"; Flags: nowait postinstall skipifsilent

; No UninstallDelete: family data in LocalAppData\GameLimiter must survive uninstall.
