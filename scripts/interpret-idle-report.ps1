[CmdletBinding()]
param()

# Interpret an existing ignored OS report. No elevation, power changes, or desktop input.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
[xml]$report = Get-Content (Join-Path $evidence 'idle-sleepstudy.xml') -Raw
$idle = Get-Content (Join-Path $evidence 'idle-result.json') -Raw | ConvertFrom-Json
$from = [DateTimeOffset](Get-Content (Join-Path $evidence 'idle-ready.txt'))
$until = [DateTimeOffset]$idle.Time
$lastSample = [DateTimeOffset]$idle.Samples[-1].Time
$states = @($report.SleepStudy.ScenarioInstances.ChildNodes | Where-Object {
    $_.LocalName -eq 'OsStateInstance' -and $_.GetAttribute('EntryTimestamp') -and $_.GetAttribute('ExitTimestamp') -and
    [DateTimeOffset]$_.GetAttribute('EntryTimestamp') -le $until -and [DateTimeOffset]$_.GetAttribute('ExitTimestamp') -ge $from
} | ForEach-Object { [ordered]@{Type=$_.GetAttribute('Type');Start=$_.GetAttribute('EntryTimestamp');
    End=$_.GetAttribute('ExitTimestamp');EntryReason=$_.GetAttribute('EntryReason');ExitReason=$_.GetAttribute('ExitReason')} })
$sleepCount = @($states | Where-Object {$_.Type -eq 'Sleep'}).Count
$screenOffCoversLastSample = @($states | Where-Object {$_.Type -eq 'Screen Off' -and
    [DateTimeOffset]$_.Start -le $lastSample -and [DateTimeOffset]$_.End -ge $lastSample}).Count -gt 0
$passed = $null -eq $idle.Failure -and $idle.AllServiceRunning -and $idle.PlanUnchanged -and $idle.IdleReached -and
    $idle.ContinuousPluggedInPowerRequest -and $sleepCount -eq 0 -and $screenOffCoversLastSample
$result = [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');MonitorStart=$from.ToString('o');MonitorEnd=$until.ToString('o');
    ReportStates=$states;SleepStateCount=$sleepCount;ScreenOffCoversFinalSample=$screenOffCoversLastSample;
    Passed=$passed;Scope='One AC idle check; no overnight or other device guarantee'}
$result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'idle-phase-verdict.json') -Encoding UTF8
$result | ConvertTo-Json -Depth 5
if (-not $passed) { exit 1 }
