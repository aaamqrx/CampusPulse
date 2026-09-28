[CmdletBinding()]
param(
    [ValidateSet('Status', 'Install', 'Refresh', 'Open', 'Remove')][string]$Action = 'Status',
    [switch]$PreserveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$source = Join-Path $repo '.local\service-validation-20260927\Service'
$target = Join-Path $env:ProgramFiles 'CampusPulse-Validation'
$binary = Join-Path $target 'CampusPulse.Service.exe'
$marker = Join-Path $target '.campuspulse-validation'
$data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CampusPulse'
$dotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
$app = Join-Path $repo 'src\CampusPulse.App\bin\Release\net10.0-windows\CampusPulse.App.dll'
$serviceName = 'CampusPulse'
$markerText = 'CampusPulse temporary service validation; no installer'

if ($PreserveData -and $Action -ne 'Install') {
    throw '-PreserveData 仅用于重新注册临时后台。'
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '此操作需要管理员权限。请在管理员 PowerShell 中运行本脚本。'
    }
}

function Get-ProductService {
    Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
}

function Assert-ProductPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Expected)
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $intended = [IO.Path]::GetFullPath($Expected).TrimEnd('\')
    if (-not [string]::Equals($resolved, $intended, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作意外路径：$resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        $root = Get-Item -LiteralPath $resolved -Force
        if ($root.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "拒绝操作链接目录：$resolved"
        }
        $links = @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)
        if ($links | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
            throw "拒绝操作包含链接的目录：$resolved"
        }
    }
}

function Assert-OurService {
    param($Service)
    if ($null -eq $Service) { return }
    if (-not [string]::Equals($Service.PathName.Trim('"'), $binary,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw '已存在同名但路径不同的服务；不会修改或删除它。'
    }
}

switch ($Action) {
    'Status' {
        $service = Get-ProductService
        if ($null -eq $service) { Write-Host 'CampusPulse 服务：未注册' }
        else {
            Write-Host "CampusPulse 服务：$($service.State)；启动类型：$($service.StartMode)"
            Write-Host "程序路径：$($service.PathName)"
        }
        Write-Host "临时副本：$(Test-Path -LiteralPath $source)；正式界面 DLL：$(Test-Path -LiteralPath $app)"
    }
    'Install' {
        Assert-Administrator
        if (Get-ProductService) { throw 'CampusPulse 服务已存在；拒绝覆盖。' }
        if (Test-Path -LiteralPath $target) { throw '临时安装目录已存在；拒绝覆盖。' }
        if (Test-Path -LiteralPath $data) {
            if (-not $PreserveData) { throw 'CampusPulse 数据目录已存在；请先核对来源，使用 -PreserveData 明确保留。' }
            Assert-ProductPath -Path $data -Expected (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CampusPulse')
            $unknown = @(Get-ChildItem -LiteralPath $data -Force | Where-Object {
                $_.PSIsContainer -or $_.Name -notin @('settings.json', 'credentials.dat', 'events.json')
            })
            if ($unknown.Count) { throw '数据目录含未知文件；拒绝自动复用。' }
            $settingsFile = Join-Path $data 'settings.json'
            if (-not (Test-Path -LiteralPath $settingsFile -PathType Leaf) -or
                (Get-Item -LiteralPath $settingsFile).Length -gt 16384) {
                throw '已有配置缺失或过大；拒绝自动复用。'
            }
            $saved = Get-Content -LiteralPath $settingsFile -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            if ($saved.ConfigVersion -ne 2 -or $saved.Enabled -isnot [bool] -or $saved.Enabled) {
                throw '仅允许复用已暂停自动重连的版本 2 配置。'
            }
        }
        elseif ($PreserveData) { throw '没有可保留的数据目录；请使用普通 Install。' }
        foreach ($name in @('CampusPulse.Service.exe', 'CampusPulse.Service.dll', 'coreclr.dll', 'hostfxr.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
                throw "后台测试副本缺少 $name。"
            }
        }
        Assert-ProductPath -Path $source -Expected (Join-Path $repo '.local\service-validation-20260927\Service')
        $forbidden = @(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object {
            $_.Name -in @('settings.json', 'credentials.dat', 'events.json') -or
            $_.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log')
        })
        if ($forbidden.Count) { throw '后台测试副本含用户数据或敏感文件；拒绝复制。' }
        New-Item -ItemType Directory -Path $target -ErrorAction Stop | Out-Null
        Set-Content -LiteralPath $marker -Value $markerText -Encoding Ascii
        Assert-ProductPath -Path $target -Expected (Join-Path $env:ProgramFiles 'CampusPulse-Validation')
        & icacls.exe $target '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '设置临时程序目录权限失败；服务未注册。' }
        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force -ErrorAction Stop
        & icacls.exe $target '/setowner' '*S-1-5-32-544' '/T' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '设置临时文件所有者失败；服务未注册。' }
        New-Service -Name $serviceName -BinaryPathName ('"' + $binary + '"') -StartupType Manual -DisplayName 'CampusPulse (validation)' | Out-Null
        Start-Service -Name $serviceName -ErrorAction Stop
        (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Assert-OurService (Get-ProductService)
        if ($PreserveData) {
            Write-Host '临时后台服务已启动（手动启动）；已保留原有配置与凭据，未生成安装包。'
        }
        else {
            Write-Host '临时后台服务已启动（手动启动）；未填写账号密码，也未生成安装包。'
        }
    }
    'Open' {
        Assert-Administrator
        Assert-OurService (Get-ProductService)
        if ((Get-Service -Name $serviceName -ErrorAction Stop).Status -ne 'Running') {
            throw '后台未运行。请先运行 -Action Install 或启动已注册的服务。'
        }
        if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf) -or
            -not (Test-Path -LiteralPath $app -PathType Leaf)) {
            throw '正式界面或本地 .NET 10 不存在；请先运行 scripts/check-dev.ps1。'
        }
        Start-Process -FilePath $dotnet -ArgumentList ('"' + $app + '"') -WorkingDirectory (Split-Path -Parent $app)
        Write-Host '已打开正式界面；标题不应包含“演示，未联网”。'
    }
    'Refresh' {
        Assert-Administrator
        $service = Get-ProductService
        if ($null -eq $service) { throw '临时服务未注册；不能刷新。' }
        Assert-OurService $service
        Assert-ProductPath -Path $source -Expected (Join-Path $repo '.local\service-validation-20260927\Service')
        Assert-ProductPath -Path $target -Expected (Join-Path $env:ProgramFiles 'CampusPulse-Validation')
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
            (Get-Content -LiteralPath $marker -Raw).Trim() -ne $markerText) {
            throw '未找到本脚本的临时服务标记；拒绝覆盖程序。'
        }
        foreach ($name in @('CampusPulse.Service.exe', 'CampusPulse.Service.dll', 'CampusPulse.Core.dll', 'coreclr.dll', 'hostfxr.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
                throw "后台测试副本缺少 $name。"
            }
        }
        $forbidden = @(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object {
            $_.Name -in @('settings.json', 'credentials.dat', 'events.json') -or
            $_.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log')
        })
        if ($forbidden.Count) { throw '后台测试副本含用户数据或敏感文件；拒绝复制。' }
        $unexpectedData = @(Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object {
            $_.Name -in @('settings.json', 'credentials.dat', 'events.json') -or
            $_.Extension -in @('.pfx', '.p12', '.pem', '.key', '.log')
        })
        if ($unexpectedData.Count) { throw '临时程序目录含用户数据或敏感文件；拒绝备份和刷新。' }
        $originalStartup = $service.StartMode
        $wasRunning = $service.State -eq 'Running'
        if ($service.State -notin @('Running', 'Stopped')) { throw '服务正在改变状态，请稍后重试。' }
        $backup = Join-Path $repo ('.local\service-validation-20260927\Backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if (Test-Path -LiteralPath $backup) { throw '备份目录已存在；拒绝覆盖。' }
        New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
        $stopAttempted = $false
        try {
            Get-ChildItem -LiteralPath $target -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $backup -Recurse -Force -ErrorAction Stop
            }
            if ($wasRunning) {
                $stopAttempted = $true
                Stop-Service -Name $serviceName -ErrorAction Stop
                (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
            }
            Get-ChildItem -LiteralPath $source -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force -ErrorAction Stop
            }
            foreach ($name in @('CampusPulse.Service.exe', 'CampusPulse.Service.dll', 'CampusPulse.Core.dll')) {
                $expected = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
                $actual = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
                if ($actual -ne $expected) { throw "刷新后校验失败：$name" }
            }
            if ($wasRunning) {
                Start-Service -Name $serviceName -ErrorAction Stop
                (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
            }
            $after = Get-ProductService
            Assert-OurService $after
            if ($after.StartMode -ne $originalStartup) { throw '服务启动类型发生意外变化。' }
            Write-Host "临时后台已刷新；保留原有数据和启动类型。旧程序备份：$backup"
        }
        catch {
            try {
                if ($stopAttempted -and (Get-Service -Name $serviceName).Status -ne 'Stopped') {
                    Stop-Service -Name $serviceName -ErrorAction Stop
                    (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
                }
                if ($stopAttempted) {
                    Get-ChildItem -LiteralPath $backup -Force | ForEach-Object {
                        Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force -ErrorAction Stop
                    }
                    if ($wasRunning) {
                        Start-Service -Name $serviceName -ErrorAction Stop
                        (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
                    }
                }
            }
            catch { Write-Warning '回滚未能完成；保留程序备份和服务状态，请先检查后再继续。' }
            throw
        }
    }
    'Remove' {
        Assert-Administrator
        Assert-ProductPath -Path $target -Expected (Join-Path $env:ProgramFiles 'CampusPulse-Validation')
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or
            (Get-Content -LiteralPath $marker -Raw).Trim() -ne $markerText) {
            throw '未找到本脚本的标记；拒绝删除临时程序目录。'
        }
        $service = Get-ProductService
        Assert-OurService $service
        if ($null -ne $service) {
            if ($service.State -ne 'Stopped') {
                Stop-Service -Name $serviceName -ErrorAction Stop
                (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
            }
            & sc.exe delete $serviceName | Out-Null
            if ($LASTEXITCODE -ne 0) { throw '删除临时服务失败；保留程序目录以便排查。' }
            Start-Sleep -Seconds 2
            if (Get-ProductService) { throw '服务仍处于待删除状态；保留程序目录，稍后重试。' }
        }
        Remove-Item -LiteralPath $target -Recurse -Force
        if (Test-Path -LiteralPath $data) {
            Assert-ProductPath -Path $data -Expected (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CampusPulse')
            $entries = @(Get-ChildItem -LiteralPath $data -Force)
            if ($entries | Where-Object { $_.Name -notin @('settings.json', 'events.json') }) {
                Write-Warning '数据目录含凭据或其他文件，已保留；请单独核对。'
            }
            else {
                Remove-Item -LiteralPath $data -Recurse -Force
                Write-Host '已清理本次无凭据测试产生的配置和事件。'
            }
        }
        Write-Host '临时后台服务与程序副本已移除。'
    }
}
