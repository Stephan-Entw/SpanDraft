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
ChangesAssociations=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\src\SpanDraft.Desktop\Assets\ProjectFileIcon\spandraft-project.ico"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Classes\.spandraft"; ValueType: string; ValueData: "SpanDraft.Project"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\SpanDraft.Project"; ValueType: string; ValueData: "SpanDraft Project"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SpanDraft.Project\DefaultIcon"; ValueType: string; ValueData: """{app}\spandraft-project.ico"",0"
Root: HKCU; Subkey: "Software\Classes\SpanDraft.Project\shell\open\command"; ValueType: string; ValueData: """{app}\SpanDraft.Desktop.exe"" ""%1"""

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{autoprograms}\SpanDraft"; Filename: "{app}\SpanDraft.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SpanDraft"; Filename: "{app}\SpanDraft.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SpanDraft.Desktop.exe"; Description: "{cm:LaunchProgram,SpanDraft}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
