#ifndef AppVersion
  #error AppVersion must be supplied by package-windows.ps1.
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by package-windows.ps1.
#endif
#ifndef RepoRoot
  #error RepoRoot must be supplied by package-windows.ps1.
#endif

[Setup]
AppId=SpanDraft
AppName=SpanDraft
AppVersion={#AppVersion}
AppPublisher=Stephan Antipin
DefaultDirName={localappdata}\Programs\SpanDraft
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\SpanDraft.Desktop.exe
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{autoprograms}\SpanDraft"; Filename: "{app}\SpanDraft.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SpanDraft"; Filename: "{app}\SpanDraft.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SpanDraft.Desktop.exe"; Description: "{cm:LaunchProgram,SpanDraft}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
