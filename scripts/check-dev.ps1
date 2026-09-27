[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

function Invoke-DotNet {
    param([string[]]$Arguments)
    & $DotNetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }
}

Push-Location $repoRoot
try {
    if (-not $DotNetPath) {
        $local = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
        $DotNetPath = if (Test-Path -LiteralPath $local -PathType Leaf) {
            $local
        } else {
            (Get-Command dotnet -ErrorAction Stop).Source
        }
    }
    $sdk = (& $DotNetPath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') {
        throw '.NET 10 SDK is required.'
    }
    & (Join-Path $PSScriptRoot 'verify-repository.ps1')
    foreach ($project in @(
        'src\CampusPulse.Core\CampusPulse.Core.csproj',
        'src\CampusPulse.Service\CampusPulse.Service.csproj',
        'src\CampusPulse.App\CampusPulse.App.csproj',
        'tests\CampusPulse.Tests\CampusPulse.Tests.csproj'
    )) {
        Invoke-DotNet -Arguments @('build', $project, '--configuration', $Configuration, '--nologo')
    }
    Invoke-DotNet -Arguments @('run', '--project', 'tests\CampusPulse.Tests\CampusPulse.Tests.csproj',
        '--configuration', $Configuration, '--no-build')
    Write-Host "Development check passed using SDK $sdk. No publish, installer, service installation, or real authentication was run."
}
finally {
    Pop-Location
}
