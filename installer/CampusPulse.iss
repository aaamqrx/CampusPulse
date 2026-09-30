#ifndef AppVersion
  #define AppVersion "0.1.0-preview.1"
#endif
#define AppName "CampusPulse"

[Setup]
AppId={{DB15D4E6-9CD2-47E0-A4EF-1529703B831A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CampusPulse contributors
DefaultDirName={autopf}\CampusPulse
DefaultGroupName=CampusPulse
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=CampusPulse-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\App\CampusPulse.App.exe
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\CampusPulse"; Filename: "{app}\App\CampusPulse.App.exe"; WorkingDir: "{app}\App"
Name: "{autodesktop}\CampusPulse"; Filename: "{app}\App\CampusPulse.App.exe"; WorkingDir: "{app}\App"

[UninstallDelete]
; Product-owned files only. Keep unrelated files that a user may have placed here.
Type: files; Name: "{commonappdata}\CampusPulse\settings.json"
Type: files; Name: "{commonappdata}\CampusPulse\credentials.dat"
Type: files; Name: "{commonappdata}\CampusPulse\events.json"
Type: dirifempty; Name: "{commonappdata}\CampusPulse"

[Code]
const
  ServiceName = 'CampusPulse';
  ServiceRegistryKey = 'SYSTEM\CurrentControlSet\Services\CampusPulse';
  SERVICE_STOPPED = 1;
  SERVICE_RUNNING = 4;
  SERVICE_CONTROL_STOP = 1;
  SC_MANAGER_CONNECT = $0001;
  SERVICE_QUERY_STATUS = $0004;
  SERVICE_STOP = $0020;

type
  TServiceStatus = record
    dwServiceType: Cardinal;
    dwCurrentState: Cardinal;
    dwControlsAccepted: Cardinal;
    dwWin32ExitCode: Cardinal;
    dwServiceSpecificExitCode: Cardinal;
    dwCheckPoint: Cardinal;
    dwWaitHint: Cardinal;
  end;

function OpenSCManager(lpMachineName, lpDatabaseName: string; dwDesiredAccess: Cardinal): THandle;
  external 'OpenSCManagerW@advapi32.dll stdcall';
function OpenService(hSCManager: THandle; lpServiceName: string; dwDesiredAccess: Cardinal): THandle;
  external 'OpenServiceW@advapi32.dll stdcall';
function QueryServiceStatus(hService: THandle; var lpServiceStatus: TServiceStatus): Boolean;
  external 'QueryServiceStatus@advapi32.dll stdcall';
function ControlService(hService: THandle; dwControl: Cardinal; var lpServiceStatus: TServiceStatus): Boolean;
  external 'ControlService@advapi32.dll stdcall';
function CloseServiceHandle(hSCObject: THandle): Boolean;
  external 'CloseServiceHandle@advapi32.dll stdcall';

var
  ExistingService: Boolean;
  StartServiceAfterInstall: Boolean;

function ReadServiceState(var State: Cardinal): Boolean;
var
  Manager, Service: THandle;
  Status: TServiceStatus;
begin
  Result := False;
  Manager := OpenSCManager('', 'ServicesActive', SC_MANAGER_CONNECT);
  if Manager = 0 then Exit;
  try
    Service := OpenService(Manager, ServiceName, SERVICE_QUERY_STATUS);
    if Service = 0 then Exit;
    try
      Result := QueryServiceStatus(Service, Status);
      if Result then State := Status.dwCurrentState;
    finally
      CloseServiceHandle(Service);
    end;
  finally
    CloseServiceHandle(Manager);
  end;
end;

function StopProductService(): Boolean;
var
  Manager, Service: THandle;
  Status: TServiceStatus;
  Attempt: Integer;
  StopRequested: Boolean;
begin
  Result := not RegKeyExists(HKLM, ServiceRegistryKey);
  if Result then Exit;
  Manager := OpenSCManager('', 'ServicesActive', SC_MANAGER_CONNECT);
  if Manager = 0 then Exit;
  try
    Service := OpenService(Manager, ServiceName, SERVICE_QUERY_STATUS or SERVICE_STOP);
    if Service = 0 then Exit;
    try
      StopRequested := False;
      for Attempt := 0 to 119 do
      begin
        if not QueryServiceStatus(Service, Status) then Exit;
        if Status.dwCurrentState = SERVICE_STOPPED then
        begin
          Result := True;
          Exit;
        end;
        if (Status.dwCurrentState = SERVICE_RUNNING) and (not StopRequested) then
          StopRequested := ControlService(Service, SERVICE_CONTROL_STOP, Status);
        Sleep(250);
      end;
    finally
      CloseServiceHandle(Service);
    end;
  finally
    CloseServiceHandle(Manager);
  end;
end;

procedure RunRequired(const Executable, Parameters, Description: string);
var
  ExitCode: Integer;
begin
  if not Exec(Executable, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException(Description + ': unable to start the command.');
  if ExitCode <> 0 then
    RaiseException(Description + ': exit code ' + IntToStr(ExitCode) + '.');
end;

procedure RunServiceCommand(const Parameters, Description: string);
begin
  RunRequired(ExpandConstant('{sys}\sc.exe'), Parameters, Description);
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  State: Cardinal;
begin
  Result := '';
  ExistingService := RegKeyExists(HKLM, ServiceRegistryKey);
  StartServiceAfterInstall := not ExistingService;
  if ExistingService then
  begin
    if not ReadServiceState(State) then
    begin
      Result := 'Cannot read the existing CampusPulse service state. No files were replaced.';
      Exit;
    end;
    StartServiceAfterInstall := State <> SERVICE_STOPPED;
    if not StopProductService() then
      Result := 'CampusPulse did not stop within 30 seconds. Close its settings window and retry. No files were replaced.';
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  BinaryArgument: string;
begin
  if CurStep <> ssPostInstall then Exit;
  RunRequired(ExpandConstant('{app}\Service\CampusPulse.Service.exe'), '--initialize-store', 'Initialize protected CampusPulse storage');
  BinaryArgument := 'binPath= "\"' + ExpandConstant('{app}\Service\CampusPulse.Service.exe') + '\""';
  if ExistingService then
  begin
    { Keep the existing startup type: the user may have disabled startup in settings. }
    RunServiceCommand('config ' + ServiceName + ' ' + BinaryArgument, 'Update CampusPulse service path');
  end
  else
  begin
    RunServiceCommand('create ' + ServiceName + ' ' + BinaryArgument + ' start= delayed-auto obj= LocalSystem DisplayName= "CampusPulse"', 'Create CampusPulse service');
  end;
  RunServiceCommand('description ' + ServiceName + ' "Campus network connectivity and authentication service"', 'Set service description');
  { SCM repeats the final action. The empty fourth action explicitly means NONE. }
  RunServiceCommand('failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/15000/restart/60000//0', 'Set finite service recovery');
  RunServiceCommand('failureflag ' + ServiceName + ' 1', 'Set service recovery policy');
  if StartServiceAfterInstall then
    RunServiceCommand('start ' + ServiceName, 'Start CampusPulse service');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;
  if RegKeyExists(HKLM, ServiceRegistryKey) then
  begin
    if not StopProductService() then
      RaiseException('CampusPulse did not stop within 30 seconds. Uninstall was stopped before removing program files.');
    RunServiceCommand('delete ' + ServiceName, 'Remove CampusPulse service');
  end;
  { No tray task is created by this release; remove a product task left by earlier previews. }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "CampusPulse.Tray" /F', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;
