[CmdletBinding()]
param([ValidateSet('Before', 'After')][string]$Phase = 'Before', [switch]$Elevate)

# User performs the reboot. This script only records timestamps and fixed service state.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Phase', $Phase)
    if (-not $child.WaitForExit(45000)) { throw 'Boot inspection pending. Check local UAC.' }
    if ($child.ExitCode -ne 0) { throw 'Boot inspection incomplete. See local sanitized result.' }
    Get-Content -LiteralPath (Join-Path $evidence ('boot-' + $Phase.ToLowerInvariant() + '.json'))
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
try {
    $os = Get-CimInstance Win32_OperatingSystem
    $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if ($null -eq $service -or $service.PathName.Trim('"') -ne
        (Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe')) { throw 'Unexpected service target' }
    $record = [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); BootTime = $os.LastBootUpTime.ToString('o');
        ServiceState = $service.State; StartMode = $service.StartMode; ProcessStarted = $null }
    if ($service.ProcessId -gt 0) { $record.ProcessStarted = (Get-Process -Id $service.ProcessId).StartTime.ToString('o') }
    if ($Phase -eq 'After') {
        $before = Get-Content -LiteralPath (Join-Path $evidence 'boot-before.json') -Raw | ConvertFrom-Json
        $boot = [DateTime]$os.LastBootUpTime
        $userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        # DWM/UMFD also have interactive sessions at the sign-in screen; exclude them.
        $logons = @(Get-CimInstance Win32_LogonSession -Filter 'LogonType=2 OR LogonType=10' |
            Where-Object { $_.StartTime -ge $boot } | Where-Object {
                $accounts = @(Get-CimAssociatedInstance -InputObject $_ -Association Win32_LoggedOnUser -ResultClassName Win32_Account)
                @($accounts | Where-Object { $_.SID -eq $userSid }).Count -gt 0
            } | Sort-Object StartTime)
        $earliest = if ($logons.Count) { $logons[0].StartTime } else { $null }
        $record.NewBootObserved = $boot -gt [DateTime]$before.BootTime
        $record.EarliestInteractiveLogon = if ($null -ne $earliest) { $earliest.ToString('o') } else { $null }
        $record.StartedBeforeInteractiveLogon = $record.NewBootObserved -and $null -ne $earliest -and
            $null -ne $record.ProcessStarted -and [DateTime]$record.ProcessStarted -lt $earliest
        # Windows may restore and lock a session before the user returns.
        # Keep session creation and actual user unlock as separate evidence.
        $record.LockUnlockQuerySucceeded = $false
        $record.CurrentUserLockUnlockEvents = @()
        $record.StartedWhileLockedBeforeUserUnlock = $false
        try {
            $boundaries = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4800, 4801; StartTime = $boot } -ErrorAction Stop |
                Where-Object {
                    [xml]$xml = $_.ToXml()
                    @($xml.Event.EventData.Data | Where-Object {
                        $_.Name -eq 'TargetUserSid' -and $_.'#text' -eq $userSid
                    }).Count -gt 0
                } | Sort-Object TimeCreated)
            $record.LockUnlockQuerySucceeded = $true
            $record.CurrentUserLockUnlockEvents = @($boundaries | ForEach-Object {
                [ordered]@{ Time = $_.TimeCreated.ToString('o'); Id = $_.Id }
            })
            if ($null -ne $record.ProcessStarted) {
                $started = [DateTime]$record.ProcessStarted
                $prior = @($boundaries | Where-Object { $_.TimeCreated -le $started })
                $firstUnlock = @($boundaries | Where-Object { $_.Id -eq 4801 } | Select-Object -First 1)
                $record.StartedWhileLockedBeforeUserUnlock = $record.NewBootObserved -and
                    $prior.Count -gt 0 -and $prior[-1].Id -eq 4800 -and
                    $firstUnlock.Count -gt 0 -and $started -lt $firstUnlock[0].TimeCreated
            }
        }
        catch { # Missing audit events cannot establish this proof.
        }
        $record.TimestampLimit = 'Current service process only; a restart after boot can prevent this proof.'
    }
    $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence ('boot-' + $Phase.ToLowerInvariant() + '.json')) -Encoding UTF8
    exit 0
}
catch {
    Set-Content -LiteralPath (Join-Path $evidence 'boot-failure.txt') -Value $_.Exception.GetType().FullName -Encoding UTF8
    exit 1
}
