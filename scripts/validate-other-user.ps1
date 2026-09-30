[CmdletBinding()]
param([switch]$Execute, [switch]$Elevate)

# Explicitly approved local-account acceptance only. Default is a reviewable plan.
$ErrorActionPreference = 'Stop'
if (-not $Execute) {
    Write-Host 'Plan: create CampusPulseAclCheck as an ordinary local user with a random temporary password.'
    Write-Host 'Impersonate only that account; attempt opening three product data files and the fixed local pipe.'
    Write-Host 'Read no file contents, send no pipe commands, create no profile, and perform no campus authentication.'
    Write-Host 'Finally delete only the newly created account after checking its SID; verify data bytes and background PID unchanged.'
    exit 0
}
$repo = Split-Path -Parent $PSScriptRoot
$evidence = Join-Path $repo '.local\acceptance-20260930'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $Elevate) { throw 'Administrator required. Use -Elevate for local UAC.' }
    $child = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Execute')
    if (-not $child.WaitForExit(45000)) { throw 'Account check pending. See local UAC and evidence.' }
    if ($child.ExitCode -ne 0) { throw 'Account check incomplete. See local evidence.' }
    Get-Content (Join-Path $evidence 'other-user-validation.json')
    exit 0
}
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Add-Type @'
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
public static class ProductOrdinaryUserProbe {
 [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 static extern bool LogonUser(string user, string domain, string password, int type, int provider, out IntPtr token);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 public static bool[] Run(string user, string domain, string password, string expectedSid, string[] files) {
  IntPtr token;
  // Network logon avoids an interactive session/profile; accesses below remain entirely local.
  if (!LogonUser(user, domain, password, 3, 0, out token)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  try {
   using (var identity = new WindowsIdentity(token))
   using (var scope = identity.Impersonate()) {
    var result = new bool[files.Length+2];
    result[0] = WindowsIdentity.GetCurrent().User.Value == expectedSid &&
      !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    for (int i=0;i<files.Length;i++) {
     try { using (var stream = new FileStream(files[i],FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {} }
     catch (UnauthorizedAccessException) { result[i+1]=true; }
    }
    try { using (var pipe = new NamedPipeClientStream(".","CampusPulse.Control.v1",PipeDirection.InOut)) { pipe.Connect(1500); } }
    catch (UnauthorizedAccessException) { result[result.Length-1]=true; }
    return result;
   }
  } finally { CloseHandle(token); }
 }
}
'@
$accountName='CampusPulseAclCheck'; $created=$false; $removed=$false; $createdSid=$null
$failure=$null; $probe=$null; $unchanged=$false; $sameBackground=$false; $passwordPointer=[IntPtr]::Zero
try {
    Import-Module Microsoft.PowerShell.LocalAccounts
    if ($null -ne (Get-LocalUser -Name $accountName -ErrorAction SilentlyContinue)) { throw 'Existing account must not be reused or modified' }
    $service=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
    $expected='"'+(Join-Path $env:ProgramFiles 'CampusPulse-Validation\CampusPulse.Service.exe')+'"'
    if ($service.State -ne 'Running' -or $service.PathName -ne $expected) { throw 'Fixed validation service required' }
    $backgroundPid=$service.ProcessId
    $data=Join-Path $env:ProgramData 'CampusPulse'
    $files=@('settings.json','credentials.dat','events.json' | ForEach-Object {Join-Path $data $_})
    foreach ($path in @($data)+$files) {
        if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked product data refused' }
    }
    $settingsHash=(Get-FileHash -LiteralPath $files[0]).Hash
    $credentialHash=(Get-FileHash -LiteralPath $files[1]).Hash
    $randomBytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try {$rng.GetBytes($randomBytes)} finally {$rng.Dispose()}
    $securePassword=ConvertTo-SecureString ('aA9!'+[Convert]::ToBase64String($randomBytes)) -AsPlainText -Force
    [Array]::Clear($randomBytes,0,$randomBytes.Length)
    $user=New-LocalUser -Name $accountName -Password $securePassword -AccountNeverExpires -Description 'Temporary CampusPulse read-only ACL acceptance; remove after probe'
    $created=$true; $createdSid=$user.SID.Value
    Add-LocalGroupMember -SID ([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')) -Member $user
    $passwordPointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $probe=[ProductOrdinaryUserProbe]::Run($accountName,$env:COMPUTERNAME,
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer),$createdSid,[string[]]$files)
    if (@($probe | Where-Object {-not $_}).Count -gt 0) { throw 'Ordinary user access assertion failed' }
} catch { $failure=$_.Exception.GetType().FullName }
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)}
    if ($null -ne $securePassword) {$securePassword.Dispose()}
    if ($created) {
        try {
            $current=Get-LocalUser -Name $accountName -ErrorAction Stop
            if ($current.SID.Value -ne $createdSid) {throw 'Account identity changed; refuse deletion'}
            Remove-LocalUser -SID $current.SID
            $removed=$null -eq (Get-LocalUser -Name $accountName -ErrorAction SilentlyContinue)
        } catch {$failure='Temporary account cleanup requires local review'}
    }
    if ($null -ne $settingsHash -and $null -ne $credentialHash) {
        try {
            $unchanged=(Get-FileHash -LiteralPath $files[0]).Hash -eq $settingsHash -and
                (Get-FileHash -LiteralPath $files[1]).Hash -eq $credentialHash
            $after=Get-CimInstance Win32_Service -Filter "Name='CampusPulse'"
            $sameBackground=$after.State -eq 'Running' -and $after.ProcessId -eq $backgroundPid
        } catch {$failure='Final background/data verification incomplete'}
    }
    [ordered]@{Time=[DateTimeOffset]::Now.ToString('o');Failure=$failure;TemporaryUserCreated=$created;
        TemporaryUserRemoved=$removed;AccessAssertions=$probe;ConfigurationAndCredentialBytesUnchanged=$unchanged;
        SameBackgroundRunning=$sameBackground;NoFileContentsRead=$true;NoControlCommandsSent=$true} |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'other-user-validation.json') -Encoding UTF8
}
if ($null -ne $failure -or -not $removed -or -not $unchanged -or -not $sameBackground) {exit 1}
exit 0
