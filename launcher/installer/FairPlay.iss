#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef ReleaseDir
  #error ReleaseDir is required
#endif

[Setup]
AppId={{D690C476-245E-4DBC-B28F-89E0C84C202F}
AppName=RDO FairPlay
AppVersion={#AppVersion}
AppPublisher=RDO FairPlay
AppPublisherURL=https://rdofairplay.com
AppSupportURL=https://discord.gg/Mp6skUnf2b
DefaultDirName={localappdata}\Programs\RDO FairPlay
DefaultGroupName=RDO FairPlay
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\CommunityFrontier.Launcher\Assets\rdo-fairplay.ico
OutputDir={#ReleaseDir}
OutputBaseFilename=RDOFairPlay-Setup-Windows-x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\fairplay-icon-{#AppVersion}.ico
ChangesAssociations=yes
AppMutex=Local\CommunityFrontier.Launcher
CloseApplications=no
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\CommunityFrontier.Launcher\Assets\rdo-fairplay.ico"; DestDir: "{app}"; DestName: "fairplay-icon-{#AppVersion}.ico"; Flags: ignoreversion
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\RDO FairPlay"; Filename: "{app}\RDOFairPlay.exe"; WorkingDir: "{app}"; IconFilename: "{app}\fairplay-icon-{#AppVersion}.ico"
Name: "{userdesktop}\RDO FairPlay"; Filename: "{app}\RDOFairPlay.exe"; WorkingDir: "{app}"; IconFilename: "{app}\fairplay-icon-{#AppVersion}.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\RDOFairPlay.exe"; Description: "Open RDO FairPlay"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall: Boolean;
var
  ExitCode: Integer;
begin
  Result := False;
  if CheckForMutexes('Local\CommunityFrontier.Launcher') then begin
    SuppressibleMsgBox('Close RDO FairPlay before uninstalling.', mbError, MB_OK, IDOK);
    Exit;
  end;
  if Exec(ExpandConstant('{app}\RDOFairPlay.exe'), '--uninstall-check', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := ExitCode = 0;
  if not Result then
    SuppressibleMsgBox('Uninstall stopped because your original game configuration could not be restored. Close Red Dead, reconnect your game drive, then use Restore original settings in RDO FairPlay and try again. Your launcher and recovery files have been kept.', mbError, MB_OK, IDOK);
end;
