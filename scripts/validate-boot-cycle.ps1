[CmdletBinding()]
param([ValidateSet('Disable','AfterManualBoot','AfterManualStart','Restore')][string]$Phase='Disable',
    [switch]$Execute, [switch]$Elevate)

# The user reboots and clicks Start in the formal interface. Never reboot or start the service here.
$ErrorActionPreference='Stop'
if ($Phase -in @('Disable','Restore') -and -not $Execute) {
    Write-Host 'Plan: Disable saves original switch/hash/boot evidence, changes only autostart to Manual, and keeps the service running.'
    Write-Host 'AfterManualBoot reads the new boot before anyone starts the background. AfterManualStart verifies user-initiated startup.'
    Write-Host 'Restore returns original switches and records a new baseline for a user reboot with a 3-minute sign-in screen wait.'
    exit 0
}
$repo=Split-Path -Parent $PSScriptRoot
$evidence=Join-Path $repo '.local\acceptance-20260930'
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) {throw 'Administrator required. Use -Elevate for local UAC.'}
    $arguments=@('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Phase',$Phase)
    if ($Execute) {$arguments+='-Execute'}
    $child=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $arguments
    if (-not $child.WaitForExit(45000)) {throw 'Boot cycle step pending. Check local UAC.'}
    if ($child.ExitCode -ne 0) {throw 'Boot cycle step incomplete. See local evidence.'}
    Get-Content (Join-Path $evidence ('boot-cycle-'+$Phase.ToLowerInvariant()+'.json'))
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
function Send([hashtable]$Request) {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','CampusPulse.Control.v1',[IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(2000)
        $bytes=[Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Compress -Depth 6)+"`n")
        $pipe.Write($bytes,0,$bytes.Length); $pipe.Flush()
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8); $pending=$reader.ReadLineAsync()
        if (-not $pending.Wait(4000)) {throw 'Status/save timeout'}
        $reply=$pending.Result | ConvertFrom-Json
        if (-not $reply.Success -or $null -eq $reply.Snapshot) {throw 'Status/save unavailable'}
        return $reply.Snapshot
    } finally {$pipe.Dispose()}
}
function Identity-Hash($Settings) {
    $bytes=[Text.Encoding]::UTF8.GetBytes((@($Settings.Username,$Settings.Carrier,$Settings.PortalUrl)|ConvertTo-Json -Compress))
    $sha=[Security.Cryptography.SHA256]::Create()
    try {return [Convert]::ToBase64String($sha.ComputeHash($bytes))} finally {$sha.Dispose()}
}
$failure=$null; $result=[ordered]@{Time=[DateTimeOffset]::Now.ToString('o');Phase=$Phase;Failure=$null;Passed=$false}
try {
    $service=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    if ($null -eq $service -or $service.PathName.Trim('"') -ne (Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe')) {throw 'Unexpected service target'}
    $data=Join-Path $env:ProgramData 'CampusPulse'; $credential=Join-Path $data 'credentials.dat'
    $baselinePath=Join-Path $evidence 'boot-cycle-original.json'
    if ($Phase -eq 'Disable') {
        if (Test-Path $baselinePath) {throw 'Existing boot cycle must be finished or reviewed before a new one'}
        $s=Send @{Command='status'}
        if ($service.State -ne 'Running' -or -not $s.HasPassword -or -not $s.Settings.StartWithWindows) {throw 'Expected running autostart baseline required'}
        [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');BootTime=(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToString('o');
            StartWithWindows=$s.Settings.StartWithWindows;Enabled=$s.Settings.Enabled;UnattendedMode=$s.Settings.UnattendedMode;
            IdentityHash=(Identity-Hash $s.Settings);CredentialHash=(Get-FileHash $credential).Hash} |
            ConvertTo-Json | Set-Content $baselinePath -Encoding UTF8
        foreach ($name in @('boot-before.json','boot-after.json')) {
            $saved=Join-Path $evidence ('initial-'+$name)
            if ((Test-Path (Join-Path $evidence $name)) -and -not (Test-Path $saved)) {Copy-Item -LiteralPath (Join-Path $evidence $name) -Destination $saved}
        }
        $s.Settings.StartWithWindows=$false
        $s=Send @{Command='save';Settings=$s.Settings}
        & (Join-Path $PSScriptRoot 'inspect-boot.ps1') -Phase Before
        Copy-Item (Join-Path $evidence 'boot-before.json') (Join-Path $evidence 'boot-disabled-before.json')
        $service=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
        $result.Passed=-not $s.Settings.StartWithWindows -and -not $s.ActualStartWithWindows -and $service.StartMode -eq 'Manual' -and $service.State -eq 'Running'
    } else {
        $original=Get-Content $baselinePath -Raw | ConvertFrom-Json
        if ($Phase -eq 'AfterManualBoot') {
            & (Join-Path $PSScriptRoot 'inspect-boot.ps1') -Phase After
            Copy-Item (Join-Path $evidence 'boot-after.json') (Join-Path $evidence 'boot-disabled-after.json')
            $boot=Get-Content (Join-Path $evidence 'boot-after.json') -Raw | ConvertFrom-Json
            $settings=Get-Content (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
            $result.NewBootObserved=$boot.NewBootObserved
            $result.Passed=$boot.NewBootObserved -and $service.State -eq 'Stopped' -and $service.StartMode -eq 'Manual' -and -not $settings.StartWithWindows
        } else {
            $s=Send @{Command='status'}
            if ((Identity-Hash $s.Settings) -ne $original.IdentityHash) {throw 'Concurrent identity change; refuse switch restoration'}
            if ($Phase -eq 'AfterManualStart') {
                $result.Passed=$service.State -eq 'Running' -and $service.StartMode -eq 'Manual' -and
                    -not $s.Settings.StartWithWindows -and -not $s.ActualStartWithWindows -and $s.HasPassword
            } else {
                $s.Settings.StartWithWindows=$original.StartWithWindows
                $s.Settings.Enabled=$original.Enabled; $s.Settings.UnattendedMode=$original.UnattendedMode
                $s=Send @{Command='save';Settings=$s.Settings}
                & (Join-Path $PSScriptRoot 'inspect-boot.ps1') -Phase Before
                Copy-Item (Join-Path $evidence 'boot-before.json') (Join-Path $evidence 'boot-auto-before.json')
                $service=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
                $result.Passed=$service.State -eq 'Running' -and $s.Settings.StartWithWindows -eq $original.StartWithWindows -and
                    $s.ActualStartWithWindows -eq $original.StartWithWindows -and $s.HasPassword
            }
            $result.Enabled=$s.Settings.Enabled; $result.UnattendedMode=$s.Settings.UnattendedMode
        }
        $result.CredentialBytesUnchanged=(Get-FileHash $credential).Hash -eq $original.CredentialHash
        $result.Passed=$result.Passed -and $result.CredentialBytesUnchanged
    }
    $result.ServiceState=$service.State; $result.StartMode=$service.StartMode
    if (-not $result.Passed) {throw 'Boot cycle assertion failed'}
} catch {$failure=$_.Exception.GetType().FullName; $result.Failure=$failure; $result.Passed=$false}
$result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence ('boot-cycle-'+$Phase.ToLowerInvariant()+'.json')) -Encoding UTF8
if ($null -ne $failure) {exit 1}
exit 0
