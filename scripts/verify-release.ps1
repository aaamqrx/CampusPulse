[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-preview\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$ExpectedCommit)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $Root 'build-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.sourceCommit -ne $ExpectedCommit -or $manifest.sourceDirty -ne $false -or
    $manifest.sourceTags -notcontains "v$Version" -or $manifest.offlineTestsPassed -ne $true -or $manifest.installerBuilt -ne $true -or
    $manifest.sdk -ne '10.0.203' -or @($manifest.payloadFiles).Count -lt 2) { throw 'Release provenance mismatch.' }
$exe = Join-Path $Root "installer\CampusPulse-Setup-$Version.exe"
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = (Get-Content -LiteralPath (Join-Path $Root 'installer\sha256.txt') -Raw).Trim()
if ($manifest.installer -ne "CampusPulse-Setup-$Version.exe" -or $manifest.sha256 -ne $hash -or
    $checksum -cne "$hash  CampusPulse-Setup-$Version.exe" -or (Get-Item -LiteralPath $exe).Length -eq 0) { throw 'Installer checksum mismatch.' }
$names = @{}
foreach ($file in $manifest.payloadFiles) {
    if ($file.path -notmatch '^(App|Service)/' -or $file.path -match '(^|/)\.\.(/|$)|[:\\]' -or
        $file.sha256 -notmatch '^[a-f0-9]{64}$' -or $file.length -le 0 -or $names.ContainsKey($file.path)) { throw 'Invalid payload inventory.' }
    $names[$file.path] = $true
}
Write-Host 'Clean tag provenance, installer hash and payload inventory verified. No installation or authentication was performed.'
