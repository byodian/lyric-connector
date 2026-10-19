#define AppName "Lyric Connector"
#define AppVersion "0.2.1"
#define AppPublisher "Lyric Connector"

[Setup]
AppId={{D1E42F4A-4D15-4BC0-A6B1-14E74C5B27AD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Lyric Connector
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
CloseApplications=yes
RestartApplications=yes
SetupLogging=yes
SetupIconFile=..\desktop\assets\lyric-connector.ico
UninstallDisplayIcon={app}\LyricConnector.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=LyricConnector-Setup-{#AppVersion}-x64
VersionInfoVersion=0.2.1.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} desktop lyrics
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\app\LyricConnector.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\app\LyricConnector.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\app\assets\lyric-connector.ico"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\LyricConnector.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\lyric-connector.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\LyricConnector.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\lyric-connector.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\LyricConnector.exe"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
  if not Result then
    MsgBox('Lyric Connector requires Microsoft .NET Framework 4.8 or later. Install it, then run setup again.',
      mbError, MB_OK);
end;
