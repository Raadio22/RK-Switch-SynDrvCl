#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Installer.ps1
#endif

#define AppName "RK-Switch SynDrvCl"
#define AppExeName "RK-Switch-SynDrvCl.exe"
#define AppPublisher "Raadio22"
#define AppUrl "https://github.com/Raadio22/RK-Switch-SynDrvCl"
#define AppId "{{E01208A7-1E91-45E9-AB50-D0A2E1305A73}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalace aplikace {#AppName}
VersionInfoProductName={#AppName}
DefaultDirName={localappdata}\Programs\RK-Switch-SynDrvCl
DefaultGroupName=RK-Switch SynDrvCl
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\Assets\rk-switch.ico
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=RK-Switch-SynDrvCl-{#AppVersion}-Setup-win-x64
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no
AppMutex=RKSwitchSynDrvCl.App
SetupLogging=yes

[Languages]
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Vytvořit zástupce na &ploše"; GroupDescription: "Další možnosti:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\RK-Switch SynDrvCl"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\RK-Switch SynDrvCl"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Spustit RK-Switch SynDrvCl"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = 'RK-Switch SynDrvCl';
  PreferencesKey = 'Software\RK-Switch-SynDrvCl';
  CredentialTarget = 'RK-Switch-SynDrvCl/Synology-DSM';
  CredTypeGeneric = 1;
  ErrorNotFound = 1168;

var
  CleanupWarning: String;

function CredDelete(TargetName: String; CredentialType: DWORD; Flags: DWORD): Boolean;
  external 'CredDeleteW@advapi32.dll stdcall uninstallonly';

procedure AddCleanupWarning(MessageText: String);
begin
  if CleanupWarning <> '' then
    CleanupWarning := CleanupWarning + #13#10;
  CleanupWarning := CleanupWarning + MessageText;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingRunValue: String;
  InstalledCommand: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  if RegQueryStringValue(HKCU, RunKey, RunValueName, ExistingRunValue) then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\{#AppExeName}') + '" --startup';
    if not RegWriteStringValue(HKCU, RunKey, RunValueName, InstalledCommand) then
      MsgBox('Automatické spuštění se nepodařilo přesměrovat na instalovanou aplikaci. Lze je znovu zapnout přímo v RK-Switch.', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CredentialError: LongInt;
  LogDirectory: String;
  DataDirectory: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegValueExists(HKCU, RunKey, RunValueName) and
       (not RegDeleteValue(HKCU, RunKey, RunValueName)) then
      AddCleanupWarning('Nepodařilo se odstranit automatické spuštění RK-Switch.');

    if RegKeyExists(HKCU, PreferencesKey) and
       (not RegDeleteKeyIncludingSubkeys(HKCU, PreferencesKey)) then
      AddCleanupWarning('Nepodařilo se odstranit uživatelské předvolby RK-Switch.');

    if not CredDelete(CredentialTarget, CredTypeGeneric, 0) then
    begin
      CredentialError := DLLGetLastError;
      if CredentialError <> ErrorNotFound then
        AddCleanupWarning('Uložené heslo RK-Switch se nepodařilo odstranit: ' + SysErrorMessage(CredentialError));
    end;

    DataDirectory := ExpandConstant('{localappdata}\RK-Switch-SynDrvCl');
    LogDirectory := AddBackslash(DataDirectory) + 'logs';
    if DirExists(LogDirectory) and (not DelTree(LogDirectory, True, True, True)) then
      AddCleanupWarning('Nepodařilo se odstranit diagnostický log RK-Switch.');
    RemoveDir(DataDirectory);
  end;

  if (CurUninstallStep = usPostUninstall) and (CleanupWarning <> '') then
    MsgBox(CleanupWarning + #13#10 + 'Zbývající položky můžete odstranit ručně; heslo je ve Správci přihlašovacích údajů Windows.', mbError, MB_OK);
end;
