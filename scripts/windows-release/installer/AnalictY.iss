#define AppName "AnalictY"
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef ReleaseRoot
#define ReleaseRoot "..\..\release\AnalictY-0.1.0"
#endif

[Setup]
AppId={{A1E7A186-5B67-4B8A-8F22-87F3C21D0301}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Eddy A R Neves
DefaultDirName={autopf}\AnalictY
DefaultGroupName=AnalictY
DisableProgramGroupPage=yes
OutputBaseFilename=AnalictY-Setup-{#AppVersion}
SetupIconFile=assets\analicty-favicon.ico
WizardImageFile=assets\analicty-wizard-large.bmp
WizardSmallImageFile=assets\analicty-wizard-small.bmp
VersionInfoCompany=Eddy A R Neves
VersionInfoDescription=Instalador do AnalictY
VersionInfoProductName=AnalictY
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) Eddy A R Neves
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked
Name: "openapp"; Description: "Abrir AnalictY ao concluir"; GroupDescription: "Finalizacao:"; Flags: unchecked

[Files]
Source: "{#ReleaseRoot}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseRoot}\runtime\*"; DestDir: "{app}\runtime"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseRoot}\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseRoot}\updater\*"; DestDir: "{app}\updater"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseRoot}\installer\*"; DestDir: "{app}\installer"; Flags: ignoreversion recursesubdirs createallsubdirs
[Dirs]
Name: "{app}\data"; Permissions: users-modify
Name: "{app}\data\backups"; Permissions: users-modify
Name: "{app}\data\certs"; Permissions: users-modify
Name: "{app}\data\mysql"; Permissions: users-modify
Name: "{app}\data\secrets"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify
Name: "{app}\logs\backend"; Permissions: users-modify
Name: "{app}\logs\frontend"; Permissions: users-modify
Name: "{app}\logs\mysql"; Permissions: users-modify
Name: "{app}\logs\updater"; Permissions: users-modify

[Icons]
Name: "{group}\AnalictY"; Filename: "https://analicty"; IconFilename: "{app}\installer\assets\analicty.ico"
Name: "{group}\AnalictY Agent"; Filename: "{app}\app\agent\AnalictY.Agent.exe"; IconFilename: "{app}\installer\assets\analicty.ico"
Name: "{autodesktop}\AnalictY"; Filename: "https://analicty"; IconFilename: "{app}\installer\assets\analicty.ico"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\service\install-services.ps1"" -InstallRoot ""{app}"" -AdminPassword ""{code:GetAdminPassword}"""; Flags: runhidden waituntilterminated
Filename: "{app}\app\agent\AnalictY.Agent.exe"; Flags: nowait runasoriginaluser skipifsilent
Filename: "https://analicty"; Description: "Abrir AnalictY"; Flags: shellexec postinstall skipifsilent; Tasks: openapp

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\service\uninstall-services.ps1"" -InstallRoot ""{app}"""; Flags: runhidden waituntilterminated


[Code]
var
  AdminPasswordPage: TInputQueryWizardPage;
  IsUpgradeInstall: Boolean;

function IsExistingAnalictYInstall(InstallRoot: String): Boolean;
begin
  Result :=
    FileExists(InstallRoot + '\data\scada.db') or
    FileExists(InstallRoot + '\data\jwt.key') or
    DirExists(InstallRoot + '\data\mysql');
end;

procedure BackupExistingInstall;
var
  ResultCode: Integer;
  InstallRoot: String;
  BackupCommand: String;
begin
  if not IsExistingAnalictYInstall(ExpandConstant('{app}')) then
  begin
    Exit;
  end;

  InstallRoot := ExpandConstant('{app}');
  BackupCommand :=
    '$root = ''' + InstallRoot + '''; ' +
    '$stamp = Get-Date -Format ''yyyyMMdd-HHmmss''; ' +
    '$backup = Join-Path $root (''data\backups\installer-{#AppVersion}-'' + $stamp); ' +
    'New-Item -ItemType Directory -Force -Path $backup | Out-Null; ' +
    'foreach ($name in @(''app'',''runtime'',''service'',''installer'')) { ' +
    '  $source = Join-Path $root $name; ' +
    '  if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $backup $name) -Recurse -Force } ' +
    '} ' +
    '$db = Join-Path $root ''data\scada.db''; ' +
    'if (Test-Path $db) { Copy-Item -LiteralPath $db -Destination (Join-Path $backup ''scada.db'') -Force }';

  Exec(
    'powershell.exe',
    '-ExecutionPolicy Bypass -NoProfile -Command "' + BackupCommand + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

procedure StopAnalictYServices;
var
  ResultCode: Integer;
  BackendServiceExe: String;
  FrontendServiceExe: String;
begin
  FrontendServiceExe := ExpandConstant('{app}\service\AnalictY.Frontend.Service.exe');
  BackendServiceExe := ExpandConstant('{app}\service\AnalictY.Backend.Service.exe');

  if FileExists(FrontendServiceExe) then
  begin
    Exec(FrontendServiceExe, 'stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  if FileExists(BackendServiceExe) then
  begin
    Exec(BackendServiceExe, 'stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  Exec(
    'powershell.exe',
    '-ExecutionPolicy Bypass -NoProfile -Command "Stop-Process -Name AnalictY.Agent -Force -ErrorAction SilentlyContinue; Stop-Service -Name AnalictYFrontend,AnalictYBackend,AnalictYMySQL -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
end;

function HasUppercase(Value: String): Boolean;
var
  Index: Integer;
  Character: String;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    Character := Copy(Value, Index, 1);
    if (Character >= 'A') and (Character <= 'Z') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function HasLowercase(Value: String): Boolean;
var
  Index: Integer;
  Character: String;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    Character := Copy(Value, Index, 1);
    if (Character >= 'a') and (Character <= 'z') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function HasDigit(Value: String): Boolean;
var
  Index: Integer;
  Character: String;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    Character := Copy(Value, Index, 1);
    if (Character >= '0') and (Character <= '9') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function HasSpecial(Value: String): Boolean;
var
  Index: Integer;
  Character: String;
begin
  Result := False;
  for Index := 1 to Length(Value) do
  begin
    Character := Copy(Value, Index, 1);
    if not (((Character >= 'A') and (Character <= 'Z')) or
      ((Character >= 'a') and (Character <= 'z')) or
      ((Character >= '0') and (Character <= '9'))) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function IsValidAdminPassword(Value: String; var ErrorMessage: String): Boolean;
begin
  Result := False;

  if Length(Value) < 10 then
  begin
    ErrorMessage := 'A senha do admin deve ter pelo menos 10 caracteres.';
    Exit;
  end;

  if Pos('"', Value) > 0 then
  begin
    ErrorMessage := 'A senha do admin nao pode conter aspas duplas.';
    Exit;
  end;

  if not HasUppercase(Value) or not HasLowercase(Value) or not HasDigit(Value) or not HasSpecial(Value) then
  begin
    ErrorMessage := 'A senha do admin deve conter maiuscula, minuscula, numero e caractere especial.';
    Exit;
  end;

  Result := True;
end;

procedure InitializeWizard;
begin
  AdminPasswordPage := CreateInputQueryPage(
    wpSelectTasks,
    'Usuario administrador',
    'Defina o primeiro acesso do AnalictY',
    'Usuario inicial: admin' + #13#10 +
    'Informe uma senha forte. Ela sera usada uma unica vez para criar o admin local.');
  AdminPasswordPage.Add('Senha do admin:', True);
  AdminPasswordPage.Add('Confirmar senha:', True);
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  IsUpgradeInstall := IsExistingAnalictYInstall(WizardDirValue);

  if IsUpgradeInstall and (PageID = AdminPasswordPage.ID) then
  begin
    Result := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopAnalictYServices;
  BackupExistingInstall;
  Result := '';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
begin
  Result := True;

  if CurPageID = AdminPasswordPage.ID then
  begin
    if AdminPasswordPage.Values[0] <> AdminPasswordPage.Values[1] then
    begin
      MsgBox('As senhas do admin nao conferem.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if not IsValidAdminPassword(AdminPasswordPage.Values[0], ErrorMessage) then
    begin
      MsgBox(ErrorMessage, mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

function GetAdminPassword(Param: String): String;
begin
  if IsUpgradeInstall then
  begin
    Result := '';
    Exit;
  end;

  Result := AdminPasswordPage.Values[0];
end;
