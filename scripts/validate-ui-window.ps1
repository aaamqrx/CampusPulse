[CmdletBinding()]
param([switch]$Elevate)

# Native window lifecycle only. Never save settings, inspect input values, or authenticate.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', ('"' + $PSCommandPath + '"'))
    if (-not $child.WaitForExit(45000)) { throw 'Window inspection pending. Check local UAC.' }
    if ($child.ExitCode -ne 0) { throw 'Window inspection failed. See local sanitized evidence.' }
    Get-Content -LiteralPath (Join-Path $evidence 'ui-window-validation.json')
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProductWindowNative {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr handle);
 [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@
$owned = $null; $failure = $null; $failureLine = $null; $failureCause = $null; $phase = 'Preparation'
$steps = [Collections.Generic.List[object]]::new()
function Record([string]$Name, [bool]$Passed) {
    $steps.Add([ordered]@{Name=$Name;Passed=$Passed;Time=[DateTimeOffset]::Now.ToString('o')})
    if (-not $Passed) { throw 'Native window assertion failed' }
}
function Service-Pid { return (Get-CimInstance Win32_Service -Filter "Name='CampusPulse'").ProcessId }
try {
    $title = [string]::Concat('CampusPulse ',[char]0x00b7,' ',[char]0x6821,[char]0x56ed,
        [char]0x7f51,[char]0x81ea,[char]0x52a8,[char]0x8fde,[char]0x63a5)
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$title)
    if ($null -ne [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)) {
        throw 'Existing user window must be left untouched'
    }
    $backgroundPid = Service-Pid
    if ($backgroundPid -le 0 -or (Get-Service CampusPulse).Status -ne 'Running') { throw 'Background not running' }
    $settingsFile = Join-Path $env:ProgramData 'CampusPulse\settings.json'
    $credentialsFile = Join-Path $env:ProgramData 'CampusPulse\credentials.dat'
    $settingsHash = (Get-FileHash -LiteralPath $settingsFile).Hash
    $credentialHash = (Get-FileHash -LiteralPath $credentialsFile).Hash
    $app = Join-Path $repo 'src\CampusPulse.App\bin\Release\net10.0-windows\CampusPulse.App.dll'
    $owned = Start-Process -FilePath (Join-Path $repo '.tools\dotnet\dotnet.exe') -WindowStyle Hidden -PassThru -ArgumentList ('"'+$app+'"')
    $window = $null
    for ($i=0;$i -lt 20 -and $null -eq $window;$i++) {
        Start-Sleep -Milliseconds 500
        $window = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)
    }
    Record 'Own formal window opened' ($null -ne $window -and $window.Current.ProcessId -eq $owned.Id)
    Start-Sleep -Seconds 2
    $phase = 'Read native handle and DPI'
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    $null = [ProductWindowNative]::ShowWindow($handle,9)
    Start-Sleep -Milliseconds 500
    $scale = [ProductWindowNative]::GetDpiForWindow($handle) / 96.0
    $phase = 'Read window pattern'
    $pattern = $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    $phase = 'Resize to minimum'
    # Use the native window operation, then verify actual geometry. The UIA provider
    # rejected Resize on this host; do not infer a product defect from that provider call.
    if (-not [ProductWindowNative]::SetWindowPos($handle,[IntPtr]::Zero,0,0,[int][Math]::Ceiling(900*$scale),[int][Math]::Ceiling(680*$scale),0x0016)) {
        throw 'Native window resize failed'
    }
    Start-Sleep -Milliseconds 700
    $bounds = $window.Current.BoundingRectangle
    Record 'Minimum supported window size responds' (-not $owned.HasExited -and
        [Math]::Abs($bounds.Width-900*$scale) -le 2 -and [Math]::Abs($bounds.Height-680*$scale) -le 2)
    $phase = 'Resize to default'
    if (-not [ProductWindowNative]::SetWindowPos($handle,[IntPtr]::Zero,0,0,[int][Math]::Ceiling(1080*$scale),[int][Math]::Ceiling(850*$scale),0x0016)) {
        throw 'Native window resize failed'
    }
    $phase = 'Minimize'
    $pattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 700
    Record 'Minimize leaves background process running' ((Service-Pid) -eq $backgroundPid -and (Get-Service CampusPulse).Status -eq 'Running')
    $phase = 'Close to tray'
    $pattern.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
    $pattern.Close()
    Start-Sleep -Seconds 2
    Record 'Close hides formal window without exiting interface' (-not $owned.HasExited -and -not [ProductWindowNative]::IsWindowVisible($handle))
    Record 'Hidden interface leaves same background process running' ((Service-Pid) -eq $backgroundPid -and (Get-Service CampusPulse).Status -eq 'Running')
    # Restore through Win32 solely for cleanup; this does not validate the tray menu.
    $null = [ProductWindowNative]::ShowWindow($handle,9)
    Start-Sleep -Milliseconds 700
    Record 'Restored window is visible' ([ProductWindowNative]::IsWindowVisible($handle))
    Record 'Window operations preserve settings and credential bytes' ((Get-FileHash -LiteralPath $settingsFile).Hash -eq $settingsHash -and
        (Get-FileHash -LiteralPath $credentialsFile).Hash -eq $credentialHash)
} catch {
    $failure=$_.Exception.GetType().FullName
    $failureLine=$_.InvocationInfo.ScriptLineNumber
    $failureCause=$_.Exception.GetBaseException().GetType().FullName
}
finally {
    if ($null -ne $owned -and -not $owned.HasExited) {
        # Only this script's own interface process; never terminate the background or user windows.
        Stop-Process -Id $owned.Id -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        if ($null -ne $backgroundPid) {
            $steps.Add([ordered]@{Name='Own interface process exit leaves same background running';
                Passed=((Service-Pid) -eq $backgroundPid -and (Get-Service CampusPulse).Status -eq 'Running');Time=[DateTimeOffset]::Now.ToString('o')})
        }
    }
    [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');Failure=$failure;FailureLine=$failureLine;FailureCause=$failureCause;Phase=$phase;Steps=@($steps.ToArray());
        TrayMenuNotTested=$true;VisualClippingNotTested=$true;NoConfigurationCommands=$true} | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $evidence 'ui-window-validation.json') -Encoding UTF8
}
if ($null -ne $failure -or @($steps | Where-Object { -not $_.Passed }).Count -gt 0) { exit 1 }
exit 0
