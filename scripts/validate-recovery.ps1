[CmdletBinding()]
param([switch]$Execute, [switch]$Elevate)

$ErrorActionPreference = 'Stop'
if (-not $Execute) {
    Write-Host 'Plan: pause automatic authentication; refresh the marked validation service with reviewed binaries;'
    Write-Host 'apply finite recovery (5/15/60 seconds, then NONE); terminate its verified process once;'
    Write-Host 'verify power release and SCM restart; restore original switches and confirm credential bytes unchanged.'
    Write-Host 'No changes made. Requires explicit approval and -Execute. No network shutdown or manual authentication.'
    exit 0
}
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Execute')
    if (-not $child.WaitForExit(55000)) { throw 'Recovery validation pending. Read local evidence; do not infer success.' }
    Get-Content -LiteralPath (Join-Path $evidence 'recovery-validation.json')
    if ($child.ExitCode -ne 0) { throw 'Recovery validation failed. Check restoration evidence.' }
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProductRecoveryQuery {
 [StructLayout(LayoutKind.Sequential)] struct FailureActions {
  public uint Reset; public IntPtr Reboot, Command; public uint Count; public IntPtr Actions;
 }
 [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
 [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
 [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool QueryServiceConfig2(IntPtr service,uint level,IntPtr buffer,uint size,out uint needed);
 [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
 public static uint[] Read() {
  IntPtr manager=OpenSCManager(null,null,1),service=IntPtr.Zero,buffer=IntPtr.Zero;
  try {
   service=OpenService(manager,"CampusPulse",1); if(service==IntPtr.Zero)throw new Exception("OpenService failed");
   uint needed;QueryServiceConfig2(service,2,IntPtr.Zero,0,out needed);buffer=Marshal.AllocHGlobal((int)needed);
   if(!QueryServiceConfig2(service,2,buffer,needed,out needed))throw new Exception("Recovery query failed");
   var value=(FailureActions)Marshal.PtrToStructure(buffer,typeof(FailureActions));
   if(value.Count>16)throw new Exception("Unexpected recovery policy");
   var result=new uint[1+value.Count*2];result[0]=value.Reset;
   for(int i=0;i<value.Count*2;i++)result[i+1]=(uint)Marshal.ReadInt32(value.Actions,i*4);
   return result;
  }finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(service!=IntPtr.Zero)CloseServiceHandle(service);if(manager!=IntPtr.Zero)CloseServiceHandle(manager);}
 }
}
'@
function Send($Request) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(2000)
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Depth 5 -Compress) + "`n")
        $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
        $task = $reader.ReadLineAsync()
        if (-not $task.Wait(5000)) { throw 'Status timeout' }
        $reply = $task.Result | ConvertFrom-Json
        if (-not $reply.Success -or $null -eq $reply.Snapshot) { throw 'Command failed' }
        return $reply.Snapshot
    } finally { $pipe.Dispose() }
}
function Has-PowerRequest {
    $text = & powercfg.exe /requests 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Power query failed' }
    return $text.Contains('CampusPulse')
}
$original = $null; $changed = $false; $restored = $false; $failure = $null
$steps = [Collections.Generic.List[object]]::new()
function Record([string]$Name, [bool]$Passed) {
    $steps.Add([ordered]@{ Name=$Name; Passed=$Passed; Time=[DateTimeOffset]::Now.ToString('o') })
    if (-not $Passed) { throw 'Recovery assertion failed' }
}
try {
    $expected = Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe'
    $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if ($null -eq $service -or $service.PathName.Trim('"') -ne $expected -or $service.State -ne 'Running') { throw 'Unexpected service target' }
    $before = Send @{Command='status'}
    $original = $before.Settings | ConvertTo-Json -Compress | ConvertFrom-Json
    if ($original.AuthenticationBlocked -or -not $original.UnattendedMode -or -not $before.KeepingAwake) { throw 'Need healthy plugged-in unattended state' }
    $credentialPath = Join-Path $env:ProgramData 'CampusPulse\credentials.dat'
    $credentialHash = (Get-FileHash -LiteralPath $credentialPath).Hash
    $changed = $true
    $paused = Send @{Command='pause'}
    Record 'Automatic authentication paused before deployment' (-not $paused.Settings.Enabled)
    & (Join-Path $repo 'scripts\dev-service-validation.ps1') -Action Refresh *> (Join-Path $evidence 'refresh-fix.txt')
    $paused = Send @{Command='status'}
    Record 'Refreshed service retains paused authentication' (-not $paused.Settings.Enabled -and $paused.HasPassword)
    & sc.exe failure CampusPulse reset= 86400 actions= 'restart/5000/restart/15000/restart/60000//0' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Recovery configuration failed' }
    & sc.exe failureflag CampusPulse 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Recovery flag configuration failed' }
    $policy = [ProductRecoveryQuery]::Read()
    Record 'Finite recovery policy read back' (($policy -join ',') -eq '86400,1,5000,1,15000,1,60000,0,0')
    Start-Sleep -Seconds 4
    Record 'Power request active while authentication paused' (Has-PowerRequest)
    $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    $process = Get-CimInstance Win32_Process -Filter ("ProcessId={0}" -f $service.ProcessId)
    if ($service.PathName.Trim('"') -ne $expected -or $null -eq $process -or $process.ExecutablePath -ne $expected) { throw 'Process identity mismatch' }
    $oldProcessId = $service.ProcessId
    Stop-Process -Id $oldProcessId -Force
    Start-Sleep -Milliseconds 500
    Record 'Process termination releases power request' (-not (Has-PowerRequest))
    $deadline = [DateTimeOffset]::Now.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    } while (($service.State -ne 'Running' -or $service.ProcessId -eq $oldProcessId) -and [DateTimeOffset]::Now -lt $deadline)
    Record 'SCM restarts terminated service with a new process' ($service.State -eq 'Running' -and
        $service.ProcessId -gt 0 -and $service.ProcessId -ne $oldProcessId -and $service.PathName.Trim('"') -eq $expected)
    Start-Sleep -Seconds 4
    $after = Send @{Command='status'}
    Record 'Restart preserves paused credentials and reacquires power' (-not $after.Settings.Enabled -and $after.HasPassword -and $after.KeepingAwake -and (Has-PowerRequest))
    $source = Join-Path $repo '.local\service-validation-20260927\Service'
    foreach ($name in @('CampusPulse.Service.exe','CampusPulse.Service.dll','CampusPulse.Core.dll')) {
        Record ('Deployed binary matches reviewed source: '+$name) ((Get-FileHash (Join-Path $source $name)).Hash -eq
            (Get-FileHash (Join-Path (Split-Path -Parent $expected) $name)).Hash)
    }
}
catch { $failure = $_.Exception.GetType().FullName }
finally {
    if ($changed) {
        try {
            if ((Get-Service CampusPulse).Status -eq 'Stopped') { Start-Service CampusPulse }
            (Get-Service CampusPulse).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
            $current = Send @{Command='status'}
            if ($current.Settings.Username -ne $original.Username -or $current.Settings.Carrier -ne $original.Carrier -or
                $current.Settings.PortalUrl -ne $original.PortalUrl) { throw 'Concurrent identity change' }
            $null = Send @{Command='save';Settings=$original}
            Start-Sleep -Seconds 4
            $current = Send @{Command='status'}
            $restored = (($current.Settings | ConvertTo-Json -Compress) -eq ($original | ConvertTo-Json -Compress)) -and
                $current.ActualStartWithWindows -eq $original.StartWithWindows -and
                (Get-FileHash -LiteralPath $credentialPath).Hash -eq $credentialHash
        } catch { $failure='Restoration requires local review'; $restored=$false }
    }
    [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');Failure=$failure;Restored=$restored;
        Steps=@($steps.ToArray());OnlyOneForcedTermination=$true;NoManualAuthentication=$true} | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath (Join-Path $evidence 'recovery-validation.json') -Encoding UTF8
}
if ($null -eq $failure -and $restored) { exit 0 }
exit 1
