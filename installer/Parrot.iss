; Parrot installer. Build it with scripts/build-installer.ps1, which publishes the exe first
; and passes the version from Parrot.App.csproj:
;   ISCC /DAppVersion=0.1.0 installer\Parrot.iss
;
; Installs per user without elevation into %LocalAppData%\Programs\Parrot. The app updates
; itself by downloading a newer copy of this installer and running it with /VERYSILENT.

#ifndef AppVersion
  #error Pass the version: ISCC /DAppVersion=x.y.z Parrot.iss
#endif

#define AppName "Parrot"
#define AppExe "Parrot.exe"
#define AppPublisher "Vitalii Kysil"

[Setup]
; Never change AppId: upgrades and the uninstaller find the installed copy by it, and
; UpdateService checks the matching uninstall key to decide it may update in place.
AppId={{9938137F-821B-4BE9-867E-57D438BFAF47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
WizardStyle=modern
SetupIconFile=..\assets\parrot.ico
WizardSmallImageFile=..\assets\parrot-256.png
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=Parrot-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
uk.AutoStart=Запускати Parrot разом із Windows
en.AutoStart=Start Parrot with Windows
uk.CloseParrot=Parrot зараз запущено.%n%nЗакрийте його (іконка в треї → «Вийти») і натисніть «Повторити».
en.CloseParrot=Parrot is running.%n%nClose it (tray icon → Exit) and press Retry.
uk.DeleteData=Видалити також ваш словник, статистику й налаштування?%n%nЯкщо залишити, вони знадобляться, коли ви знову встановите Parrot.
en.DeleteData=Also delete your dictionary, statistics and settings?%n%nKeep them if you might install Parrot again.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The same value AutoStartService writes, so the in-app switch and the installer agree.
; A silent upgrade is an update from inside the app: it keeps whatever the user set there.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Parrot"; \
  ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Check: not IsSilentUpgrade

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; An in-app update runs silently; bring the new version back up and let it say so.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[Code]
const
  AppMutexName = 'Local\Parrot.SingleInstance';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9938137F-821B-4BE9-867E-57D438BFAF47}_is1';

var
  SilentUpgrade: Boolean;

function IsSilentUpgrade(): Boolean;
begin
  Result := SilentUpgrade;
end;

// During an in-app update Parrot quits right after starting this installer, so a silent
// run waits for it; an interactive run asks the user to close it instead.
function EnsureParrotClosed(Silent: Boolean): Boolean;
var
  Attempt: Integer;
begin
  if Silent then
  begin
    for Attempt := 1 to 30 do
    begin
      if not CheckForMutexes(AppMutexName) then
      begin
        Result := True;
        Exit;
      end;
      Sleep(500);
    end;
    Result := False;
    Exit;
  end;

  while CheckForMutexes(AppMutexName) do
    if MsgBox(CustomMessage('CloseParrot'), mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;

  Result := True;
end;

function InitializeSetup(): Boolean;
begin
  // Read before this run rewrites the uninstall key.
  SilentUpgrade := WizardSilent and RegKeyExists(HKEY_CURRENT_USER, UninstallKey);
  Result := EnsureParrotClosed(WizardSilent);
end;

function InitializeUninstall(): Boolean;
begin
  Result := EnsureParrotClosed(UninstallSilent);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Unticking the task on a manual reinstall should switch autostart off, not leave it be.
  if (CurStep = ssPostInstall) and not WizardSilent and not WizardIsTaskSelected('autostart') then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Parrot');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // The app may have written this itself, so it is removed whether or not the task was used.
  RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Parrot');

  // User data lives apart from the program and survives unless the user asks otherwise.
  if not UninstallSilent and DirExists(ExpandConstant('{userappdata}\Parrot')) then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\Parrot'), True, True, True);
end;
