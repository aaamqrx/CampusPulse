[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('.local\release-checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $root 'installer') -Force | Out-Null
$commit = 'a' * 40
$count = 0
function Fixture([string]$version) {
    $name = "CampusPulse-Setup-$version.exe"
    [IO.File]::WriteAllText((Join-Path $root ('installer\'+$name)), 'fake installer for offline validation only')
    $hash = (Get-FileHash -LiteralPath (Join-Path $root ('installer\'+$name))).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $root 'installer\sha256.txt') -Value "$hash  $name" -Encoding ASCII
    return @{version=$version;sourceCommit=$commit;sourceDirty=$false;sourceTags=@("v$version");offlineTestsPassed=$true;
        installerBuilt=$true;sdk='10.0.203';installer=$name;sha256=$hash;
        payloadFiles=@(@{path='App/a.dll';length=1;sha256=$hash},@{path='Service/b.dll';length=1;sha256=$hash})}
}
function Verify($manifest,[bool]$accepted,[string]$name,[bool]$candidate=$false) {
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'build-manifest.json') -Encoding UTF8
    $didPass = $true
    try { & (Join-Path $PSScriptRoot 'verify-release.ps1') -Version $manifest.version -Root $root -ExpectedCommit $commit -AllowUntaggedCandidate:$candidate | Out-Null }
    catch { $didPass = $false }
    if ($didPass -ne $accepted) { throw "Release validation failed: $name" }
    $script:count++
}
Verify (Fixture '0.1.0') $true 'stable accepted'
Verify (Fixture '0.1.0-preview.3') $true 'preview accepted'
$m=Fixture '0.1.0';$m.sourceDirty=$true;Verify $m $false 'dirty tree refused'
$m=Fixture '0.1.0';$m.sourceCommit='b'*40;Verify $m $false 'wrong source refused'
$m=Fixture '0.1.0';$m.sourceTags=@('v0.1.0-preview.3');Verify $m $false 'wrong tag refused'
$m=Fixture '0.1.0';$m.sha256='0'*64;Verify $m $false 'installer mismatch refused'
$m=Fixture '0.1.0';$m.payloadFiles[0].path='App/../outside.dll';Verify $m $false 'path traversal refused'
$m=Fixture '0.1.0';$m.payloadFiles[1].path='App/a.dll';Verify $m $false 'duplicate payload refused'
$m=Fixture '0.1.0';$m.offlineTestsPassed=$false;Verify $m $false 'failed offline checks refused'
$m=Fixture '0.1.0-rc.1';Verify $m $false 'unsupported channel refused'
$m=Fixture '0.1.0';$m.sourceTags=@();Verify $m $false 'untagged cannot be released'
$m=Fixture '0.1.0';$m.sourceTags=@();Verify $m $true 'explicit unpublished candidate accepted' $true
$m=Fixture '0.1.0';$m.sourceTags=@();$m.sourceDirty=$true;Verify $m $false 'candidate still rejects dirty source' $true
Write-Host "$count/$count release provenance checks passed. Fake fixture files only; no installation or network access."
