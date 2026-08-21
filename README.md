**简体中文** | [English](./README.en.md)

# ForgeLink

ForgeLink 是面向 Windows x64 工控机和边缘计算机的 PLC 数据采集与转发系统。本仓库已经提供一个可编译、可运行的 .NET 10 首版：后台 Collector Service 独立采集，WPF 桌面端通过本机命名管道管理 API 展示设备、点位和实时数据。

## 核心能力

- Collector Service 与 WPF Desktop 进程分离，关闭桌面端不影响采集；
- 协议无关的 `IPlcDriver`、历史通道 `IHistoryChannel` 与配置仓储接口；
- 基于有界 `Channel` 的异步采集管道和线程安全实时缓存；
- SQLite 版本化初始化，仅保存设备和点位配置；
- InfluxDB 历史门禁，默认关闭且绝不回退 SQLite；
- 基于官方 `InfluxDB3.Client` 的 InfluxDB 3.x 基础适配器、批量写入和 SQL 查询；
- WPF UI 明亮主题、统一设计令牌、虚拟化数据表格；
- 本机健康、状态、设备、点位和实时数据 API；
- 模拟 PLC 驱动，可在无现场设备时完整演示采集链路。
- 基于 NModbus 的 Modbus TCP 驱动，支持功能区地址、连续批量读取、Unit ID、超时和寄存器顺序；
- 设备和点位完整 CRUD、关联删除保护、连接测试及运行时热加载；
- 点位 CSV 导入导出，支持事务回滚和复杂文本转义。

## 系统架构

```text
ForgeLink Desktop
       │ Windows 命名管道 ForgeLink.Collector
       ▼
Collector Service ── SQLite 配置库
       │
       ├─ 采集调度 ── IPlcDriver ── PLC/模拟设备
       ├─ 有界处理管道 ── 实时内存缓存
       └─ HistoryGate ── IHistoryChannel（默认关闭）
```

## 支持协议

当前仓库已实现 `Simulation` 和基于 NModbus 的 `Modbus TCP` 驱动。Modbus TCP 已通过本机 NModbus 从站集成测试，但尚未在具体型号的现场 PLC 上验收。地址、Unit ID 和寄存器顺序说明见 [Modbus TCP 配置说明](./docs/modbus-tcp.md)。OPC UA、Siemens S7、Modbus RTU、Mitsubishi MC 和 Omron FINS 仍是后续候选协议。

## 环境与安装

- Windows x64；
- .NET SDK 10.0.400 或兼容的 10.0 补丁版本；
- 运行时需要 .NET Desktop Runtime 10 与 ASP.NET Core Runtime 10。

WiX 离线安装包和 Windows Service 注册属于工程化交付阶段，当前开发版从命令行启动。

## 快速开始

```powershell
.\tools\verify.ps1
.\tools\run-dev.ps1
```

第二条命令会在后台启动 Collector Service，等待健康检查通过，再打开桌面管理端；关闭桌面端后，脚本会停止本次启动的服务进程。

也可以分别启动：

```powershell
$env:ForgeLink__DataRoot = "$PWD\data\development"
dotnet run --project .\src\ForgeLink.Collector.Service
dotnet run --project .\src\ForgeLink.Desktop
```

## 配置说明

- IPC 管道名默认是 `ForgeLink.Collector`，可用 `ForgeLink__PipeName` 覆盖；
- 开发数据目录由 `ForgeLink__DataRoot` 指定；
- 未指定数据目录时使用 `%ProgramData%\ForgeLink`；
- 首次启动会写入 1 台脱敏模拟设备和 3 个演示点位。

## 数据和安全边界

- SQLite 不保存点位采集历史；
- 历史未配置、未验证或未启用时不保存、不积累、不补写；
- PLC 写入接口默认拒绝请求；
- 管理 API 仅通过本机命名管道开放，不监听 TCP 管理端口；
- `data/`、数据库、日志、证书和私钥均被排除在版本控制之外；
- 示例地址和数据均为脱敏模拟值。

## 开发与测试

解决方案入口是 `ForgeLink.slnx`。质量门禁包含 NuGet 还原、警告即错误编译、格式校验和 xUnit 测试。当前自动化测试覆盖工程值换算、配置边界、历史门禁、配置热加载信号、CSV、SQLite 删除保护和批量事务回滚。

真实 PLC、InfluxDB 3、REST/MQTT、Windows Service、安装升级和 72 小时稳定性仍需在对应现场或模拟环境中执行集成验收。InfluxDB 3 适配器尚未接入 DPAPI 配置、采集管道和桌面历史页面。

## 许可证和贡献方式

本项目许可证尚未指定。提交变更前请运行 `.\tools\verify.ps1`，并保持中英文 README 的章节同步。第三方组件许可证见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
