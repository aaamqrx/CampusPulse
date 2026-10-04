[CmdletBinding()]
param([ValidateSet('Preflight', 'Update', 'Inspect', 'Rollback')][string]$Phase = 'Preflight',
    [switch]$Execute, [switch]$Elevate)

# Fixed local recovery candidate. No credential decryption, settings commands or login commands.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence = Join-Path $repo '.local\recovery-repair-20261003'
$candidate = Join-Path $evidence 'candidate'
$target = Join-Path $env:ProgramFiles 'CampusPulse\Service'
$serviceExe = Join-Path $target 'CampusPulse.Service.exe'
$data = Join-Path $env:ProgramData 'CampusPulse'
$backup = Join-Path $env:ProgramData 'CampusPulse-RecoveryBackup-20261003'
$resultPath = Join-Path $evidence ('r5-' + $Phase.ToLowerInvariant() + '.json')
$report = [ordered]@{ Time=[DateTimeOffset]::Now.ToString('o'); Phase=$Phase; Checks=@();
    BackendUpdated=$false; RollbackCompleted=$false; BackupRetained=$false; Failure=$null; Finished=$null }
$mutationStarted = $false
$backupReady = $false
function Check([bool]$ok, [string]$name) {
    $report.Checks += [ordered]@{Name=$name;Passed=$ok}
    if (-not $ok) { throw [InvalidOperationException]::new($name) }
}
function Safe-Path([string]$path, [string]$root) {
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\')
    $base = [IO.Path]::GetFullPath($root).TrimEnd('\')
    if (-not ($full.Equals($base,[StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase))) { throw 'Path outside fixed scope' }
    $current=$full
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked path refused' }
        $current=Split-Path -Parent $current
    }
    return $full
}
function Service-Record {
    $record=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    Check ($null -ne $record -and $record.PathName.Trim('"').Equals($serviceExe,[StringComparison]::OrdinalIgnoreCase)) 'Exact installed service path'
    Check ($record.StartName -eq 'LocalSystem') 'Expected service identity'
    return $record
}
function Read-Status {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(5000)
        $bytes=[Text.Encoding]::UTF8.GetBytes('{"Command":"status"}'+"`n")
        $pipe.Write($bytes,0,$bytes.Length); $pipe.Flush()
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
        $pending=$reader.ReadLineAsync()
        if (-not $pending.Wait(8000)) { throw 'Status timeout' }
        $reply=$pending.Result | ConvertFrom-Json
        if (-not $reply.Success -or $null -eq $reply.Snapshot) { throw 'Status refused' }
        return $reply.Snapshot
    } finally { $pipe.Dispose() }
}
function Safe-Snapshot($s) {
    # Do not export Settings, Message, raw replies, account identity or credential hashes.
    return [ordered]@{ State=$s.State; ErrorCode=$s.ErrorCode; LastCheck=$s.LastCheck;
        LastSuccess=$s.LastSuccess; NextCheck=$s.NextCheck; HasPassword=$s.HasPassword;
        Enabled=$s.Settings.Enabled; StartWithWindows=$s.Settings.StartWithWindows;
        UnattendedMode=$s.Settings.UnattendedMode; KeepingAwake=$s.KeepingAwake;
        AuthenticationBlocked=$s.Settings.AuthenticationBlocked;
        BlockedReasonCode=$s.Settings.BlockedReasonCode; AuthenticationRetryAt=$s.Settings.AuthenticationRetryAt;
        Diagnostics=$s.Diagnostics;
        Events=@($s.RecentEvents | ForEach-Object { [ordered]@{
            Time=$_.Time; Kind=$_.Kind; ReasonCode=$_.ReasonCode; Source=$_.Source } }) }
}
function Stop-Backend {
    $null=Service-Record
    Stop-Service -Name CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
}
function Start-Backend {
    $null=Service-Record
    Start-Service -Name CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
}
function Files-In([string]$root) {
    foreach ($item in Get-ChildItem -LiteralPath $root -File -Recurse -Force) {
        $null=Safe-Path $item.FullName $root
        [ordered]@{Path=$item.FullName.Substring($root.Length+1); Sha256=(Get-FileHash -LiteralPath $item.FullName).Hash}
    }
}
function Copy-Files([string]$from, [string]$to, $files) {
    foreach ($file in $files) {
        $source=Safe-Path (Join-Path $from $file.Path) $from
        $dest=Safe-Path (Join-Path $to $file.Path) $to
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $dest -Force
        Check ((Get-FileHash -LiteralPath $dest).Hash -eq $file.Sha256) ('Copied binary: '+$file.Path)
    }
}
function Protect-Backup {
    Check (-not (Test-Path -LiteralPath $backup)) 'No existing backup overwritten'
    New-Item -ItemType Directory -Path $backup | Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    foreach ($id in @('S-1-5-18','S-1-5-32-544')) {
        $sid=[Security.Principal.SecurityIdentifier]::new($id)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    Set-Acl -LiteralPath $backup -AclObject $acl
    $actual=Get-Acl -LiteralPath $backup
    $rules=@($actual.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
    Check ($actual.AreAccessRulesProtected -and $rules.Count -eq 2 -and
        @($rules | Where-Object {$_.IdentityReference.Value -notin @('S-1-5-18','S-1-5-32-544')}).Count -eq 0) 'Backup restricted to SYSTEM and administrators'
    Set-Content -LiteralPath (Join-Path $backup '.recovery-backup') -Value 'CampusPulse recovery backup 20261003 v1' -Encoding ASCII
}
function Restore-Backend {
    $null=Safe-Path $backup (Join-Path $env:ProgramData 'CampusPulse-RecoveryBackup-20261003')
    Check ((Get-Content -LiteralPath (Join-Path $backup '.recovery-backup') -Raw).Trim() -eq 'CampusPulse recovery backup 20261003 v1') 'Fixed rollback marker'
    $saved=Get-Content -LiteralPath (Join-Path $backup 'backup-manifest.json') -Raw | ConvertFrom-Json
    Check ($saved.ServicePath -eq $serviceExe -and $saved.Complete) 'Complete rollback manifest'
    Stop-Backend
    Copy-Files (Join-Path $backup 'Service') $target $saved.Files
    foreach ($file in $saved.AddedFiles) {
        $path=Safe-Path (Join-Path $target $file) $target
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    foreach ($name in @('settings.json','credentials.dat','events.json')) {
        $source=Safe-Path (Join-Path $backup ('Data\'+$name)) $backup
        $dest=Safe-Path (Join-Path $data $name) $data
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $dest -Force }
        elseif (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Force }
    }
    Start-Backend
    $report.RollbackCompleted=$true
}

if ($Phase -in @('Update','Rollback') -and -not $Execute) {
    Write-Output 'Plan only. Preflight checks manifest, exact service, online state, switches and credential availability.'
    Write-Output 'Update replaces only installed Service binaries; preserves credentials, identity, switches, startup mode and user UI. Protected rollback retained.'
    Write-Output 'No network disconnect, power-plan change, reboot, credential export, settings command or reconnect command.'
    exit 0
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required; use -Elevate for Windows UAC.' }
    $args=@('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Phase',$Phase)
    if ($Execute) { $args+='-Execute' }
    $child=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $args
    if (-not $child.WaitForExit(45000)) { Write-Output 'Operation pending. Inspect timestamped result before retrying.'; exit 2 }
    if (Test-Path -LiteralPath $resultPath) {
        $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        [ordered]@{Phase=$result.Phase;Time=$result.Time;Checks=$result.Checks.Count;
            Passed=@($result.Checks | Where-Object {$_.Passed}).Count;BackendUpdated=$result.BackendUpdated;
            RollbackCompleted=$result.RollbackCompleted;Failure=$result.Failure;Finished=$result.Finished} | ConvertTo-Json
    }
    exit $child.ExitCode
}
try {
    $null=Safe-Path $target (Join-Path $env:ProgramFiles 'CampusPulse\Service')
    $null=Safe-Path $data (Join-Path $env:ProgramData 'CampusPulse')
    $null=Safe-Path $candidate (Join-Path $repo '.local\recovery-repair-20261003\candidate')
    $null=Safe-Path $backup (Join-Path $env:ProgramData 'CampusPulse-RecoveryBackup-20261003')
    $record=Service-Record
    $report.ServiceVersion=(Get-Item -LiteralPath $serviceExe).VersionInfo.ProductVersion
    $report.StartMode=$record.StartMode
    $report.BeforeProcessId=$record.ProcessId
    if ($Phase -eq 'Rollback') { Restore-Backend }
    else {
        $before=Read-Status
        $report.Before=Safe-Snapshot $before
        if ($Phase -in @('Preflight','Update')) {
            Check ($record.State -eq 'Running') 'Backend is running'
            Check ($before.State -eq 5) 'Online before candidate update'
            Check ($report.ServiceVersion.StartsWith('0.1.0-preview.1')) 'Expected installed baseline version'
            Check $before.HasPassword 'Saved credential available'
            $manifest=Get-Content -LiteralPath (Join-Path $candidate 'candidate-manifest.json') -Raw | ConvertFrom-Json
            Check ($manifest.SourceVersion -eq '0.1.0-preview.2-dev') 'Expected candidate version'
            $files=@(($manifest.Components | Where-Object {$_.Name -eq 'Service'}).Files)
            Check ($files.Count -eq 226) 'Expected Service file count'
            foreach ($file in $files) {
                $path=Safe-Path (Join-Path $candidate ('Service\'+$file.Path)) (Join-Path $candidate 'Service')
                Check ((Get-FileHash -LiteralPath $path).Hash -eq $file.Sha256) ('Candidate hash: '+$file.Path)
                $null=Safe-Path (Join-Path $target $file.Path) $target
            }
        }
        if ($Phase -eq 'Update') {
            Protect-Backup
            $backupReady=$true
            Stop-Backend
            $oldFiles=@(Files-In $target)
            Copy-Files $target (Join-Path $backup 'Service') $oldFiles
            New-Item -ItemType Directory -Path (Join-Path $backup 'Data') | Out-Null
            foreach ($name in @('settings.json','credentials.dat','events.json')) {
                $source=Safe-Path (Join-Path $data $name) $data
                if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $backup ('Data\'+$name)) }
            }
            $savedSettings=Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
            $credentialHash=(Get-FileHash -LiteralPath (Join-Path $data 'credentials.dat')).Hash
            $oldNames=@($oldFiles | ForEach-Object {$_.Path})
            [ordered]@{Complete=$true; ServicePath=$serviceExe; Files=$oldFiles;
                AddedFiles=@($files | Where-Object {$_.Path -notin $oldNames} | ForEach-Object {$_.Path})} |
                ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'backup-manifest.json') -Encoding UTF8
            $mutationStarted=$true
            Copy-Files (Join-Path $candidate 'Service') $target $files
            Start-Backend
            $after=$null
            for ($i=0;$i -lt 24;$i++) {
                Start-Sleep -Milliseconds 500
                $after=Read-Status
                if ($after.State -eq 5) { break }
                if (-not $after.HasPassword) { break }
            }
            Check ($after.HasPassword) 'Credential still available'
            foreach ($key in @('Enabled','StartWithWindows','UnattendedMode','Username','Carrier','PortalUrl','OnlineCheckSeconds')) {
                Check ($after.Settings.$key -eq $savedSettings.$key) ('Original setting retained: '+$key)
            }
            Check ((Get-FileHash -LiteralPath (Join-Path $data 'credentials.dat')).Hash -eq $credentialHash) 'Encrypted credential unchanged'
            $current=Service-Record
            $report.AfterProcessId=$current.ProcessId
            Check ($current.State -eq 'Running' -and $current.ProcessId -ne $record.ProcessId) 'New backend process running'
            Check ($current.StartMode -eq $record.StartMode) 'Startup mode unchanged'
            Check ($after.State -eq 5) 'Online observed after backend start'
            Check ($null -eq $after.Diagnostics.LastSubmissionAt) 'No credential submission during update checks'
            $report.After=Safe-Snapshot $after
            $report.ServiceVersion=(Get-Item -LiteralPath $serviceExe).VersionInfo.ProductVersion
            $report.BackendUpdated=$true
        }
    }
} catch {
    $report.Failure=[ordered]@{Type=$_.Exception.GetType().FullName;Line=$_.InvocationInfo.ScriptLineNumber}
    if ($mutationStarted) {
        try { Restore-Backend } catch { $report.RollbackFailure=$_.Exception.GetType().FullName }
    } elseif ($backupReady) {
        try { Start-Backend } catch { $report.RestartFailure=$_.Exception.GetType().FullName }
    }
} finally {
    $report.BackupRetained=Test-Path -LiteralPath $backup
    $report.Finished=[DateTimeOffset]::Now.ToString('o')
    $report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if ($null -ne $report.Failure) { exit 1 }
exit 0
