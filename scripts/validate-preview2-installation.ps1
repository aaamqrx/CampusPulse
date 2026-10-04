[CmdletBinding()]
param([ValidateSet('Lifecycle','FinalPackage','Restore')][string]$Phase='Lifecycle',
    [switch]$Execute,[switch]$Elevate)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$version='0.1.0-preview.2'
$artifacts=Join-Path $repo ('artifacts\'+$version)
$installer=Join-Path $artifacts ('installer\CampusPulse-Setup-'+$version+'.exe')
$oldRoot=Join-Path $env:ProgramFiles 'CampusPulse'
$newRoot='E:\Apps\CampusPulse'
$newParent='E:\Apps'
$oldExe=Join-Path $oldRoot 'Service\CampusPulse.Service.exe'
$newExe=Join-Path $newRoot 'Service\CampusPulse.Service.exe'
$data=Join-Path $env:ProgramData 'CampusPulse'
$backup=Join-Path $env:ProgramData 'CampusPulse-ReleaseBackup-20261004'
$evidence=Join-Path $repo '.local\release-20261004'
$regKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DB15D4E6-9CD2-47E0-A4EF-1529703B831A}_is1'
$resultPath=Join-Path $evidence ('installer-'+$Phase.ToLowerInvariant()+'.json')
if(-not $Execute){
    Write-Output 'Plan only. Fixed preview.2 package and E:\Apps\CampusPulse target.'
    Write-Output 'Lifecycle: protected original-data/program backup; pause authentication; upgrade C; uninstall and restore paused data from private backup; install/reinstall/uninstall/reinstall E; restore original settings; inspect owned native E window.'
    Write-Output 'FinalPackage: require current clean v0.1.0-preview.2 tag; reinstall exact tagged package at E and verify payload/settings/native UI.'
    Write-Output 'No password export, network disconnect, power-plan change, reboot or authentication command. Existing user windows must be exited first.'
    exit 0
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
    if(-not $Elevate){throw 'Administrator required; use -Elevate for local UAC'}
    $child=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Phase',$Phase,'-Execute')
    if(-not $child.WaitForExit(45000)){Write-Output 'Installation validation pending; read the timestamped result before retrying.';exit 2}
    if(Test-Path -LiteralPath $resultPath){$r=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json;
        [ordered]@{Phase=$r.Phase;Time=$r.Time;Checks=$r.Checks.Count;Passed=@($r.Checks|Where-Object{$_.Passed}).Count;
            Failure=$r.Failure;OriginalProfileRestored=$r.OriginalProfileRestored;Finished=$r.Finished}|ConvertTo-Json}
    exit $child.ExitCode
}
$report=[ordered]@{Time=[DateTimeOffset]::Now.ToString('o');ClientDate='2026-10-04';Phase=$Phase;
    Checks=@();Failure=$null;OriginalProfileRestored=$false;BackupRetained=$false;Finished=$null}
$paused=$false;$mutation=$false;$installerPending=$false;$owned=$null;$originalSettings=$null
function Check([bool]$ok,[string]$name){
    $report.Checks += [ordered]@{Name=$name;Passed=$ok}
    if(-not $ok){throw [InvalidOperationException]::new($name)}
}
function Safe([string]$path,[string]$root){
    $full=[IO.Path]::GetFullPath($path).TrimEnd('\');$base=[IO.Path]::GetFullPath($root).TrimEnd('\')
    if(-not($full.Equals($base,[StringComparison]::OrdinalIgnoreCase)-or$full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase))){throw 'Path outside fixed scope'}
    $current=$full
    while($current){if((Test-Path -LiteralPath $current)-and((Get-Item -LiteralPath $current -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Linked target refused'};$current=Split-Path -Parent $current}
    return $full
}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Service([string]$expected){
    $s=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    Check ($null-ne$s-and$s.PathName.Trim('"').Equals($expected,[StringComparison]::OrdinalIgnoreCase)-and$s.StartName-eq'LocalSystem') 'Exact owned service and identity'
    return $s
}
function Control($request){
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','CampusPulse.Control.v1',[IO.Pipes.PipeDirection]::InOut)
    try{$pipe.Connect(5000);$bytes=[Text.Encoding]::UTF8.GetBytes(($request|ConvertTo-Json -Depth 5 -Compress)+"`n");$pipe.Write($bytes,0,$bytes.Length);$pipe.Flush();
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8);$pending=$reader.ReadLineAsync();if(-not$pending.Wait(8000)){throw 'Control timeout'};
        $reply=$pending.Result|ConvertFrom-Json;if(-not$reply.Success){throw 'Control refused'};return $reply.Snapshot
    }finally{$pipe.Dispose()}
}
function Snapshot($s){
    return [ordered]@{State=$s.State;LastCheck=$s.LastCheck;LastSuccess=$s.LastSuccess;NextCheck=$s.NextCheck;
        Enabled=$s.Settings.Enabled;StartWithWindows=$s.Settings.StartWithWindows;UnattendedMode=$s.Settings.UnattendedMode;
        KeepingAwake=$s.KeepingAwake;HasPassword=$s.HasPassword;Diagnostics=$s.Diagnostics;
        Events=@($s.RecentEvents|Where-Object{$_.Kind-in@('authentication_submitted','authentication_result','network_result')}|
            ForEach-Object{[ordered]@{Time=$_.Time;Kind=$_.Kind;Source=$_.Source;ReasonCode=$_.ReasonCode}})}
}
function Stop-Owned([string]$exe){$s=Service $exe;if($s.State-ne'Stopped'){Stop-Service CampusPulse;(Get-Service CampusPulse).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}}
function Start-Owned([string]$exe){$null=Service $exe;Start-Service CampusPulse;(Get-Service CampusPulse).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))}
function Secure-Folder([string]$path,[bool]$readUsers){
    New-Item -ItemType Directory -Path $path -Force|Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $ids=@('S-1-5-18','S-1-5-32-544');if($readUsers){$ids+='S-1-5-32-545'}
    foreach($id in $ids){$rights=if($id-eq'S-1-5-32-545'){[Security.AccessControl.FileSystemRights]::ReadAndExecute}else{[Security.AccessControl.FileSystemRights]::FullControl};
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($id),$rights,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))}
    Set-Acl -LiteralPath $path -AclObject $acl
    Assert-Acl $path $readUsers
}
function Assert-Acl([string]$path,[bool]$readUsers){
    $acl=Get-Acl -LiteralPath $path;$rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]));$ids=@('S-1-5-18','S-1-5-32-544');if($readUsers){$ids+='S-1-5-32-545'}
    $admin=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    Check ($acl.AreAccessRulesProtected-and$acl.GetOwner([Security.Principal.SecurityIdentifier]).Equals($admin)-and$rules.Count-eq$ids.Count-and
        @($rules|Where-Object{$_.IdentityReference.Value-notin$ids}).Count-eq0) ('Protected ACL and owner: '+[IO.Path]::GetFileName($path))
    if($readUsers){$users=@($rules|Where-Object{$_.IdentityReference.Value-eq'S-1-5-32-545'});
        $write=[Security.AccessControl.FileSystemRights]'Write, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership';
        Check ($users.Count-eq1-and($users[0].FileSystemRights-band$write)-eq0) ('Users cannot write/delete programs: '+[IO.Path]::GetFileName($path))}
}
function Backup-Original {
    Check (-not(Test-Path -LiteralPath $backup)) 'No prior backup overwritten'
    Secure-Folder $backup $false
    Set-Content -LiteralPath (Join-Path $backup '.preview2-backup') -Value 'CampusPulse preview2 original C installation 20261004 v1' -Encoding ASCII
    New-Item -ItemType Directory -Path (Join-Path $backup 'Data')|Out-Null
    foreach($name in @('settings.json','credentials.dat','events.json')){
        $source=Safe (Join-Path $data $name) $data
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $backup ('Data\'+$name))}
    }
}
function Copy-Programs([string]$from,[string]$to){
    foreach($file in Get-ChildItem -LiteralPath $from -Recurse -File -Force){
        $null=Safe $file.FullName $from;$relative=$file.FullName.Substring($from.Length+1);$dest=Safe (Join-Path $to $relative) $to;
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        Check ((Hash $file.FullName)-eq(Hash $dest)) ('Program backup: '+$relative)
    }
}
function Run-Setup([string]$root,[string]$logName){
    $null=Safe $root $root;$log=Join-Path $evidence $logName
    $process=Start-Process -FilePath $installer -WindowStyle Hidden -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$root+'"'),('/LOG="'+$log+'"'))
    if(-not$process.WaitForExit(120000)){$script:installerPending=$true;throw 'Installer still pending; do not race recovery'}
    Check ($process.ExitCode-eq0) ('Installer completed: '+$logName)
}
function Run-Uninstall([string]$root,[string]$logName){
    $null=Service (Join-Path $root 'Service\CampusPulse.Service.exe')
    $registration=Get-ItemProperty -LiteralPath $regKey
    Check ($registration.InstallLocation.TrimEnd('\')-eq$root-and$registration.UninstallString.Trim('"')-eq(Join-Path $root 'unins000.exe')) 'Exact product uninstall registration'
    $process=Start-Process -FilePath (Join-Path $root 'unins000.exe') -WindowStyle Hidden -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $evidence $logName)+'"'))
    if(-not$process.WaitForExit(90000)){$script:installerPending=$true;throw 'Uninstall still pending'}
    Check ($process.ExitCode-eq0) ('Uninstaller completed: '+$logName)
    # Inno's launcher can exit before its temporary child deletes unins000.exe.
    Start-Sleep -Seconds 3
    Check ($null-eq(Get-CimInstance Win32_Service -Filter "Name='CampusPulse'")-and-not(Test-Path -LiteralPath $regKey)) 'Uninstall removed owned service and registration'
    # Backend-only development updates left two PDB files that the installer
    # never owned. Remove only those exact files after private-backup hash checks.
    foreach($relative in @('Service\CampusPulse.Core.pdb','Service\CampusPulse.Service.pdb')){
        $leftover=Safe (Join-Path $root $relative) $root
        if(Test-Path -LiteralPath $leftover){
            Check ((Hash $leftover)-eq(Hash (Join-Path $backup ('Programs\'+$relative)))) 'Known development debug file matches protected backup'
            Remove-Item -LiteralPath $leftover -Force
        }
    }
    foreach($folder in @((Join-Path $root 'Service'),$root)){
        $null=Safe $folder $root
        if((Test-Path -LiteralPath $folder)-and@(Get-ChildItem -LiteralPath $folder -Force).Count-eq0){Remove-Item -LiteralPath $folder -Force}
    }
    Check (-not(Test-Path -LiteralPath $root)) 'Uninstall removed product programs; no unrelated files removed'
    Check (-not(Test-Path -LiteralPath (Join-Path $data 'credentials.dat'))) 'Normal uninstall removed product credential data'
    Restore-PausedData
}
function Restore-PausedData {
    Secure-Folder $data $false
    foreach($name in @('credentials.dat','events.json')){Copy-Item -LiteralPath (Join-Path $backup ('Data\'+$name)) -Destination (Join-Path $data $name) -Force}
    $profile=$originalSettings|Select-Object *;$profile.Enabled=$false
    $profile|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding UTF8
    Check ((Hash (Join-Path $data 'credentials.dat'))-eq(Hash (Join-Path $backup 'Data\credentials.dat'))) 'Protected credential restored before new service can start'
}
function Assert-Profile([bool]$enabled){
    $s=Control @{Command='status'}
    Check ($s.HasPassword-and$s.Settings.Enabled-eq$enabled) 'Expected authentication mode and saved credential'
    foreach($key in @('StartWithWindows','UnattendedMode','Username','Carrier','PortalUrl','OnlineCheckSeconds')){
        Check ($s.Settings.$key-eq$originalSettings.$key) ('Original profile retained: '+$key)
    }
    Check ((Hash (Join-Path $data 'credentials.dat'))-eq(Hash (Join-Path $backup 'Data\credentials.dat'))) 'Original encrypted credential bytes retained'
    return $s
}
function Assert-Payload([string]$root){
    foreach($component in @('App','Service')){
        $published=Join-Path $artifacts ('publish\'+$component)
        foreach($file in Get-ChildItem -LiteralPath $published -Recurse -File){$relative=$file.FullName.Substring($published.Length+1);
            $installed=Safe (Join-Path $root ($component+'\'+$relative)) $root
            Check ((Hash $file.FullName)-eq(Hash $installed)) ('Installed payload: '+$component+'/'+$relative)
        }
    }
    Assert-Acl $root $true
    foreach($name in @('App','Service','Service\CampusPulse.Service.exe','App\CampusPulse.App.exe')){Assert-Acl (Join-Path $root $name) $true}
    Check ((Get-Item -LiteralPath (Join-Path $root 'Service\CampusPulse.Service.exe')).VersionInfo.ProductVersion.StartsWith($version)) 'Installed preview.2 version'
}
function Restore-Profile([string]$exe){
    Stop-Owned $exe
    Copy-Item -LiteralPath (Join-Path $backup 'Data\settings.json') -Destination (Join-Path $data 'settings.json') -Force
    Check ((Hash (Join-Path $data 'settings.json'))-eq(Hash (Join-Path $backup 'Data\settings.json'))) 'Original settings bytes restored before start'
    $mode=if($originalSettings.StartWithWindows){'delayed-auto'}else{'demand'}
    & sc.exe config CampusPulse start= $mode|Out-Null;if($LASTEXITCODE){throw 'Startup restoration failed'}
    Start-Owned $exe
    $s=$null
    for($i=0;$i-lt24;$i++){Start-Sleep -Milliseconds 500;$s=Control @{Command='status'};if($s.State-eq5){break}}
    $null=Assert-Profile ([bool]$originalSettings.Enabled)
    $current=Service $exe
    $expectedMode=if($originalSettings.StartWithWindows){'Auto'}else{'Manual'}
    Check ($current.StartMode-eq$expectedMode-and$s.ActualStartWithWindows-eq[bool]$originalSettings.StartWithWindows) 'Original startup behavior restored'
    Check ($s.State-eq5) 'Public internet verified after profile restoration'
    Check (-not$originalSettings.UnattendedMode-or$s.KeepingAwake) 'Original unattended behavior restored'
    $report.After=Snapshot $s;$report.OriginalProfileRestored=$true
}
function Native-Window([string]$root){
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
    $script:owned=Start-Process -FilePath (Join-Path $root 'App\CampusPulse.App.exe') -WindowStyle Hidden -PassThru
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id);$window=$null
    for($i=0;$i-lt24-and$null-eq$window;$i++){Start-Sleep -Milliseconds 500;$window=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)}
    Check ($null-ne$window) 'Exact installed E native window opened'
    Start-Sleep -Seconds 2
    foreach($id in @('VersionText','StateText','PasswordInput','EnabledBox','StartWithWindowsBox','UnattendedBox')){
        $find=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id);$element=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,$find)
        Check ($null-ne$element) ('Native control present: '+$id)
        if($id-eq'VersionText'){Check ($element.Current.Name.Contains($version)) 'Installed native UI version'}
        if($id-eq'PasswordInput'){Check $element.Current.IsPassword 'Installed password input masked'}
        if($id.EndsWith('Box')){$toggle=$element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern);$key=switch($id){'EnabledBox'{'Enabled'}'StartWithWindowsBox'{'StartWithWindows'}'UnattendedBox'{'UnattendedMode'}};
            Check (($toggle.Current.ToggleState.ToString()-eq'On')-eq[bool]$originalSettings.$key) ('Native restored switch: '+$key)}
    }
    Check $owned.Responding 'Installed native window responsive'
    Stop-Process -Id $owned.Id;$script:owned=$null
}
function Restore-Original {
    if($installerPending){throw 'Installer pending; protected backup retained'}
    $null=Safe $backup $backup
    Check ((Get-Content -LiteralPath (Join-Path $backup '.preview2-backup') -Raw).Trim()-eq'CampusPulse preview2 original C installation 20261004 v1') 'Fixed complete backup marker'
    Check (Test-Path -LiteralPath (Join-Path $backup 'complete.json')) 'Original program backup complete'
    $originalSettings=Get-Content -LiteralPath (Join-Path $backup 'Data\settings.json') -Raw|ConvertFrom-Json
    $s=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if($null-ne$s){$path=$s.PathName.Trim('"');Check ($path-in@($oldExe,$newExe)) 'Rollback only fixed owned service';Stop-Owned $path;
        & sc.exe delete CampusPulse|Out-Null;if($LASTEXITCODE){throw 'Service rollback removal failed'}}
    # Installer registration is restored by the untouched published preview.1 installer.
    $legacy=Join-Path $repo 'artifacts\installer\CampusPulse-Setup-0.1.0-preview.1.exe'
    $pauseProfile=$originalSettings|Select-Object *;$pauseProfile.Enabled=$false
    # A normal uninstall removes product data. Recreate its protected directory
    # from the private backup before an installer can start the service.
    Secure-Folder $data $false
    foreach($name in @('credentials.dat','events.json')){Copy-Item -LiteralPath (Join-Path $backup ('Data\'+$name)) -Destination (Join-Path $data $name) -Force}
    $pauseProfile|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding UTF8
    $savedInstaller=$script:installer;$script:installer=$legacy
    try{Run-Setup $oldRoot 'rollback-original-installer.log'}finally{$script:installer=$savedInstaller}
    Stop-Owned $oldExe
    Copy-Programs (Join-Path $backup 'Programs') $oldRoot
    foreach($name in @('settings.json','credentials.dat','events.json')){Copy-Item -LiteralPath (Join-Path $backup ('Data\'+$name)) -Destination (Join-Path $data $name) -Force}
    Restore-Profile $oldExe
    $report.RollbackCompleted=$true
}
try{
    foreach($path in @($oldRoot,$newRoot,$newParent,$data,$backup,$installer)){$null=Safe $path $path}
    Check (@(Get-Process -Name CampusPulse.App -ErrorAction SilentlyContinue).Count-eq0) 'User interface exited; no file lock to force-close'
    if($Phase-eq'Restore'){Restore-Original}
    else{
        $manifest=Get-Content -LiteralPath (Join-Path $artifacts 'build-manifest.json') -Raw|ConvertFrom-Json
        Check ($manifest.version-eq$version-and$manifest.offlineTestsPassed-and$manifest.installerBuilt-and(Hash $installer).ToLowerInvariant()-eq$manifest.sha256) 'Exact candidate and build manifest'
        $report.InstallerSha256=$manifest.sha256;$report.SourceCommit=$manifest.sourceCommit
        if($Phase-eq'FinalPackage'){
            $head=(& git -C $repo -c "safe.directory=$repo" rev-parse HEAD|Out-String).Trim()
            $tag=(& git -C $repo -c "safe.directory=$repo" rev-list -n 1 v0.1.0-preview.2|Out-String).Trim()
            Check ($LASTEXITCODE-eq0-and-not$manifest.sourceDirty-and$manifest.sourceTags-contains'v0.1.0-preview.2'-and$head-eq$tag-and$tag-eq$manifest.sourceCommit) 'Exact clean current release tag'
            $null=Service $newExe
            Check (Test-Path -LiteralPath (Join-Path $backup 'complete.json')) 'Protected original profile backup available'
            $originalSettings=Get-Content -LiteralPath (Join-Path $backup 'Data\settings.json') -Raw|ConvertFrom-Json
            $before=Control @{Command='status'};$report.Before=Snapshot $before
            $paused=$true;$null=Control @{Command='pause'};$mutation=$true
            Run-Setup $newRoot 'install-final-tagged-package.log'
            $null=Assert-Profile $false;Assert-Payload $newRoot
            Restore-Profile $newExe;Native-Window $newRoot
        }else{
            $null=Service $oldExe;$before=Control @{Command='status'};$report.Before=Snapshot $before
            Check ($before.State-eq5-and$before.HasPassword) 'Original online profile available before lifecycle'
            Check (-not(Test-Path -LiteralPath $newRoot)) 'E target absent; no unrelated directory overwritten'
            Check (-not(Test-Path -LiteralPath $newParent)) 'E Apps parent absent; no shared parent permissions changed'
            $originalSettings=$before.Settings
            $reuseBackup=Test-Path -LiteralPath $backup
            if($reuseBackup){
                Check ((Get-Content -LiteralPath (Join-Path $backup '.preview2-backup') -Raw).Trim()-eq'CampusPulse preview2 original C installation 20261004 v1'-and(Test-Path -LiteralPath (Join-Path $backup 'complete.json'))) 'Reuse only fixed completed original backup'
                foreach($name in @('settings.json','credentials.dat')){Check ((Hash (Join-Path $data $name))-eq(Hash (Join-Path $backup ('Data\'+$name)))) 'Retry original data matches completed protected backup'}
                $report.ReusedOriginalBackup=$true
            }else{Backup-Original}
            $paused=$true;$null=Control @{Command='pause'}
            Stop-Owned $oldExe
            if(-not$reuseBackup){
                Copy-Programs $oldRoot (Join-Path $backup 'Programs')
                [ordered]@{Complete=$true;OriginalRoot=$oldRoot;CreatedAt=[DateTimeOffset]::Now.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $backup 'complete.json') -Encoding UTF8
            }
            $mutation=$true
            Run-Setup $oldRoot 'upgrade-existing-c.log'
            # The original was running. A stopped-service install deliberately remains stopped.
            Start-Owned $oldExe
            $null=Assert-Profile $false;Assert-Payload $oldRoot
            Run-Uninstall $oldRoot 'uninstall-c-before-migration.log'
            Secure-Folder $newParent $true
            Run-Setup $newRoot 'install-e.log'
            $null=Assert-Profile $false;Assert-Payload $newRoot
            Run-Setup $newRoot 'reinstall-e.log';$null=Assert-Profile $false
            Run-Uninstall $newRoot 'uninstall-e.log'
            Run-Setup $newRoot 'restore-e-installation.log';$null=Assert-Profile $false;Assert-Payload $newRoot
            Restore-Profile $newExe;Native-Window $newRoot
            $registration=Get-ItemProperty -LiteralPath $regKey
            Check ($registration.InstallLocation.TrimEnd('\')-eq$newRoot-and$registration.DisplayVersion-eq$version) 'E installation registered at preview.2'
            $null=Service $newExe;Check (-not(Test-Path -LiteralPath $oldRoot)) 'Old C installation removed'
        }
    }
}catch{
    $report.Failure=[ordered]@{Type=$_.Exception.GetType().FullName;Line=$_.InvocationInfo.ScriptLineNumber}
    if($mutation){try{Restore-Original}catch{$report.RollbackFailure=[ordered]@{Type=$_.Exception.GetType().FullName;Line=$_.InvocationInfo.ScriptLineNumber}}}
    elseif($paused-and$null-ne$originalSettings){try{Restore-Profile $oldExe}catch{$report.ProfileRestoreFailure=$_.Exception.GetType().FullName}}
}finally{
    if($null-ne$owned-and-not$owned.HasExited){Stop-Process -Id $owned.Id -ErrorAction SilentlyContinue}
    $report.BackupRetained=Test-Path -LiteralPath $backup;$report.Finished=[DateTimeOffset]::Now.ToString('o')
    $report.ProcessExitCode=if($null-ne$report.Failure){1}else{0}
    $report|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if($null-ne$report.Failure){exit 1}
exit 0
