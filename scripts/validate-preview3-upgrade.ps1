[CmdletBinding()]
param([ValidateSet('Upgrade','FinalPackage','Restore','Cleanup')][string]$Phase='Upgrade',
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedCommit,
    [switch]$Execute,[switch]$Elevate,[switch]$ReuseVerifiedBackup)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$version='0.1.0-preview.3'
$delivery=Join-Path $repo '.local\preview3\ci\0.1.0-preview.3'
$evidence=Join-Path $repo '.local\preview3'
$package=Join-Path $delivery ('installer\CampusPulse-Setup-'+$version+'.exe')
$oldPackage=Join-Path $repo 'artifacts\0.1.0-preview.2\installer\CampusPulse-Setup-0.1.0-preview.2.exe'
$oldPackageHash='4480aaffd22ff601d4a089e4b9a45ae6ac7cc7fae01b743ff77e3916cc224f28'
$data=Join-Path $env:ProgramData 'CampusPulse'
$backup=Join-Path $env:ProgramData 'CampusPulse-Preview3Backup-20261010'
$registration='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DB15D4E6-9CD2-47E0-A4EF-1529703B831A}_is1'
$resultPath=Join-Path $evidence ('upgrade-'+$Phase.ToLowerInvariant()+'.json')
if(-not$Execute){Write-Output 'Plan only: verify exact CI delivery, registered product, protected backup and original profile; upgrade in place, then inspect or restore. No uninstall, reboot or reconnect command.';exit 0}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 if(-not$Elevate){throw 'Administrator required; use -Elevate for local UAC.'}
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Phase',$Phase,'-ExpectedCommit',$ExpectedCommit,'-Execute')
 if($ReuseVerifiedBackup){$arguments+='-ReuseVerifiedBackup'}
 $child=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments
 if(-not$child.WaitForExit(45000)){Write-Output 'Validation pending. Read the timestamped result after completion; do not run concurrent installation or restoration.';exit 2}
 if(Test-Path -LiteralPath $resultPath){$r=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json;[ordered]@{Phase=$r.Phase;Checks=$r.Checks.Count;Passed=@($r.Checks|Where-Object Passed).Count;Failure=$r.Failure;ProfileRestored=$r.ProfileRestored;Finished=$r.Finished}|ConvertTo-Json}
 exit $child.ExitCode
}
$report=[ordered]@{Time=[DateTimeOffset]::Now.ToString('o');Phase=$Phase;Version=$version;ExpectedCommit=$ExpectedCommit;Checks=@();Failure=$null;ProfileRestored=$false;InstallerPending=$false;Finished=$null}
$owned=$null;$mutated=$false;$installerPending=$false;$state=$null;$root=$null;$exe=$null
function Check([bool]$ok,[string]$name){$report.Checks+=@{Name=$name;Passed=$ok};if(-not$ok){throw [InvalidOperationException]::new($name)}}
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Safe([string]$path,[string]$base){
 $full=[IO.Path]::GetFullPath($path).TrimEnd('\');$limit=[IO.Path]::GetFullPath($base).TrimEnd('\')
 if(-not($full.Equals($limit,[StringComparison]::OrdinalIgnoreCase)-or$full.StartsWith($limit+'\',[StringComparison]::OrdinalIgnoreCase))){throw 'Path outside fixed scope.'}
 for($parent=$full;$parent;$parent=Split-Path -Parent $parent){if((Test-Path -LiteralPath $parent)-and((Get-Item -LiteralPath $parent -Force).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Linked path refused.'}}
 return $full
}
function Files([string]$folder){
 $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue($folder)
 while($queue.Count){$dir=$queue.Dequeue();$null=Safe $dir $folder;foreach($item in Get-ChildItem -LiteralPath $dir -Force){
  $null=Safe $item.FullName $folder
  if($item.PSIsContainer){$queue.Enqueue($item.FullName)}else{$item}
 }}
}
function Assert-Acl([string]$path,[bool]$usersRead){
 $acl=Get-Acl -LiteralPath $path;$rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
 $allowed=@('S-1-5-18','S-1-5-32-544');if($usersRead){$allowed+='S-1-5-32-545'}
 Check ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value-eq'S-1-5-32-544'-and@($rules|Where-Object{$_.IdentityReference.Value-notin$allowed}).Count-eq0) ('Protected ownership and access: '+[IO.Path]::GetFileName($path))
 foreach($id in @('S-1-5-18','S-1-5-32-544')){Check (@($rules|Where-Object{$_.IdentityReference.Value-eq$id-and$_.AccessControlType-eq'Allow'-and($_.FileSystemRights-band[Security.AccessControl.FileSystemRights]::FullControl)-eq[Security.AccessControl.FileSystemRights]::FullControl}).Count-gt0) 'SYSTEM and administrators retain full access'}
 if($usersRead){$write=[Security.AccessControl.FileSystemRights]'Write,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership';Check (@($rules|Where-Object{$_.IdentityReference.Value-eq'S-1-5-32-545'-and($_.FileSystemRights-band$write)-ne0}).Count-eq0) 'Users cannot replace program files'}
 else{Check $acl.AreAccessRulesProtected 'Private backup inheritance disabled'}
}
function Secure-Backup{
 Check (-not(Test-Path -LiteralPath $backup)) 'No existing backup overwritten'
 New-Item -ItemType Directory -Path $backup|Out-Null
 $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
 $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
 foreach($id in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($id),[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))}
 Set-Acl -LiteralPath $backup -AclObject $acl;Assert-Acl $backup $false
 Set-Content -LiteralPath (Join-Path $backup '.preview3-backup') -Value 'CampusPulse original preview.2 in-place backup 20261010 v1' -Encoding ASCII
}
function Copy-Checked([string]$source,[string]$destination){
 $before=Hash $source
 New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force|Out-Null
 Copy-Item -LiteralPath $source -Destination $destination -Force
 Check ((Hash $destination)-eq$before-and(Hash $source)-eq$before) ('Verified backup/copy: '+[IO.Path]::GetFileName($source))
}
function Service{
 $s=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
 Check ($null-ne$s-and$s.PathName.Trim('"').Equals($exe,[StringComparison]::OrdinalIgnoreCase)-and$s.StartName-eq'LocalSystem') 'Exact registered owned LocalSystem service'
 return $s
}
function Stop-Owned{$s=Service;if($s.State-ne'Stopped'){Stop-Service CampusPulse;(Get-Service CampusPulse).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}}
function Start-Owned{$null=Service;Start-Service CampusPulse;(Get-Service CampusPulse).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))}
function Status{
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','CampusPulse.Control.v1',[IO.Pipes.PipeDirection]::InOut)
 try{$pipe.Connect(5000);$bytes=[Text.Encoding]::UTF8.GetBytes('{"Command":"status"}'+"`n");$pipe.Write($bytes,0,$bytes.Length);$pipe.Flush();$reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8);$pending=$reader.ReadLineAsync();if(-not$pending.Wait(8000)){throw 'Control timeout.'};$reply=$pending.Result|ConvertFrom-Json;if(-not$reply.Success){throw 'Status unavailable.'};return $reply.Snapshot}finally{$pipe.Dispose()}
}
function Read-Backup{
 $null=Safe $backup $backup;Assert-Acl $backup $false
 Check ((Get-Content -LiteralPath (Join-Path $backup '.preview3-backup') -Raw).Trim()-eq'CampusPulse original preview.2 in-place backup 20261010 v1') 'Fixed private backup marker'
 $script:state=Get-Content -LiteralPath (Join-Path $backup 'complete.json') -Raw|ConvertFrom-Json
 Check ($state.ExpectedCommit-eq$ExpectedCommit-and$state.Root-eq$root) 'Backup belongs to this release and registered root'
 foreach($file in $state.Files){$path=Safe (Join-Path $backup $file.Path) $backup;Check ((Hash $path)-eq$file.Sha256) 'Private backup inventory intact'}
}
function Run-Setup([string]$setup,[string]$log){
 $process=Start-Process -FilePath $setup -WindowStyle Hidden -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$root+'"'),('/LOG="'+(Join-Path $evidence $log)+'"'))
 if(-not$process.WaitForExit(120000)){$script:installerPending=$true;$report.InstallerPending=$true;throw 'Installer pending; retain backup and do not restore concurrently.'}
 Check ($process.ExitCode-eq0) 'Installer completed with exit code zero'
}
function Assert-Payload{
 foreach($file in $manifest.payloadFiles){$path=Safe (Join-Path $root $file.path.Replace('/','\')) $root;Check ((Get-Item -LiteralPath $path).Length-eq$file.length-and(Hash $path)-eq$file.sha256) ('CI payload matches: '+$file.path)}
 Check ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.StartsWith($version)) 'Installed service product version'
 foreach($path in @($root,(Join-Path $root 'App'),(Join-Path $root 'Service'),$exe,(Join-Path $root 'App\CampusPulse.App.exe'))){Assert-Acl $path $true}
}
function Assert-Profile{
 $original=Get-Content -LiteralPath (Join-Path $backup 'Data\settings.json') -Raw|ConvertFrom-Json
 Check ((Hash (Join-Path $data 'credentials.dat'))-eq$state.CredentialHash) 'Original encrypted credential bytes retained'
 $dataAcl=Get-Acl -LiteralPath $data
 Check $dataAcl.AreAccessRulesProtected 'Data root inheritance disabled'
 foreach($name in @('settings.json','credentials.dat','events.json')){
  $acl=Get-Acl -LiteralPath (Join-Path $data $name)
  $rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
  Check (@($rules|Where-Object{$_.IdentityReference.Value-notin@('S-1-5-18','S-1-5-32-544')}).Count-eq0) ('Data stays private: '+$name)
  foreach($id in @('S-1-5-18','S-1-5-32-544')){Check (@($rules|Where-Object{$_.IdentityReference.Value-eq$id-and$_.AccessControlType-eq'Allow'-and($_.FileSystemRights-band[Security.AccessControl.FileSystemRights]::FullControl)-eq[Security.AccessControl.FileSystemRights]::FullControl}).Count-gt0) 'Private data retains SYSTEM and administrator access'}
 }
 $current=Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw|ConvertFrom-Json
 foreach($key in @('Enabled','StartWithWindows','UnattendedMode','Username','Carrier','PortalUrl','OnlineCheckSeconds')){Check ($current.$key-eq$original.$key) ('Original setting retained: '+$key)}
 $s=Service;Check (($s.State-eq'Running')-eq[bool]$state.WasRunning) 'Original service running state retained'
 $expectedMode=if($original.StartWithWindows){'Auto'}else{'Manual'};Check ($s.StartMode-eq$expectedMode) 'Startup mode matches saved switch'
 if($state.WasRunning){$snapshot=Status;Check $snapshot.HasPassword 'Saved credential readable by new service';foreach($key in @('Enabled','StartWithWindows','UnattendedMode')){Check ($snapshot.Settings.$key-eq$original.$key) ('Actual restored switch: '+$key)};Check ($snapshot.ActualStartWithWindows-eq[bool]$original.StartWithWindows) 'Actual startup snapshot matches';if($original.UnattendedMode){$power=Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue; if($null-eq$power-or@($power|Where-Object BatteryStatus -eq 2).Count){Check $snapshot.KeepingAwake 'Unattended request active while plugged in'}};$report.NetworkState=$snapshot.State}
 $report.ProfileRestored=$true
}
function Restore-Settings{
 Stop-Owned
 Copy-Checked (Join-Path $backup 'Data\settings.json') (Join-Path $data 'settings.json')
 if($state.WasRunning){Start-Owned;Start-Sleep -Seconds 2}
 Assert-Profile
}
function Restore-Old{
 if($installerPending){throw 'Installer pending; rollback deferred.'}
 Read-Backup;Check ((Hash $oldPackage)-eq$oldPackageHash) 'Untouched published rollback installer'
 Stop-Owned
 Run-Setup $oldPackage 'rollback-preview2.log'
 Stop-Owned
 foreach($file in $state.Files){if($file.Path.StartsWith('Programs/')){$relative=$file.Path.Substring(9);Copy-Checked (Safe (Join-Path $backup $file.Path) $backup) (Safe (Join-Path $root $relative) $root)}elseif($file.Path.StartsWith('Data/')){Copy-Checked (Safe (Join-Path $backup $file.Path) $backup) (Safe (Join-Path $data $file.Path.Substring(5)) $data)}}
 if($state.WasRunning){Start-Owned;Start-Sleep -Seconds 2}
 Assert-Profile;$report.RollbackCompleted=$true
}
function Native-Window{
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
 $script:owned=Start-Process -FilePath (Join-Path $root 'App\CampusPulse.App.exe') -WindowStyle Hidden -PassThru
 $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$owned.Id);$window=$null
 for($i=0;$i-lt30-and$null-eq$window;$i++){Start-Sleep -Milliseconds 300;$window=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)}
 Check ($null-ne$window) 'Exact installed native window exists'
 Start-Sleep -Seconds 2
 foreach($id in @('VersionText','PasswordInput','EnabledBox','StartWithWindowsBox','UnattendedBox','UpdateStatusText','CheckUpdateButton')){
  $c=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id);$element=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,$c);Check ($null-ne$element) ('Installed control exists: '+$id)
  if($id-eq'VersionText'){Check ($element.Current.Name-eq$version) 'Installed native version displayed'}
  if($id-eq'PasswordInput'){Check $element.Current.IsPassword 'Native password control masked'}
  if($id.EndsWith('Box')){
   $original=Get-Content -LiteralPath (Join-Path $backup 'Data\settings.json') -Raw|ConvertFrom-Json
   $key=switch($id){'EnabledBox'{'Enabled'}'StartWithWindowsBox'{'StartWithWindows'}'UnattendedBox'{'UnattendedMode'}}
   $toggle=$element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
   Check (($toggle.Current.ToggleState.ToString()-eq'On')-eq[bool]$original.$key) ('Native restored switch: '+$key)
  }
 }
 Check $owned.Responding 'Installed settings process responsive; tray resource loaded'
 $shell=New-Object -ComObject WScript.Shell
 foreach($folder in @([Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('CommonPrograms'))){
  $shortcut=$shell.CreateShortcut((Join-Path $folder 'CampusPulse.lnk'))
  Check ($shortcut.TargetPath-eq(Join-Path $root 'App\CampusPulse.App.exe')) 'Product shortcut uses installed EXE and its icon'
 }
 $icon=[Drawing.Icon]::ExtractAssociatedIcon((Join-Path $root 'App\CampusPulse.App.exe'));Check ($null-ne$icon) 'Installed EXE icon extracted';$icon.Dispose()
 $icon=[Drawing.Icon]::ExtractAssociatedIcon($package);Check ($null-ne$icon) 'Exact installer icon extracted';$bitmap=$icon.ToBitmap();$bitmap.Save((Join-Path $evidence 'installer-icon.png'));$bitmap.Dispose();$icon.Dispose()
 Stop-Process -Id $owned.Id;$script:owned=$null
}
try{
 $reg=Get-ItemProperty -LiteralPath $registration
 $root=Safe ([IO.Path]::GetFullPath($reg.InstallLocation).TrimEnd('\')) ([IO.Path]::GetFullPath($reg.InstallLocation).TrimEnd('\'))
 Check ($root-ne[IO.Path]::GetPathRoot($root).TrimEnd('\')) 'Installation root is not an entire drive'
 $exe=Join-Path $root 'Service\CampusPulse.Service.exe';$null=Service
 foreach($path in @($data,$backup,$package,$oldPackage)){$null=Safe $path $path}
 if($Phase-in@('Upgrade','FinalPackage')){
  & (Join-Path $repo 'scripts\verify-release.ps1') -Version $version -Root $delivery -ExpectedCommit $ExpectedCommit
  $manifest=Get-Content -LiteralPath (Join-Path $delivery 'build-manifest.json') -Raw|ConvertFrom-Json
  $report.InstallerSha256=$manifest.sha256
 }
 if($Phase-eq'Upgrade'){
  Check ($reg.DisplayVersion-eq'0.1.0-preview.2') 'Expected existing preview.2 registration'
  Check (@(Get-Process -Name CampusPulse.App -ErrorAction SilentlyContinue).Count-eq0) 'No user settings process needs force-closing'
  Check ((Hash $oldPackage)-eq$oldPackageHash) 'Published rollback installer prepared'
  if($ReuseVerifiedBackup){
   Read-Backup;Assert-Profile
   foreach($file in $state.Files){if($file.Path.StartsWith('Programs/')){Check ((Hash (Safe (Join-Path $root $file.Path.Substring(9)) $root))-eq$file.Sha256) 'Restored original program matches private backup'}}
   $report.OriginalProfileVerifiedBeforeRetry=$true
  }else{
  Secure-Backup
  $originalService=Service
  $inventory=@()
  foreach($name in @('settings.json','credentials.dat','events.json')){$source=Safe (Join-Path $data $name) $data;Copy-Checked $source (Join-Path $backup ('Data\'+$name));$inventory+=@{Path='Data/'+$name;Sha256=Hash (Join-Path $backup ('Data\'+$name))}}
  foreach($component in @('App','Service')){foreach($file in Files (Join-Path $root $component)){$relative=$file.FullName.Substring($root.Length+1).Replace('\','/');$destination=Safe (Join-Path $backup ('Programs\'+$relative)) $backup;Copy-Checked $file.FullName $destination;$inventory+=@{Path='Programs/'+$relative;Sha256=Hash $destination}}}
  $state=[ordered]@{Root=$root;ExpectedCommit=$ExpectedCommit;WasRunning=($originalService.State-eq'Running');CredentialHash=Hash (Join-Path $backup 'Data\credentials.dat');Files=$inventory}
  $state|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $backup 'complete.json') -Encoding UTF8
  Read-Backup
  }
  $mutated=$true;Stop-Owned
  $paused=Get-Content -LiteralPath (Join-Path $backup 'Data\settings.json') -Raw|ConvertFrom-Json;$paused.Enabled=$false
  $paused|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding UTF8
  Run-Setup $package 'upgrade-preview3.log'
  Assert-Payload
  Restore-Settings
  $reg=Get-ItemProperty -LiteralPath $registration;Check ($reg.DisplayVersion-eq$version-and$reg.InstallLocation.TrimEnd('\')-eq$root) 'New version registered in original installation directory'
 }
 elseif($Phase-eq'FinalPackage'){Read-Backup;Assert-Payload;Assert-Profile;Native-Window}
 elseif($Phase-eq'Restore'){Restore-Old}
 elseif($Phase-eq'Cleanup'){
  $final=Get-Content -LiteralPath (Join-Path $evidence 'upgrade-finalpackage.json') -Raw|ConvertFrom-Json
  Check (-not$final.Failure-and$final.ProfileRestored-and$final.ExpectedCommit-eq$ExpectedCommit) 'Completed final acceptance before cleanup'
  Read-Backup;Assert-Profile
  $null=@(Files $backup)
  $resolved=Safe $backup (Join-Path $env:ProgramData 'CampusPulse-Preview3Backup-20261010')
  Check ($resolved-eq[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'CampusPulse-Preview3Backup-20261010'))) 'Exact private backup deletion scope'
  Remove-Item -LiteralPath $resolved -Recurse -Force
  Check (-not(Test-Path -LiteralPath $resolved)) 'Only this run private backup removed'
 }
}catch{
 $report.Failure=$_.Exception.Message
 if($mutated-and-not$installerPending){try{Restore-Old}catch{$report.RollbackFailure=$_.Exception.Message}}
}finally{
 if($owned-and-not$owned.HasExited){Stop-Process -Id $owned.Id -Force}
 $report.Finished=[DateTimeOffset]::Now.ToString('o');$report|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $resultPath -Encoding UTF8
}
[ordered]@{Phase=$Phase;Checks=$report.Checks.Count;Passed=@($report.Checks|Where-Object Passed).Count;Failure=$report.Failure;ProfileRestored=$report.ProfileRestored;Finished=$report.Finished}|ConvertTo-Json
if($report.Failure){exit 1}
