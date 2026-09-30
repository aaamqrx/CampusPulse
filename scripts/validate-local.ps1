[CmdletBinding()]
param([switch]$Execute, [switch]$Elevate)

# Explicit opt-in: temporarily change only the three switches and restart this service.
# Preserve credential bytes, identity, portal and all other settings. Never authenticate manually.
$ErrorActionPreference = 'Stop'
if (-not $Execute) {
    Write-Host 'Plan: pause reconnect; check independent power request; toggle startup Manual/Auto;'
    Write-Host 'toggle unattended power request; restart paused validation service; restore original switches.'
    Write-Host 'No changes made. Requires explicit approval and -Execute. No reboot, credential edit or reconnect command.'
    exit 0
}
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Execute')
    if (-not $child.WaitForExit(55000)) { throw 'Validation pending. Check local UAC and local result; no pass inferred.' }
    Get-Content -LiteralPath (Join-Path $evidence 'switch-validation.json')
    if ($child.ExitCode -ne 0) { throw 'Validation failed. Check restored-state evidence before continuing.' }
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$original = $null
$changed = $false
$success = $false
$restored = $false
$failure = $null
function Get-Status {
    return (Send-Request @{ Command = 'status' }).Snapshot
}
function Send-Request($Request) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Depth 5 -Compress) + "`n")
        $pipe.Write($bytes, 0, $bytes.Length)
        $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait(5000)) { throw 'Status timeout' }
        $reply = $pending.Result | ConvertFrom-Json
        if (-not $reply.Success -or $null -eq $reply.Snapshot) { throw 'Command failed' }
        return $reply
    }
    finally { $pipe.Dispose() }
}
function Copy-Settings($Settings) {
    return ($Settings | ConvertTo-Json -Compress | ConvertFrom-Json)
}
function Assert-Identity($Snapshot) {
    if ($Snapshot.Settings.Username -ne $original.Username -or
        $Snapshot.Settings.Carrier -ne $original.Carrier -or
        $Snapshot.Settings.PortalUrl -ne $original.PortalUrl -or
        $Snapshot.Settings.AuthenticationBlocked -ne $original.AuthenticationBlocked) {
        throw 'Concurrent identity change; stop validation'
    }
}
function Save-Switches($Settings) {
    Assert-Identity (Get-Status)
    return (Send-Request @{ Command = 'save'; Settings = $Settings }).Snapshot
}
function Record-Step([string]$Name, [bool]$Passed) {
    $steps.Add([ordered]@{ Name = $Name; Passed = $Passed; Time = [DateTimeOffset]::Now.ToString('o') })
    if (-not $Passed) { throw 'Acceptance assertion failed' }
}
function Has-PowerRequest {
    $text = & powercfg.exe /requests 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Power query failed' }
    return $text.Contains('CampusPulse')
}
try {
    $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if ($null -eq $service -or $service.State -ne 'Running' -or $service.PathName.Trim('"') -ne
        (Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe')) { throw 'Unexpected service target' }
    $before = Get-Status
    $original = Copy-Settings $before.Settings
    if ($original.ConfigVersion -ne 2 -or $original.AuthenticationBlocked) { throw 'Unsupported acceptance state' }
    $credentialPath = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CampusPulse\credentials.dat'
    $credentialHash = (Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash
    $changed = $true
    $paused = (Send-Request @{ Command = 'pause' }).Snapshot
    Start-Sleep -Seconds 4
    $paused = Get-Status
    Record-Step 'CFG-05 pause preserves unattended and power' (-not $paused.Settings.Enabled -and
        $paused.Settings.UnattendedMode -eq $original.UnattendedMode -and $paused.KeepingAwake -eq $before.KeepingAwake)
    $desired = Copy-Settings $paused.Settings
    $desired.StartWithWindows = $false
    $null = Save-Switches $desired
    Start-Sleep -Seconds 1
    $s = Get-Status
    $svc = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    Record-Step 'BOOT-02 immediate Manual with service still Running' ($svc.StartMode -eq 'Manual' -and
        $svc.State -eq 'Running' -and -not $s.Settings.StartWithWindows -and -not $s.ActualStartWithWindows -and -not $s.Settings.Enabled)
    $desired.StartWithWindows = $true
    $null = Save-Switches $desired
    $s = Get-Status
    $svc = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    $reg = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\CampusPulse'
    Record-Step 'BOOT-03 immediate delayed Auto' ($svc.StartMode -eq 'Auto' -and $svc.State -eq 'Running' -and
        $reg.DelayedAutoStart -eq 1 -and $s.Settings.StartWithWindows -and $s.ActualStartWithWindows)
    $desired.UnattendedMode = $false
    $null = Save-Switches $desired
    Start-Sleep -Seconds 4
    $s = Get-Status
    Record-Step 'PWR-02 switch off releases request' (-not $s.KeepingAwake -and -not (Has-PowerRequest) -and -not $s.Settings.Enabled)
    $desired.UnattendedMode = $true
    $null = Save-Switches $desired
    Start-Sleep -Seconds 4
    $s = Get-Status
    Record-Step 'CFG-05 independent plugged-in power with reconnect paused' ($s.KeepingAwake -and (Has-PowerRequest) -and -not $s.Settings.Enabled)
    Stop-Service CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    Record-Step 'PWR-02 service stop releases request' (-not (Has-PowerRequest))
    Start-Service CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
    Start-Sleep -Seconds 4
    $s = Get-Status
    Assert-Identity $s
    Record-Step 'CFG-04 paused service restart preserves configuration' ((($s.Settings | ConvertTo-Json -Compress) -eq
        ($desired | ConvertTo-Json -Compress)) -and $s.HasPassword -and -not $s.Settings.Enabled -and $s.KeepingAwake)
    Record-Step 'SEC-03 credential bytes unchanged' ((Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash -eq $credentialHash)
    $success = $true
}
catch { $failure = $_.Exception.GetType().FullName }
finally {
    if ($changed -and $null -ne $original) {
        try {
            if ((Get-Service CampusPulse).Status -eq 'Stopped') { Start-Service CampusPulse }
            (Get-Service CampusPulse).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
            $null = Save-Switches $original
            Start-Sleep -Seconds 4
            $after = Get-Status
            $restored = (($after.Settings | ConvertTo-Json -Compress) -eq ($original | ConvertTo-Json -Compress)) -and
                $after.ActualStartWithWindows -eq $original.StartWithWindows -and
                (Get-FileHash -LiteralPath $credentialPath -Algorithm SHA256).Hash -eq $credentialHash
        }
        catch { $failure = 'Restoration requires local review'; $restored = $false }
    }
    [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); Success = $success; Restored = $restored;
        Failure = $failure; Steps = @($steps.ToArray()); OriginalSwitches = $(if ($null -ne $original) {
            [ordered]@{ Enabled = $original.Enabled; StartWithWindows = $original.StartWithWindows; UnattendedMode = $original.UnattendedMode }
        }) } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence 'switch-validation.json') -Encoding UTF8
}
if ($success -and $restored) { exit 0 }
exit 1
