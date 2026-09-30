[CmdletBinding()]
param(
    [ValidateSet('Power', 'Lock', 'Sleep')][string]$Scenario = 'Power',
    [ValidateRange(15, 180)][int]$DurationSeconds = 90,
    [switch]$Elevate
)

# Read-only sampling during user-operated power/lock checks. Never change power or credentials.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Scenario', $Scenario, '-DurationSeconds', $DurationSeconds)
    Write-Host ('Read-only monitor dispatched; PID={0}. Check local ready/result files, not this dispatch, for evidence.' -f $child.Id)
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AcceptancePower {
 [StructLayout(LayoutKind.Sequential)] public struct Status {
  public byte AC, BatteryFlag, BatteryPercent, SystemFlag; public uint LifeTime, FullLifeTime;
 }
 [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out Status value);
 public static int AC() { Status value; return GetSystemPowerStatus(out value) ? value.AC : -1; }
}
'@
function Get-Status {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(1500)
        $request = [Text.Encoding]::UTF8.GetBytes('{"Command":"status"}' + "`n")
        $pipe.Write($request, 0, $request.Length); $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait(2000)) { throw 'Status timeout' }
        $reply = $pending.Result | ConvertFrom-Json
        if (-not $reply.Success -or $null -eq $reply.Snapshot) { throw 'Status unavailable' }
        return $reply.Snapshot
    }
    finally { $pipe.Dispose() }
}
$samples = [Collections.Generic.List[object]]::new()
$failure = $null
$planBefore = & powercfg.exe /query | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Power plan query failed' }
$started = [DateTimeOffset]::Now
$deadline = $started.AddSeconds($DurationSeconds)
try {
    $initial = Get-Status
    if ($Scenario -eq 'Sleep') {
        if ($initial.Settings.UnattendedMode -or $initial.KeepingAwake) { throw 'User must disable unattended mode before sleep test' }
    }
    elseif (-not $initial.Settings.UnattendedMode) { throw 'Unattended mode must already be enabled by user' }
    Set-Content -LiteralPath (Join-Path $evidence ($Scenario.ToLowerInvariant() + '-ready.txt')) -Value ([DateTimeOffset]::Now.ToString('o')) -Encoding UTF8
    while ([DateTimeOffset]::Now -lt $deadline) {
        $s = Get-Status
        $requests = & powercfg.exe /requests 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'Power request query failed' }
        $sample = [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); AC = [AcceptancePower]::AC();
            ServiceRunning = ((Get-Service CampusPulse).Status -eq 'Running'); Enabled = $s.Settings.Enabled;
            UnattendedMode = $s.Settings.UnattendedMode; KeepingAwake = $s.KeepingAwake;
            PowerRequest = $requests.Contains('CampusPulse'); LastCheck = $s.LastCheck; State = $s.State }
        $samples.Add($sample)
        $sample | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $evidence ($Scenario.ToLowerInvariant() + '-samples.jsonl')) -Encoding UTF8
        if ($Scenario -eq 'Power') {
            $connectedBefore = $false; $released = $false; $recovered = $false
            foreach ($point in $samples) {
                if (-not $released -and $point.AC -eq 1 -and $point.KeepingAwake -and $point.PowerRequest) { $connectedBefore = $true }
                if ($connectedBefore -and $point.AC -eq 0 -and -not $point.KeepingAwake -and -not $point.PowerRequest) { $released = $true }
                if ($released -and $point.AC -eq 1 -and $point.KeepingAwake -and $point.PowerRequest) { $recovered = $true }
            }
            if ($recovered) { break }
        }
        if ($Scenario -eq 'Sleep' -and $samples.Count -ge 2) {
            $sleepEvents = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power';
                Id = @(42, 107, 506, 507); StartTime = $started.LocalDateTime } -ErrorAction SilentlyContinue)
            $resume = @($sleepEvents | Where-Object { $_.Id -in @(107, 507) } | Sort-Object TimeCreated)
            if (@($sleepEvents | Where-Object { $_.Id -in @(42, 506) }).Count -gt 0 -and
                $resume.Count -gt 0 -and $sample.ServiceRunning -and $null -ne $sample.LastCheck -and
                [DateTimeOffset]$sample.LastCheck -gt [DateTimeOffset]$resume[-1].TimeCreated -and $sample.State -eq 5) { break }
        }
        Start-Sleep -Seconds 2
    }
}
catch { $failure = $_.Exception.GetType().FullName }
$planAfter = & powercfg.exe /query | Out-String
$planUnchanged = $LASTEXITCODE -eq 0 -and $planBefore -eq $planAfter
$result = [ordered]@{ Scenario = $Scenario; Time = [DateTimeOffset]::Now.ToString('o'); Failure = $failure;
    SampleCount = $samples.Count; AllServiceRunning = ($samples.Count -gt 0 -and @($samples | Where-Object { -not $_.ServiceRunning }).Count -eq 0);
    PlanUnchanged = $planUnchanged; PowerCycleObserved = ($Scenario -eq 'Power' -and $recovered);
    LockObserved = 'User confirmation required; status sampling alone cannot prove lock'; Samples = @($samples.ToArray()) }
if ($Scenario -eq 'Sleep') {
    $result.SleepEvents = @($sleepEvents | Sort-Object TimeCreated | ForEach-Object {
        [ordered]@{ Id = $_.Id; Time = $_.TimeCreated.ToString('o') }
    })
    $result.SleepAndResumeObserved = @($sleepEvents | Where-Object { $_.Id -in @(42, 506) }).Count -gt 0 -and
        @($sleepEvents | Where-Object { $_.Id -in @(107, 507) }).Count -gt 0
    $resume = @($sleepEvents | Where-Object { $_.Id -in @(107, 507) } | Sort-Object TimeCreated)
    $result.ResumeNetworkChecked = $samples.Count -gt 0 -and $resume.Count -gt 0 -and
        $null -ne $samples[-1].LastCheck -and [DateTimeOffset]$samples[-1].LastCheck -gt
        [DateTimeOffset]$resume[-1].TimeCreated -and $samples[-1].State -eq 5
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence ($Scenario.ToLowerInvariant() + '-result.json')) -Encoding UTF8
if ($null -ne $failure -or -not $planUnchanged) { exit 1 }
if ($Scenario -eq 'Power' -and -not $recovered) { exit 1 }
if ($Scenario -eq 'Sleep' -and (-not $result.SleepAndResumeObserved -or -not $result.ResumeNetworkChecked)) { exit 1 }
exit 0
