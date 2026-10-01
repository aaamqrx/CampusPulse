[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-preview\.\d+)?$')]
    [string]$Version = '0.1.0-preview.1',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$DotNetPath,
    [string]$InnoCompilerPath,
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$installerRoot = Join-Path $artifactsRoot 'installer'
$installer = Join-Path $installerRoot "CampusPulse-Setup-$Version.exe"
$checksumPath = Join-Path $installerRoot 'sha256.txt'
$manifestPath = Join-Path $artifactsRoot 'build-manifest.json'

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory)][string]$Path)
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to modify a path outside the repository artifacts directory.'
    }
    $ancestor = $resolved
    while ($ancestor -and $ancestor -ne $repoRoot) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing to modify a linked artifact path: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    return $resolved
}

function Remove-KnownArtifact {
    param([string]$Path)
    $safePath = Assert-SafeArtifactPath $Path
    if (Test-Path -LiteralPath $safePath) { Remove-Item -LiteralPath $safePath -Force }
}

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    # Only build arguments are passed here, never user credentials.
    Write-Host ("Running: {0} {1}" -f [IO.Path]::GetFileName($Executable), ($Arguments -join ' '))
    $global:LASTEXITCODE = 0
    & $Executable @Arguments | Tee-Object -FilePath $script:buildLog -Append | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$([IO.Path]::GetFileName($Executable)) failed with exit code $LASTEXITCODE. See $script:buildLog"
    }
}

function Reset-PublishDirectory {
    param([ValidateSet('App', 'Service')][string]$Component)
    $target = Assert-SafeArtifactPath (Join-Path $artifactsRoot "publish\$Component")
    if (Test-Path -LiteralPath $target) {
        $links = @(Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object {
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint
        })
        if ($links.Count) { throw "Refusing to clean publish directory containing links: $target" }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    return $target
}

function Assert-PublishPayload {
    param([string]$Path, [string]$Component)
    foreach ($required in @("CampusPulse.$Component.exe", "CampusPulse.$Component.dll", 'coreclr.dll', 'hostfxr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $required) -PathType Leaf)) {
            throw "The self-contained $Component output is missing $required."
        }
    }
    $forbidden = @(Get-ChildItem -LiteralPath $Path -Recurse -Force | Where-Object {
        $_.Name -in @('settings.json', 'credentials.dat', 'events.json', '.local', '.tools', '.git') -or
        $_.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log')
    })
    if ($forbidden.Count) { throw "Unexpected user data or secret-like file in $Component publish output." }
}

Push-Location $repoRoot
try {
    # Invalidate old success artifacts before checking tools or source files.
    foreach ($path in @($installer, $checksumPath, $manifestPath)) { Remove-KnownArtifact $path }
    $logRoot = Assert-SafeArtifactPath (Join-Path $artifactsRoot 'logs')
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $script:buildLog = Join-Path $logRoot ('build-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    "CampusPulse build $Version; started $([DateTime]::UtcNow.ToString('O'))" | Set-Content -LiteralPath $script:buildLog

    $projects = @(
        'src\CampusPulse.Core\CampusPulse.Core.csproj',
        'src\CampusPulse.Service\CampusPulse.Service.csproj',
        'src\CampusPulse.App\CampusPulse.App.csproj',
        'tests\CampusPulse.Tests\CampusPulse.Tests.csproj'
    )
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Required project is missing: $project" }
    }
    if (-not $DotNetPath) {
        $localDotnet = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
        $DotNetPath = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
    }
    $sdkVersion = (& $DotNetPath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') { throw '.NET 10 SDK is required; a runtime alone is insufficient.' }

    if (-not $SkipInstaller) {
        if (-not $InnoCompilerPath) {
            $candidates = @((Join-Path $repoRoot '.tools\inno\ISCC.exe'), (Join-Path $repoRoot '.tools\innosetup\ISCC.exe'))
            if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
            $InnoCompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
            if (-not $InnoCompilerPath) {
                $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
                if ($command) { $InnoCompilerPath = $command.Source }
            }
        }
        if (-not $InnoCompilerPath -or -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
            throw 'Inno Setup 6 was not found. Supply -InnoCompilerPath or use -SkipInstaller.'
        }
    }

    & (Join-Path $PSScriptRoot 'verify-repository.ps1')
    foreach ($project in $projects) {
        Invoke-Checked $DotNetPath @('build', $project, '--configuration', $Configuration, "-p:Version=$Version", '--nologo')
    }
    # Offline console runner: a nonzero exit must stop packaging.
    Invoke-Checked $DotNetPath @('run', '--project', 'tests\CampusPulse.Tests\CampusPulse.Tests.csproj', '--configuration', $Configuration, '--no-build')

    foreach ($component in @('Service', 'App')) {
        $destination = Reset-PublishDirectory $component
        Invoke-Checked $DotNetPath @(
            'publish', "src\CampusPulse.$component\CampusPulse.$component.csproj",
            '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true',
            '-p:PublishSingleFile=false', '-p:DebugType=None', '-p:DebugSymbols=false',
            "-p:Version=$Version", '--output', $destination, '--nologo'
        )
        Assert-PublishPayload $destination $component
    }

    if (-not $SkipInstaller) {
        Assert-SafeArtifactPath $installerRoot | Out-Null
        New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
        $numericVersion = ($Version -split '-')[0] + '.0'
        Invoke-Checked $InnoCompilerPath @("/DAppVersion=$Version", "/DAppNumericVersion=$numericVersion", (Join-Path $repoRoot 'installer\CampusPulse.iss'))
        if (-not (Test-Path -LiteralPath $installer -PathType Leaf) -or (Get-Item -LiteralPath $installer).Length -eq 0) {
            throw "Expected installer missing or empty: $installer"
        }
        $checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($checksumPath, "$checksum  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
    }

    $sourceCommit = $null; $sourceDirty = $null; $sourceTags = @()
    if ((Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
        $sourceCommit = (& git -c "safe.directory=$repoRoot" rev-parse HEAD | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot record the build source commit.' }
        $sourceChanges = @(& git -c "safe.directory=$repoRoot" status --porcelain)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot record the build source state.' }
        $sourceDirty = $sourceChanges.Count -gt 0
        $sourceTags = @(& git -c "safe.directory=$repoRoot" tag --points-at HEAD)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot record the build source tags.' }
    }
    $manifest = [ordered]@{
        sourceCommit = $sourceCommit; sourceDirty = $sourceDirty; sourceTags = $sourceTags
        version = $Version; sdk = $sdkVersion; completedUtc = [DateTime]::UtcNow.ToString('O')
        configuration = $Configuration; runtime = 'win-x64'; selfContained = $true
        offlineTestsPassed = $true; installerBuilt = -not [bool]$SkipInstaller
        installer = if ($SkipInstaller) { $null } else { [IO.Path]::GetFileName($installer) }
        sha256 = if ($SkipInstaller) { $null } else { $checksum }
        installerExecuted = $false; realAuthenticationTested = $false
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Build, offline tests and self-contained publish completed. Evidence: $script:buildLog"
    if (-not $SkipInstaller) { Write-Host "Created installer: $installer" }
    Write-Host 'No product installer was run. Installation, real authentication and overnight recovery remain unverified.'
}
catch {
    foreach ($path in @($installer, $checksumPath, $manifestPath)) { Remove-KnownArtifact $path }
    throw
}
finally { Pop-Location }
