[CmdletBinding()]
param([switch]$Elevate)

# Read-only OS report for the accepted idle check. Raw machine data stays ignored locally.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile','-File',('"'+$PSCommandPath+'"'))
    Write-Host ('Read-only report dispatched; PID={0}. Check result file for completion.' -f $child.Id)
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$failure = $null; $code = $null
try {
    if (-not (Test-Path (Join-Path $evidence 'idle-result.json'))) { throw 'Completed idle evidence required' }
    $report = Join-Path $evidence 'idle-sleepstudy.xml'
    & powercfg.exe /sleepstudy /duration 1 /xml /output $report 2>&1 | Out-File (Join-Path $evidence 'idle-sleepstudy-command.txt') -Encoding UTF8
    $code = $LASTEXITCODE
    if ($code -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'OS report not available' }
    # Do not print the raw report: it may contain machine names and unrelated app history.
    [xml]$xml = Get-Content -LiteralPath $report -Raw
    [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');ExitCode=$code;Failure=$null;
        ReportCreated=$true;RootElement=$xml.DocumentElement.LocalName;
        ChildElementNames=@($xml.DocumentElement.ChildNodes | Where-Object {$_.NodeType -eq 'Element'} |
            Select-Object -ExpandProperty LocalName -Unique);IdleVerdict='Pending session interpretation'} |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'idle-sleepstudy-result.json') -Encoding UTF8
} catch {
    $failure=$_.Exception.GetType().FullName
    [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');ExitCode=$code;Failure=$failure;ReportCreated=$false} |
        ConvertTo-Json | Set-Content (Join-Path $evidence 'idle-sleepstudy-result.json') -Encoding UTF8
}
if ($null -ne $failure) { exit 1 }
exit 0
