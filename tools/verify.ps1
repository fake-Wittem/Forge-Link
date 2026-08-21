# 文件说明：执行 ForgeLink 的还原、编译、格式和单元测试质量门禁。
# 责任边界：不连接真实 PLC、TDengine、MQTT Broker 或外部 REST 系统。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $workspacePath 'ForgeLink.slnx'

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $solutionPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet format $solutionPath --no-restore --verify-no-changes
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test $solutionPath --no-build --no-restore --verbosity minimal
exit $LASTEXITCODE
