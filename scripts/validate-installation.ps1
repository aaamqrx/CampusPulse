[CmdletBinding()]
param([ValidateSet('Preflight', 'Lifecycle', 'Restore')][string]$Phase = 'Preflight', [switch]$Execute, [switch]$Elevate)

# Fixed local acceptance only. Never print settings, credentials, or full replies.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$evidence = Join-Path $repo '.local\acceptance-20260930'
$data = Join-Path $env:ProgramData 'CampusPulse'
$backup = Join-Path $env:ProgramData 'CampusPulse-AcceptanceBackup-20260930'
$appRoot = Join-Path $env:ProgramFiles 'CampusPulse'
$productExe = Join-Path $appRoot 'Service\CampusPulse.Service.exe'
$validationExe = Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe'
$installer = Join-Path $repo 'artifacts\installer\CampusPulse-Setup-0.1.0-preview.1.exe'
$baseline = Join-Path $evidence 'upgrade-baseline\CampusPulse-Setup-0.1.0-preview.0.exe'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DB15D4E6-9CD2-47E0-A4EF-1529703B831A}_is1'
$shortcuts = @((Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'CampusPulse.lnk'),
    (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'CampusPulse.lnk'))
$resultPath = Join-Path $evidence ('installer-' + $Phase.ToLowerInvariant() + '.json')
if (-not $Execute) {
    Write-Host 'Preflight: read fixed targets and test refusal of the existing validation service.'
    Write-Host 'Lifecycle: protected opaque backup, pause, remove marked validation service, install test baseline, upgrade, repeat, uninstall, reinstall final, restore original data/settings.'
    Write-Host 'No reboot, network disconnection, power plan change, or manual authentication. Clean Windows and old-code migration remain untested.'
    Write-Host 'Restore: recover the fixed protected backup after an interrupted lifecycle; never decrypt credentials.'
    exit 0
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required; use -Elevate for Windows UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Phase', $Phase, '-Execute')
    if (-not $child.WaitForExit(45000)) { Write-Host 'Acceptance still running. Read the timestamped result file after completion.'; exit 2 }
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath }
    exit $child.ExitCode
}

$report = [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); Phase = $Phase; Assertions = @(); Failure = $null; OriginalDataRestored = $false; Finished = $null }
$mutationStarted = $false
$installerPending = $false
$ownedUi = $null
$ownedSentinel = $false
function Assert-Check([bool]$Condition, [string]$Name) {
    $report.Assertions += [ordered]@{ Name = $Name; Passed = $Condition }
    if (-not $Condition) { throw [InvalidOperationException]::new($Name) }
}
function Assert-SafePath([string]$Path, [string]$Expected) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $full.Equals([IO.Path]::GetFullPath($Expected).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected target' }
    $current = $full
    while ($current) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked target refused' }
        $current = Split-Path -Parent $current
    }
}
function Get-ServiceRecord { Get-CimInstance Win32_Service -Filter "Name='CampusPulse'" }
function Assert-ServicePath([string]$Expected) {
    $service = Get-ServiceRecord
    if ($null -eq $service -or -not $service.PathName.Trim('"').Equals($Expected, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected service target' }
    return $service
}
function Send-Control($Request) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(5000)
        $bytes = [Text.Encoding]::UTF8.GetBytes(($Request | ConvertTo-Json -Depth 6 -Compress) + "`n")
        $pipe.Write($bytes, 0, $bytes.Length); $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe, [Text.Encoding]::UTF8)
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait(8000)) { throw 'Control timeout' }
        $reply = $pending.Result | ConvertFrom-Json
        if (-not $reply.Success) { throw 'Control command refused' }
        return $reply.Snapshot
    }
    finally { $pipe.Dispose() }
}
function Get-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Run-Setup([string]$Executable, [string]$LogName) {
    Assert-SafePath $Executable $Executable
    $logPath = Join-Path $evidence $LogName
    $process = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/LOG="' + $logPath + '"'))
    if (-not $process.WaitForExit(120000)) { $script:installerPending = $true; throw 'Installer pending; do not race restoration against it' }
    return $process.ExitCode
}
function Stop-FixedService([string]$Expected) {
    $service = Assert-ServicePath $Expected
    if ($service.State -ne 'Stopped') {
        Stop-Service -Name CampusPulse
        (Get-Service CampusPulse).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
}
function Protect-Backup {
    if (Test-Path -LiteralPath $backup) { throw 'Existing backup refused; review prior run before retrying' }
    New-Item -ItemType Directory -Path $backup | Out-Null
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
        $sid = [Security.Principal.SecurityIdentifier]::new($sidText)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
            [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow))
    }
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    Set-Acl -LiteralPath $backup -AclObject $acl
    $readback = Get-Acl -LiteralPath $backup
    $rules = @($readback.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    Assert-Check ($readback.AreAccessRulesProtected -and $rules.Count -eq 2 -and
        @($rules | Where-Object { $_.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544') }).Count -eq 0) 'Backup restricted to SYSTEM and administrators'
    Set-Content -LiteralPath (Join-Path $backup '.campuspulse-acceptance-backup') -Value 'CampusPulse original-data backup v1' -Encoding ASCII
}
function Remove-ProductDataFiles {
    Assert-SafePath $data (Join-Path $env:ProgramData 'CampusPulse')
    foreach ($name in @('settings.json', 'credentials.dat', 'events.json')) {
        $path = Join-Path $data $name
        Assert-SafePath $path (Join-Path $data $name)
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}
function Restore-Original {
    if ($installerPending) { throw 'Installer still pending; protected backup retained' }
    Assert-SafePath $backup (Join-Path $env:ProgramData 'CampusPulse-AcceptanceBackup-20260930')
    if ((Get-Content -LiteralPath (Join-Path $backup '.campuspulse-acceptance-backup') -Raw).Trim() -ne 'CampusPulse original-data backup v1') { throw 'Unexpected backup marker' }
    $service = Get-ServiceRecord
    if ($null -eq $service) {
        $code = Run-Setup $installer 'install-restore-final.log'
        if ($code -ne 0) { throw 'Final reinstall failed; protected backup retained' }
        $service = Assert-ServicePath $productExe
    }
    $expected = $service.PathName.Trim('"')
    if ($expected -notin @($productExe, $validationExe)) { throw 'Unexpected restoration service' }
    Stop-FixedService $expected
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    Assert-SafePath $data (Join-Path $env:ProgramData 'CampusPulse')
    foreach ($name in @('settings.json', 'credentials.dat', 'events.json')) {
        $saved = Join-Path $backup $name
        $target = Join-Path $data $name
        Assert-SafePath $saved (Join-Path $backup $name)
        Assert-SafePath $target (Join-Path $data $name)
        if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    & $expected --initialize-store | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Protected store initialization failed' }
    Assert-Check ((Get-Hash (Join-Path $data 'settings.json')) -eq (Get-Hash (Join-Path $backup 'settings.json'))) 'Original settings bytes restored before service start'
    Assert-Check ((Get-Hash (Join-Path $data 'credentials.dat')) -eq (Get-Hash (Join-Path $backup 'credentials.dat'))) 'Original encrypted credential bytes restored'
    $savedSettings = Get-Content -LiteralPath (Join-Path $backup 'settings.json') -Raw | ConvertFrom-Json
    $startupType = if ($savedSettings.StartWithWindows) { 'delayed-auto' } else { 'demand' }
    & sc.exe config CampusPulse start= $startupType | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Startup restoration failed' }
    Start-Service CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $snapshot = Send-Control @{ Command = 'status' }
    Assert-Check ($snapshot.HasPassword -and $snapshot.Settings.Enabled -eq $savedSettings.Enabled -and
        $snapshot.Settings.StartWithWindows -eq $savedSettings.StartWithWindows -and
        $snapshot.Settings.UnattendedMode -eq $savedSettings.UnattendedMode) 'Original three switches and credential availability restored'
    $report.OriginalDataRestored = $true
}

try {
    foreach ($path in @($data, $backup, $appRoot, $installer, $baseline)) { Assert-SafePath $path $path }
    $manifest = Get-Content -LiteralPath (Join-Path $repo 'artifacts\build-manifest.json') -Raw | ConvertFrom-Json
    Assert-Check ((Get-Hash $installer).ToLowerInvariant() -eq $manifest.sha256) 'New final candidate matches build manifest'
    $report.InstallerSha256 = $manifest.sha256
    if ($Phase -eq 'Restore') { Restore-Original }
    else {
        $service = Assert-ServicePath $validationExe
        Assert-Check ($service.State -eq 'Running') 'Fixed marked validation service running before test'
        Assert-Check (-not (Test-Path -LiteralPath $uninstallKey) -and -not (Test-Path -LiteralPath $appRoot)) 'No existing product installation to overwrite'
    }
    if ($Phase -eq 'Preflight') {
        $settingsHash = Get-Hash (Join-Path $data 'settings.json')
        $credentialHash = Get-Hash (Join-Path $data 'credentials.dat')
        $code = Run-Setup $installer 'install-rejected-validation-path.log'
        $after = Assert-ServicePath $validationExe
        Assert-Check ($code -ne 0) 'Installer refuses same-name service at validation path'
        Assert-Check ($after.State -eq 'Running' -and $after.ProcessId -eq $service.ProcessId) 'Refusal leaves original service process running'
        Assert-Check ((Get-Hash (Join-Path $data 'settings.json')) -eq $settingsHash -and
            (Get-Hash (Join-Path $data 'credentials.dat')) -eq $credentialHash) 'Refusal preserves configuration and encrypted credentials'
        Assert-Check (-not (Test-Path -LiteralPath $uninstallKey) -and -not (Test-Path -LiteralPath (Join-Path $appRoot 'Service\CampusPulse.Service.exe'))) 'Refusal creates no installed product or service binary'
        $report.OriginalDataRestored = $true
    }
    elseif ($Phase -eq 'Lifecycle') {
        $preflight = Get-Content -LiteralPath (Join-Path $evidence 'installer-preflight.json') -Raw | ConvertFrom-Json
        Assert-Check ($null -eq $preflight.Failure -and $null -ne $preflight.Finished -and
            $preflight.InstallerSha256 -eq $manifest.sha256) 'Prior collision preflight completed for this exact candidate'
        if (-not (Test-Path -LiteralPath $baseline)) { throw 'Synthetic upgrade baseline missing' }
        $powerBefore = (& powercfg.exe /getactivescheme | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Power plan read failed' }
        Protect-Backup
        foreach ($name in @('settings.json', 'credentials.dat')) {
            Assert-SafePath (Join-Path $data $name) (Join-Path $data $name)
            Copy-Item -LiteralPath (Join-Path $data $name) -Destination (Join-Path $backup $name)
            Assert-Check ((Get-Hash (Join-Path $data $name)) -eq (Get-Hash (Join-Path $backup $name))) ('Protected backup verified: ' + $name)
        }
        $eventPath = Join-Path $data 'events.json'
        Assert-SafePath $eventPath (Join-Path $data 'events.json')
        if (Test-Path -LiteralPath $eventPath) { Copy-Item -LiteralPath $eventPath -Destination (Join-Path $backup 'events.json') }
        $mutationStarted = $true
        $snapshot = Send-Control @{ Command = 'pause' }
        Assert-Check (-not $snapshot.Settings.Enabled) 'Automatic authentication paused before lifecycle'
        Stop-FixedService $validationExe
        if (Test-Path -LiteralPath (Join-Path $data 'events.json')) { Copy-Item -LiteralPath (Join-Path $data 'events.json') -Destination (Join-Path $backup 'events.json') -Force }
        & (Join-Path $repo 'scripts\dev-service-validation.ps1') -Action Remove *> (Join-Path $evidence 'remove-marked-validation.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Validation service removal failed' }
        Assert-Check ($null -eq (Get-ServiceRecord)) 'Only marked validation service removed'
        Remove-ProductDataFiles
        Assert-Check ((Run-Setup $baseline 'install-test-baseline.log') -eq 0) 'Synthetic preview.0 baseline installed'
        $installed = Assert-ServicePath $productExe
        $snapshot = Send-Control @{ Command = 'status' }
        Assert-Check ($installed.State -eq 'Running' -and $installed.StartMode -eq 'Auto' -and
            -not $snapshot.Settings.Enabled -and $snapshot.Settings.StartWithWindows -and
            -not $snapshot.Settings.UnattendedMode -and -not $snapshot.HasPassword) 'Fresh product defaults: automatic startup, authentication and unattended off, no credential'
        Assert-Check (@($shortcuts | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -eq 0) 'Installer creates product desktop and Start menu shortcuts'
        $testSettings = $snapshot.Settings
        $testSettings.Username = 'CampusPulseAcceptanceFake'
        $testSettings.StartWithWindows = $false
        $testSettings.Enabled = $false
        $testSettings.UnattendedMode = $true
        $snapshot = Send-Control @{ Command = 'save'; Settings = $testSettings; Password = 'FAKE-ONLY-INSTALLATION+913' }
        $settingsHash = Get-Hash (Join-Path $data 'settings.json')
        $credentialHash = Get-Hash (Join-Path $data 'credentials.dat')
        Assert-Check ((Run-Setup $installer 'upgrade-to-final.log') -eq 0) 'New final candidate upgrades synthetic preview.0 baseline'
        $upgraded = Assert-ServicePath $productExe
        $snapshot = Send-Control @{ Command = 'status' }
        Assert-Check ($upgraded.State -eq 'Running' -and $upgraded.StartMode -eq 'Manual' -and
            $snapshot.HasPassword -and -not $snapshot.Settings.Enabled -and
            -not $snapshot.Settings.StartWithWindows -and $snapshot.Settings.UnattendedMode -and
            (Get-Hash (Join-Path $data 'settings.json')) -eq $settingsHash -and
            (Get-Hash (Join-Path $data 'credentials.dat')) -eq $credentialHash) 'Upgrade preserves independent switches, manual startup and encrypted fake credential'
        foreach ($component in @('App', 'Service')) {
            foreach ($name in @("CampusPulse.$component.exe", "CampusPulse.$component.dll", 'CampusPulse.Core.dll')) {
                Assert-Check ((Get-Hash (Join-Path $appRoot "$component\$name")) -eq
                    (Get-Hash (Join-Path $repo "artifacts\publish\$component\$name"))) ('Installed payload matches final build: ' + $component + '/' + $name)
            }
        }
        Assert-Check ((Run-Setup $installer 'repeat-final-install.log') -eq 0) 'Same-version final reinstall succeeds'
        Assert-Check ((Get-Hash (Join-Path $data 'settings.json')) -eq $settingsHash -and
            (Get-Hash (Join-Path $data 'credentials.dat')) -eq $credentialHash) 'Same-version reinstall preserves settings and encrypted fake credential'
        $appExe = Join-Path $appRoot 'App\CampusPulse.App.exe'
        $ownedUi = Start-Process -FilePath $appExe -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 3
        & (Join-Path $repo 'scripts\inspect-ui.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Installed native UI inspection failed' }
        Copy-Item -LiteralPath (Join-Path $evidence 'ui-inspection.json') -Destination (Join-Path $evidence 'ui-installed-final.json') -Force
        $ui = Get-Content -LiteralPath (Join-Path $evidence 'ui-installed-final.json') -Raw | ConvertFrom-Json
        Assert-Check ($ui.WindowProcessId -eq $ownedUi.Id -and $ui.PasswordMasked) 'Installed self-contained interface responds with masked password input'
        Stop-Process -Id $ownedUi.Id
        $ownedUi = $null
        $sentinel = Join-Path $data 'acceptance-scope-check.keep'
        Assert-SafePath $sentinel (Join-Path $data 'acceptance-scope-check.keep')
        if (Test-Path -LiteralPath $sentinel) { throw 'Existing unrelated sentinel refused' }
        Set-Content -LiteralPath $sentinel -Value 'Created only by CampusPulse local acceptance' -Encoding ASCII
        $ownedSentinel = $true
        $uninstaller = Join-Path $appRoot 'unins000.exe'
        Assert-Check ((Run-Setup $uninstaller 'uninstall-final.log') -eq 0) 'Actual final uninstaller exits successfully'
        Start-Sleep -Seconds 3
        Assert-Check ($null -eq (Get-ServiceRecord) -and -not (Test-Path -LiteralPath $uninstallKey)) 'Uninstall removes service and uninstall registration'
        Assert-Check (@($shortcuts | Where-Object { Test-Path -LiteralPath $_ }).Count -eq 0 -and
            -not (Test-Path -LiteralPath (Join-Path $appRoot 'App\CampusPulse.App.exe')) -and
            -not (Test-Path -LiteralPath $productExe)) 'Uninstall removes installed application, service binaries and product shortcuts'
        Assert-Check (@('settings.json', 'credentials.dat', 'events.json' | Where-Object { Test-Path -LiteralPath (Join-Path $data $_) }).Count -eq 0) 'Uninstall removes only known product settings, credential and events'
        Assert-Check (Test-Path -LiteralPath $sentinel) 'Uninstall preserves unrelated data file'
        Remove-Item -LiteralPath $sentinel -Force
        $ownedSentinel = $false
        $requests = & powercfg.exe /requests | Out-String
        Assert-Check ($LASTEXITCODE -eq 0 -and -not $requests.Contains('CampusPulse')) 'Uninstall releases CampusPulse power request'
        & (Join-Path $repo 'scripts\inspect-ui.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Disconnected UI inspection failed' }
        Copy-Item -LiteralPath (Join-Path $evidence 'ui-inspection.json') -Destination (Join-Path $evidence 'ui-disconnected-password-fix.json') -Force
        $report.DisconnectedPasswordHint = (Get-Content -LiteralPath (Join-Path $evidence 'ui-inspection.json') -Raw | ConvertFrom-Json).Controls.PasswordHint.Text
        Assert-Check ((Run-Setup $installer 'reinstall-fresh-final.log') -eq 0) 'Final package installs again after uninstall'
        $snapshot = Send-Control @{ Command = 'status' }
        Assert-Check (-not $snapshot.HasPassword -and -not $snapshot.Settings.Enabled -and
            $snapshot.Settings.StartWithWindows -and -not $snapshot.Settings.UnattendedMode) 'Reinstall has fresh defaults and does not recover deleted fake credentials'
        Restore-Original
        $powerAfter = (& powercfg.exe /getactivescheme | Out-String).Trim()
        Assert-Check ($LASTEXITCODE -eq 0 -and $powerAfter -eq $powerBefore) 'Original Windows power plan unchanged'
        $report.UpgradeLimit = 'Synthetic installer preview.0 uses current payload; old-code/schema migration and clean Windows are not covered.'
        $report.BackupLocation = $backup
    }
}
catch { $report.Failure = $_.Exception.GetType().FullName }
finally {
    if ($null -ne $ownedUi -and -not $ownedUi.HasExited) { Stop-Process -Id $ownedUi.Id -ErrorAction SilentlyContinue }
    if ($ownedSentinel -and -not $installerPending) {
        try {
            Assert-SafePath $sentinel (Join-Path $data 'acceptance-scope-check.keep')
            if ((Get-Content -LiteralPath $sentinel -Raw).Trim() -eq 'Created only by CampusPulse local acceptance') { Remove-Item -LiteralPath $sentinel -Force }
        }
        catch { $report.SentinelCleanupFailure = $_.Exception.GetType().FullName }
    }
    if ($mutationStarted -and -not $report.OriginalDataRestored) {
        try { Restore-Original }
        catch { $report.RestorationFailure = $_.Exception.GetType().FullName }
    }
    $report.Finished = [DateTimeOffset]::Now.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if ($null -ne $report.Failure -or -not $report.OriginalDataRestored) { exit 1 }
exit 0
