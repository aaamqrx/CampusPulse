[CmdletBinding()]
param([string]$DotNetPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$app = Join-Path $repoRoot 'src\CampusPulse.App\bin\Release\net10.0-windows\CampusPulse.App.dll'

if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
    throw 'The development app is not built. Run powershell -File scripts/check-dev.ps1 first.'
}
if (-not $DotNetPath) {
    $local = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
    $DotNetPath = if (Test-Path -LiteralPath $local -PathType Leaf) {
        $local
    } else {
        (Get-Command dotnet -ErrorAction Stop).Source
    }
}

# Launch the built DLL so the installer manifest is not involved. This mode uses only simulated UI data.
& $DotNetPath $app '--smoke-test'
if ($LASTEXITCODE -ne 0) {
    throw "CampusPulse demo exited with code $LASTEXITCODE"
}
