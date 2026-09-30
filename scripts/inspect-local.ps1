[CmdletBinding()]
param([switch]$Elevate)

# Read-only acceptance evidence. Never serialize the full settings or credentials.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidenceDirectory = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'))
    if (-not $child.WaitForExit(45000)) { throw 'Inspection pending. Check local UAC; no pass result is available.' }
    if ($child.ExitCode -ne 0) { throw 'Elevated inspection failed. See local sanitized evidence.' }
    $report = Get-Content -LiteralPath (Join-Path $evidenceDirectory 'inspection.json') -Raw | ConvertFrom-Json
    $report | Select-Object Time, Windows, Build, ServiceState, StartMode, Enabled,
        StartWithWindows, ActualStartWithWindows, UnattendedMode, KeepingAwake,
        HasPassword, State, LastSuccess, CampusPulsePowerRequest | Format-List
    exit 0
}

New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
try {
    $service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if ($null -eq $service -or $service.PathName.Trim('"') -ne
        (Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe')) {
        throw 'Unexpected service target'
    }
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(3000)
        $request = [Text.Encoding]::UTF8.GetBytes('{"Command":"status"}' + "`n")
        $pipe.Write($request, 0, $request.Length)
        $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait(5000)) { throw 'Status timeout' }
        $reply = $pending.Result | ConvertFrom-Json
    }
    finally { $pipe.Dispose() }
    if (-not $reply.Success -or $null -eq $reply.Snapshot) { throw 'Status unavailable' }
    $s = $reply.Snapshot
    $data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CampusPulse'
    $permissions = foreach ($path in @($data, (Join-Path $data 'settings.json'),
        (Join-Path $data 'credentials.dat'), (Join-Path $data 'events.json'),
        (Split-Path -Parent $service.PathName.Trim('"')), $service.PathName.Trim('"'))) {
        $item = Get-Item -LiteralPath $path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked target refused' }
        $acl = Get-Acl -LiteralPath $path
        [ordered]@{
            Name = $item.Name
            Protected = $acl.AreAccessRulesProtected
            OwnerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
            Rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object {
                [ordered]@{ Sid = $_.IdentityReference.Value; Rights = $_.FileSystemRights.ToString(); Type = $_.AccessControlType.ToString(); Inherited = $_.IsInherited }
            })
        }
    }
    $os = Get-CimInstance Win32_OperatingSystem
    $requests = & powercfg.exe /requests 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'Power request query failed' }
    $reg = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\CampusPulse'
    $events = @($s.RecentEvents | Sort-Object Time | ForEach-Object {
        [ordered]@{ Time = $_.Time; Message = $_.Message }
    })
    $result = [ordered]@{
        Time = [DateTimeOffset]::Now.ToString('o')
        Windows = $os.Caption; Build = $os.BuildNumber
        ServiceState = $service.State; StartMode = $service.StartMode; Identity = $service.StartName
        DelayedAutoStart = $reg.DelayedAutoStart
        Enabled = $s.Settings.Enabled; StartWithWindows = $s.Settings.StartWithWindows
        ActualStartWithWindows = $s.ActualStartWithWindows
        UnattendedMode = $s.Settings.UnattendedMode; KeepingAwake = $s.KeepingAwake
        HasPassword = $s.HasPassword; State = $s.State; ErrorCode = $s.ErrorCode
        LastCheck = $s.LastCheck; LastSuccess = $s.LastSuccess; NextCheck = $s.NextCheck
        CampusPulsePowerRequest = $requests.Contains('CampusPulse')
        Permissions = @($permissions); Events = $events
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'inspection.json') -Encoding UTF8
    & sc.exe qfailure CampusPulse | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'service-recovery.txt') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw 'Service recovery query failed' }
    Write-Host 'Sanitized read-only evidence saved. No check, authentication, or settings command sent.'
    exit 0
}
catch {
    # Exception messages from I/O are not included in a public-facing report.
    Set-Content -LiteralPath (Join-Path $evidenceDirectory 'inspection-failure.txt') -Value $_.Exception.GetType().FullName -Encoding UTF8
    exit 1
}
