[CmdletBinding()]
param([switch]$Elevate, [switch]$KeepOwnWindow)

# Read only allowlisted controls. Never enumerate account or password values.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $childArgs = @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'))
    if ($KeepOwnWindow) { $childArgs += '-KeepOwnWindow' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList $childArgs
    if (-not $child.WaitForExit(45000)) { throw 'UI inspection pending. Check local UAC.' }
    if ($child.ExitCode -ne 0) { throw 'UI inspection failed. See local sanitized evidence.' }
    Get-Content -LiteralPath (Join-Path $evidence 'ui-inspection.json')
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$owned = $null
$completed = $false
try {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [Windows.Automation.AutomationElement]::RootElement
    function Find-Window {
        # WPF class names contain dynamic IDs. Match the exact product title instead.
        $title = [string]::Concat('CampusPulse ', [char]0x00b7, ' ',
            [char]0x6821, [char]0x56ed, [char]0x7f51, [char]0x81ea, [char]0x52a8, [char]0x8fde, [char]0x63a5)
        $condition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty, $title)
        return $root.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
    }
    $window = Find-Window
    if ($null -eq $window) {
        $app = Join-Path $repo 'src\CampusPulse.App\bin\Release\net10.0-windows\CampusPulse.App.dll'
        # A retained window is for the user's explicit manual acceptance, so it must be visible.
        $style = if ($KeepOwnWindow) { 'Normal' } else { 'Hidden' }
        $owned = Start-Process -FilePath (Join-Path $repo '.tools\dotnet\dotnet.exe') -WindowStyle $style -PassThru -ArgumentList ('"' + $app + '"')
        for ($i = 0; $i -lt 15 -and $null -eq $window; $i++) { Start-Sleep -Milliseconds 500; $window = Find-Window }
    }
    if ($null -eq $window) { throw 'Formal product window not found' }
    Start-Sleep -Seconds 2
    $controls = [ordered]@{}
    foreach ($id in @('StateText', 'ServiceStateText', 'ActualAutostartText', 'AwakeText',
        'PasswordHint', 'UnsavedText', 'StartWithWindowsBox', 'EnabledBox', 'UnattendedBox',
        'SaveButton', 'CheckButton', 'ReconnectButton', 'PauseButton', 'StartServiceButton', 'StopServiceButton')) {
        $condition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
        $element = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $element) { throw 'Required control unavailable' }
        $entry = [ordered]@{ Text = $element.Current.Name; Enabled = $element.Current.IsEnabled }
        if ($id.EndsWith('Box')) {
            $toggle = $element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            $entry.Checked = $toggle.Current.ToggleState.ToString()
        }
        $controls[$id] = $entry
    }
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, 'PasswordInput')
    $password = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $password -or -not $password.Current.IsPassword) { throw 'Password masking unavailable' }
    $result = [ordered]@{ Time = [DateTimeOffset]::Now.ToString('o'); FormalTitle = $true;
        PasswordMasked = $password.Current.IsPassword; Controls = $controls; LaunchedOwnWindow = ($null -ne $owned);
        WindowProcessId = $window.Current.ProcessId; KeptForManualAcceptance = $KeepOwnWindow.IsPresent }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'ui-inspection.json') -Encoding UTF8
    if ($KeepOwnWindow) {
        [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');WindowProcessId=$window.Current.ProcessId;
            ServiceProcessId=(Get-CimInstance Win32_Service -Filter "Name='CampusPulse'").ProcessId;
            SettingsHash=(Get-FileHash (Join-Path $env:ProgramData 'CampusPulse\settings.json')).Hash;
            CredentialHash=(Get-FileHash (Join-Path $env:ProgramData 'CampusPulse\credentials.dat')).Hash} |
            ConvertTo-Json | Set-Content (Join-Path $evidence 'ui-manual-baseline.json') -Encoding UTF8
    }
    $completed = $true
    exit 0
}
catch {
    Set-Content -LiteralPath (Join-Path $evidence 'ui-failure.txt') -Value $_.Exception.GetType().FullName -Encoding UTF8
    exit 1
}
finally {
    # Only end a process created by this inspection; existing user windows are untouched.
    if ($null -ne $owned -and -not $owned.HasExited -and (-not $KeepOwnWindow -or -not $completed)) {
        Stop-Process -Id $owned.Id -ErrorAction SilentlyContinue
    }
}
