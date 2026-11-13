; Inno Setup script for the Windows installer. Built by .github/workflows/release.yml:
;   iscc /DAppVersion=1.2.0 /DFileVersion=1.2.0 /DNumericVersion=1.2.0 /DSourceDir=<publish folder> /DOutputDir=<dir> DeckHost.iss
; A pre-release: AppVersion 1.3.0-alpha.3+4b09090 (shown), FileVersion 1.3.0-alpha.3-4b09090 (file name).
; Installs per user (no administrator rights), like the start-with-Windows entry DeckHost makes itself.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef FileVersion
  #define FileVersion AppVersion
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\DeckHost"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

[Setup]
AppId={{5C8E7F0A-3B9D-4E61-9A27-6D3F1C0B8E42}
AppName=DeckHost
AppVersion={#AppVersion}
AppVerName=DeckHost {#AppVersion}
AppPublisher=Marco Veltman
AppPublisherURL=https://github.com/vman26/DeckHost
VersionInfoVersion={#NumericVersion}
DefaultDirName={autopf}\DeckHost
DefaultGroupName=DeckHost
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=DeckHost-{#FileVersion}-win-x64-setup
SetupIconFile=..\..\DeckHost.App\Assets\deckhost.ico
UninstallDisplayIcon={app}\DeckHost.App.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; DeckHost holds this mutex while it runs (Program.Main); setup asks to close it first.
AppMutex=Local\DeckHost.SingleInstance
CloseApplications=yes
LicenseFile=..\..\LICENSE

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\DeckHost"; Filename: "{app}\DeckHost.App.exe"
Name: "{autodesktop}\DeckHost"; Filename: "{app}\DeckHost.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DeckHost.App.exe"; Description: "{cm:LaunchProgram,DeckHost}"; Flags: nowait postinstall skipifsilent
; An update started from DeckHost (silent, /relaunch=1) starts it again.
Filename: "{app}\DeckHost.App.exe"; Flags: nowait; Check: RelaunchAfterUpdate

[UninstallRun]
; Removes the start-with-Windows entry DeckHost may have added (HKCU\...\Run\DeckHost).
Filename: "{sys}\reg.exe"; Parameters: "delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v DeckHost /f"; Flags: runhidden; RunOnceId: "RemoveRunEntry"

[Code]
function RelaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;
