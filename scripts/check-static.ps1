[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$source = [IO.File]::ReadAllText((Join-Path $root 'src\CampusPulse.Core\Contracts.cs'))
$version = [regex]::Match($source, 'public const string Version = "([^"]+)";').Groups[1].Value
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
if ($props.Project.PropertyGroup.Version -ne $version) { throw 'Assembly and product versions differ.' }
$installer = [IO.File]::ReadAllText((Join-Path $root 'installer\CampusPulse.iss'))
if ($installer -notmatch ('#define AppVersion "' + [regex]::Escape($version) + '"')) { throw 'Installer default version differs.' }
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -Filter '*.ps1') {
    $tokens = $null; $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($script.FullName), [ref]$tokens, [ref]$parseErrors)
    if (@($parseErrors).Count) { throw "PowerShell syntax error: $($script.Name)" }
}
$documents = @(Get-Item -LiteralPath (Join-Path $root 'README.md')) + @(Get-ChildItem -LiteralPath (Join-Path $root 'docs') -Filter '*.md' -Recurse)
foreach ($document in $documents) {
    $body = [IO.File]::ReadAllText($document.FullName)
    if (([regex]::Matches($body, '(?m)^```').Count % 2) -ne 0) { throw "Unclosed Markdown fence: $($document.Name)" }
    foreach ($match in [regex]::Matches($body, '\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value.Trim().Trim('<','>')
        if ($target -match '^(https?://|#|mailto:|codex:)' -or $target -match '^/[^/]') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#')[0])
        if ($target -and -not (Test-Path -LiteralPath (Join-Path $document.DirectoryName $target))) {
            throw "Broken local Markdown link in $($document.Name): $target"
        }
    }
}
foreach ($name in @('campuspulse.svg', 'campuspulse.png', 'campuspulse.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "assets\icon\$name") -PathType Leaf)) { throw "Missing icon asset: $name" }
}
Write-Host 'Versions, PowerShell syntax, local Markdown links and committed icon files verified.'
