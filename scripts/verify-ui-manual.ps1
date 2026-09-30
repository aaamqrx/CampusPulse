[CmdletBinding()]
param([switch]$Elevate)

# Verify after the user operates the tray. Does not operate or stop any interface/service.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile','-File',('"'+$PSCommandPath+'"'))
    if (-not $child.WaitForExit(30000)) { throw 'Verification pending. Check local UAC.' }
    if ($child.ExitCode -ne 0) { throw 'Manual interface verification incomplete' }
    Get-Content (Join-Path $evidence 'ui-manual-after.json')
    exit 0
}
$baseline = Get-Content (Join-Path $evidence 'ui-manual-baseline.json') -Raw | ConvertFrom-Json
$service = Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
$result = [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');
    TestedInterfaceExited=($null -eq (Get-Process -Id $baseline.WindowProcessId -ErrorAction SilentlyContinue));
    SameBackgroundRunning=($service.State -eq 'Running' -and $service.ProcessId -eq $baseline.ServiceProcessId);
    SettingsBytesUnchanged=((Get-FileHash (Join-Path $env:ProgramData 'CampusPulse\settings.json')).Hash -eq $baseline.SettingsHash);
    CredentialBytesUnchanged=((Get-FileHash (Join-Path $env:ProgramData 'CampusPulse\credentials.dat')).Hash -eq $baseline.CredentialHash);
    UserConfirmationRequired=$true}
$result | ConvertTo-Json | Set-Content (Join-Path $evidence 'ui-manual-after.json') -Encoding UTF8
if (-not $result.TestedInterfaceExited -or -not $result.SameBackgroundRunning -or
    -not $result.SettingsBytesUnchanged -or -not $result.CredentialBytesUnchanged) { exit 1 }
exit 0
