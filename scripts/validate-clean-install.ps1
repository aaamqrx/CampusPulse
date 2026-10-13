[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-preview\.\d+)?$')][string]$Version,
    [Parameter(Mandatory)][string]$DeliveryRoot,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedCommit,
    [switch]$AllowUntaggedCandidate
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This script deliberately refuses local developer machines and existing installations.
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Only a disposable GitHub-hosted Windows runner is supported.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator required.' }
$registration = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{DB15D4E6-9CD2-47E0-A4EF-1529703B831A}_is1'
$data = Join-Path $env:ProgramData 'CampusPulse'
$installRoot = Join-Path $env:RUNNER_TEMP 'CampusPulse 安装验收'
$serviceExe = Join-Path $installRoot 'Service\CampusPulse.Service.exe'
$root = [IO.Path]::GetFullPath($DeliveryRoot)
$package = Join-Path $root "installer\CampusPulse-Setup-$Version.exe"
$evidence = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\installation-evidence'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$os = Get-CimInstance Win32_OperatingSystem
$report = [ordered]@{Time=[DateTimeOffset]::UtcNow.ToString('o');Version=$Version;ExpectedCommit=$ExpectedCommit;
    OperatingSystem=$os.Caption;Build=$os.BuildNumber;Checks=@();Failure=$null;Finished=$null;InstallerSha256=$null;
    Boundaries=@('Disposable Windows Server runner, not a clean Windows 11 desktop', 'No reboot or real campus authentication', 'Preinstalled shared runtimes are not removed; loaded private runtime paths are checked')}
function Check([bool]$condition,[string]$name) {
    $report.Checks += @{Name=$name;Passed=$condition}
    if (-not $condition) { throw $name }
}
function Service { Get-CimInstance Win32_Service -Filter "Name='CampusPulse'" }
function Owned-Service {
    $s = Service
    Check ($null -ne $s -and $s.PathName.Trim('"') -eq $serviceExe -and $s.StartName -eq 'LocalSystem') 'Exact product service and LocalSystem identity'
    return $s
}
function Control($request) {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'CampusPulse.Control.v1', [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(10000)
        $bytes = [Text.Encoding]::UTF8.GetBytes(($request | ConvertTo-Json -Depth 6 -Compress) + "`n")
        $pipe.Write($bytes,0,$bytes.Length); $pipe.Flush()
        $reader = [IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
        $pending = $reader.ReadLineAsync()
        if (-not $pending.Wait(10000)) { throw 'Control timeout.' }
        $reply = $pending.Result | ConvertFrom-Json
        Check $reply.Success ('Control accepted: ' + $request.Command)
        return $reply.Snapshot
    } finally { $pipe.Dispose() }
}
function Setup([string]$path,[string]$name,[bool]$expectedSuccess=$true) {
    $p = Start-Process -FilePath $path -WindowStyle Hidden -PassThru -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$installRoot+'"'),('/LOG="'+(Join-Path $evidence ($name+'.log'))+'"'))
    if (-not $p.WaitForExit(120000)) { throw 'Installer pending; do not run a second installer.' }
    Check (($p.ExitCode -eq 0) -eq $expectedSuccess) ($name + ' expected installer exit result')
}
function Assert-Payload {
    foreach ($file in $manifest.payloadFiles) {
        $path = Join-Path $installRoot $file.path
        Check ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -eq $file.length -and
            (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -eq $file.sha256) ('Payload: '+$file.path)
    }
    $reg = Get-ItemProperty -LiteralPath $registration
    Check ($reg.DisplayVersion -eq $Version -and $reg.InstallLocation.TrimEnd('\') -eq $installRoot) 'Expected product registration and Unicode path'
}
function Assert-Defaults {
    $s = Owned-Service
    Check ($s.State -eq 'Running' -and $s.StartMode -eq 'Auto') 'New install starts its automatic service'
    $snapshot = Control @{Command='status'}
    Check (-not $snapshot.Settings.Enabled -and $snapshot.Settings.StartWithWindows -and -not $snapshot.Settings.UnattendedMode) 'Three first-install defaults'
    Check (-not $snapshot.HasPassword -and -not $snapshot.Diagnostics.LastSubmissionAt) 'No credentials or authentication submission'
    return $snapshot
}
function Uninstall([string]$name) {
    $null = Owned-Service
    $uninstaller = Join-Path $installRoot 'unins000.exe'
    Check (Test-Path -LiteralPath $uninstaller) 'Owned uninstaller exists'
    Setup $uninstaller $name
    for ($i=0;$i -lt 30 -and $null -ne (Service);$i++) { Start-Sleep -Milliseconds 300 }
    Check ($null -eq (Service)) 'Uninstall removes service'
    Check (-not (Test-Path -LiteralPath $registration)) 'Uninstall removes product registration'
    foreach ($file in @('settings.json','credentials.dat','events.json')) {
        Check (-not (Test-Path -LiteralPath (Join-Path $data $file))) ('Uninstall removes '+$file)
    }
    Check (-not (Test-Path -LiteralPath $serviceExe)) 'Uninstall removes service program'
    Check (@(Get-Process -Name CampusPulse.Service,CampusPulse.App -ErrorAction SilentlyContinue).Count -eq 0) 'Uninstall leaves no product process'
    foreach ($folder in @([Environment]::GetFolderPath('CommonDesktopDirectory'),[Environment]::GetFolderPath('CommonPrograms'))) {
        Check (-not (Test-Path -LiteralPath (Join-Path $folder 'CampusPulse.lnk'))) 'Uninstall removes shortcut'
    }
}
$ownedApp = $null
try {
    Check ($null -eq (Service) -and -not (Test-Path -LiteralPath $registration) -and -not (Test-Path -LiteralPath $data) -and -not (Test-Path -LiteralPath $installRoot)) 'Fresh runner has no CampusPulse service, product, data or test directory'
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -Version $Version -Root $root -ExpectedCommit $ExpectedCommit -AllowUntaggedCandidate:$AllowUntaggedCandidate
    $manifest = Get-Content -LiteralPath (Join-Path $root 'build-manifest.json') -Raw | ConvertFrom-Json
    $report.InstallerSha256 = $manifest.sha256
    Setup $package 'fresh-install'
    Assert-Payload
    $null = Assert-Defaults
    $s = Owned-Service
    $privateRuntime = @((Get-Process -Id $s.ProcessId).Modules | Where-Object ModuleName -EQ 'coreclr.dll')
    Check ($privateRuntime.Count -eq 1 -and $privateRuntime[0].FileName -eq (Join-Path $installRoot 'Service\coreclr.dll')) 'Service loads bundled runtime'
    $ownedApp = Start-Process -FilePath (Join-Path $installRoot 'App\CampusPulse.App.exe') -WindowStyle Hidden -PassThru -ArgumentList '--smoke-test'
    Start-Sleep -Seconds 3
    Check (-not $ownedApp.HasExited -and $ownedApp.Responding) 'Self-contained WPF demo runs on disposable runner'
    $appRuntime = @((Get-Process -Id $ownedApp.Id).Modules | Where-Object ModuleName -EQ 'coreclr.dll')
    Check ($appRuntime.Count -eq 1 -and $appRuntime[0].FileName -eq (Join-Path $installRoot 'App\coreclr.dll')) 'WPF loads bundled runtime'
    Stop-Process -Id $ownedApp.Id; $ownedApp = $null
    Setup $package 'same-version-reinstall'
    Assert-Payload
    $null = Assert-Defaults
    $null = Owned-Service
    Restart-Service CampusPulse
    (Get-Service CampusPulse).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $null = Assert-Defaults
    Set-Content -LiteralPath (Join-Path $installRoot 'unrelated-sentinel.txt') -Value 'Preserve unrelated user files' -Encoding UTF8
    Uninstall 'fresh-uninstall'
    Check ((Get-Content -LiteralPath (Join-Path $installRoot 'unrelated-sentinel.txt') -Raw).Trim() -eq 'Preserve unrelated user files') 'Uninstall preserves unrelated file'

    # Exercise migration with fake credentials only and automatic authentication disabled.
    $previous = Join-Path $env:RUNNER_TEMP 'CampusPulse-Setup-0.1.0-preview.3.exe'
    Invoke-WebRequest 'https://github.com/aaamqrx/CampusPulse/releases/download/v0.1.0-preview.3/CampusPulse-Setup-0.1.0-preview.3.exe' -OutFile $previous -TimeoutSec 120
    Check ((Get-FileHash -LiteralPath $previous).Hash.ToLowerInvariant() -eq '0d17c33d3374407cc5f631eb3f23ff616d2828895f2a31aaa4e8f0674f754142') 'Published preview.3 rollback/upgrade baseline hash'
    Setup $previous 'previous-preview-install'
    $baseline = Assert-Defaults
    $baseline.Settings.Username = 'campuspulse-ci-fake'
    $baseline.Settings.StartWithWindows = $false
    $fake = 'CAMPUSPULSE-CI-FAKE-PASSWORD-NOT-A-REAL-ACCOUNT'
    $null = Control @{Command='save';Settings=$baseline.Settings;Password=$fake}
    $credentialHash = (Get-FileHash -LiteralPath (Join-Path $data 'credentials.dat')).Hash
    $settingsBefore = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
    $null = Owned-Service
    Stop-Service CampusPulse
    Setup $package 'preview-to-stable-upgrade'
    Assert-Payload
    $s = Owned-Service
    Check ($s.State -eq 'Stopped' -and $s.StartMode -eq 'Manual') 'Upgrade preserves stopped service and disabled auto-start'
    Check ((Get-FileHash -LiteralPath (Join-Path $data 'credentials.dat')).Hash -eq $credentialHash) 'Upgrade retains encrypted fake credentials'
    $settingsAfter = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
    foreach ($field in @('Username','PortalUrl','Carrier','Enabled','StartWithWindows','UnattendedMode')) {
        Check ($settingsAfter.$field -eq $settingsBefore.$field) ('Upgrade preserves '+$field)
    }
    Start-Service CampusPulse
    $snapshot = Control @{Command='status'}
    Check ($snapshot.HasPassword -and -not $snapshot.Settings.Enabled -and -not $snapshot.Diagnostics.LastSubmissionAt) 'Upgraded service reads fake credentials without submitting authentication'
    foreach ($file in @('settings.json','events.json')) {
        if (Test-Path -LiteralPath (Join-Path $data $file)) {
            Check (-not ([IO.File]::ReadAllText((Join-Path $data $file)).Contains($fake))) ('No fake password plaintext in '+$file)
        }
    }
    Uninstall 'upgraded-uninstall'
    Setup $package 'reinstall-after-uninstall'
    $null = Assert-Defaults
    Uninstall 'final-uninstall'

    # Failure path: an unrelated service must be rejected before any files are installed.
    $foreignPath = '"' + (Join-Path $env:SystemRoot 'System32\cmd.exe') + '" /c exit'
    & sc.exe create CampusPulse binPath= $foreignPath start= demand | Out-Null
    Check ($LASTEXITCODE -eq 0) 'Create disposable foreign-service fixture'
    try {
        Setup $package 'foreign-service-refused' $false
        $foreign = Service
        Check ($null -ne $foreign -and $foreign.PathName -eq $foreignPath -and $foreign.State -eq 'Stopped') 'Installer leaves foreign service unchanged'
        Check (-not (Test-Path -LiteralPath $serviceExe) -and -not (Test-Path -LiteralPath $registration)) 'Rejected installation leaves no product files or registration'
    } finally {
        $foreign = Service
        if ($null -ne $foreign -and $foreign.PathName -eq $foreignPath -and $foreign.State -eq 'Stopped') {
            & sc.exe delete CampusPulse | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Foreign fixture cleanup failed.' }
        }
    }
} catch { $report.Failure = $_.Exception.Message }
finally {
    if ($ownedApp -and -not $ownedApp.HasExited) { Stop-Process -Id $ownedApp.Id }
    $report.Finished = [DateTimeOffset]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'installation.json') -Encoding UTF8
}
[ordered]@{Checks=$report.Checks.Count;Passed=@($report.Checks | Where-Object Passed).Count;Failure=$report.Failure;OperatingSystem=$report.OperatingSystem;InstallerSha256=$report.InstallerSha256} | ConvertTo-Json
if ($report.Failure) { exit 1 }
