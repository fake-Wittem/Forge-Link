# 文件说明：在开发环境中依次启动 Collector Service 和桌面管理端。
# 责任边界：仅用于本机开发演示，不安装 Windows Service 或修改系统 ACL。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $workspacePath 'src\ForgeLink.Collector.Service\ForgeLink.Collector.Service.csproj'
$desktopProject = Join-Path $workspacePath 'src\ForgeLink.Desktop\ForgeLink.Desktop.csproj'
$developmentDataPath = Join-Path $workspacePath 'data\development'
$pipeName = 'ForgeLink.Collector'
$serviceProcess = $null

function Test-CollectorPipe {
    param([string]$Name)
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $Name, [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(500)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.Encoding]::ASCII, 1024, $true)
        $writer.Write("GET /health HTTP/1.1`r`nHost: localhost`r`nConnection: close`r`n`r`n")
        $writer.Flush()
        $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::ASCII, $false, 1024, $true)
        $statusLine = $reader.ReadLine()
        return $statusLine -match '^HTTP/1\.[01] 200 '
    }
    catch { return $false }
    finally { $pipe.Dispose() }
}

try {
    $env:ForgeLink__DataRoot = $developmentDataPath
    $env:ForgeLink__PipeName = $pipeName
    $serviceProcess = Start-Process -FilePath 'dotnet' `
        -ArgumentList @('run', '--project', $serviceProject, '--no-launch-profile') `
        -WorkingDirectory $workspacePath -PassThru -WindowStyle Hidden

    $serviceReady = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            if (Test-CollectorPipe -Name $pipeName) {
                $serviceReady = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $serviceReady) {
        throw 'Collector Service 未能在 10 秒内启动，请执行 tools\verify.ps1 查看构建结果。'
    }

    dotnet run --project $desktopProject --no-launch-profile
}
finally {
    if ($null -ne $serviceProcess -and -not $serviceProcess.HasExited) {
        Stop-Process -Id $serviceProcess.Id
    }
}
