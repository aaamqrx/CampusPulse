[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$required = @(
    'src/CampusPulse.Core/CampusPulse.Core.csproj',
    'src/CampusPulse.Service/CampusPulse.Service.csproj',
    'src/CampusPulse.App/CampusPulse.App.csproj',
    'tests/CampusPulse.Tests/CampusPulse.Tests.csproj',
    'installer/CampusPulse.iss',
    'README.md', 'LICENSE'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        throw "Required project file missing: $relative"
    }
}
foreach ($relative in @('.local/', '.tools/', 'artifacts/')) {
    if (-not ((Get-Content -LiteralPath (Join-Path $root '.gitignore')) -contains $relative)) {
        throw "Ignored local directory missing from .gitignore: $relative"
    }
}
Write-Host 'Project structure and local ignore entries verified. This does not scan future Git commits or installer payloads.'
