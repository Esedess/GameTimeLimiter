#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\publish\win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir SourcePath + "..\artifacts\release"
#endif

[Setup]
AppId={{D0B3694E-CE1A-471E-96D8-483EA7E191E8}
AppName=GameLimiter
AppVersion={#AppVersion}
AppVerName=GameLimiter {#AppVersion}
DefaultDirName={localappdata}\Programs\GameLimiter
DefaultGroupName=GameLimiter
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
VersionInfoVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\GameLimiter"; Filename: "{app}\GameLimiter.exe"; WorkingDir: "{app}"; Comment: "GameLimiter — настройки игрового времени"
Name: "{autodesktop}\GameLimiter"; Filename: "{app}\GameLimiter.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\GameLimiter.exe"; Description: "{cm:LaunchProgram,GameLimiter}"; Flags: nowait postinstall skipifsilent

; No UninstallDelete: family data in LocalAppData\GameLimiter must survive uninstall.
