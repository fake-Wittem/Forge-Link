# ForgeLink 开发环境：Visual Studio 2026 安装指导

## 1. 文档目的

本文用于配置 ForgeLink PLC 数据采集系统的 Windows 开发环境。

本文只说明 Visual Studio 和开发工具的安装，不包含 ForgeLink 的运行部署、数据库初始化或系统架构方案。

## 2. 推荐版本

| 项目 | 推荐选择 |
| --- | --- |
| IDE | Visual Studio 2026 Stable（稳定通道） |
| 版本 | Community、Professional 或 Enterprise |
| 开发框架 | .NET 10 LTS |
| 操作系统 | 64 位 Windows 11 |
| 系统架构 | x64 优先 |
| 语言包 | 中文（简体），可同时保留英文 |

说明：

- 个人开发、学习或符合许可条件的小型团队可使用 Community。
- 企业项目应根据组织规模和 Visual Studio 许可条款选择 Professional 或 Enterprise。
- Visual Studio 2026 官方支持的桌面操作系统为 64 位 Windows 11；不建议在 Windows 10 上新建本项目的标准开发环境。
- 统一使用 Visual Studio 的 Stable 通道，不使用 Preview 版本作为正式开发环境。

## 3. 开发机建议配置

| 硬件 | 最低可用 | 推荐配置 |
| --- | --- | --- |
| CPU | 4 核 x64 | 8 核或以上 |
| 内存 | 8 GB | 16 GB 或以上 |
| 磁盘 | 30 GB 可用空间 | SSD，预留 50 GB 或以上 |
| 显示分辨率 | 1366 × 768 | 1920 × 1080 或以上 |

PLC 模拟器、数据库工具、多个调试进程同时运行时，建议使用 32 GB 内存。

## 4. 安装前准备

1. 确认 Windows Update 已完成，操作系统处于受支持状态。
2. 确认当前账户具有安装软件所需的管理员权限。
3. 从 [Visual Studio 官方下载页](https://visualstudio.microsoft.com/downloads/) 下载 Visual Studio 2026 安装程序。
4. 如开发机处于受限网络，先准备 Visual Studio 离线布局或组织内部软件源。
5. 不要预先安装来源不明的 PLC 驱动包；厂商 SDK 应在确定 PLC 型号和授权方式后单独安装。

## 5. 必装工作负荷

打开 Visual Studio Installer，在“工作负荷”页选择以下两项。

### 5.1 .NET 桌面开发

必须安装。用于开发：

- WPF 桌面管理程序；
- C# 类库；
- 控制台诊断工具；
- XAML 界面和数据绑定；
- Windows 桌面程序的生成、调试与发布。

工作负荷标识：

```text
Microsoft.VisualStudio.Workload.ManagedDesktop
```

### 5.2 ASP.NET 和 Web 开发

必须安装。用于开发：

- .NET Worker Service 后台采集服务；
- ASP.NET Core Minimal API；
- REST、健康检查和本机管理接口；
- 后续可能使用的 gRPC 服务；
- Worker Service 项目模板。

工作负荷标识：

```text
Microsoft.VisualStudio.Workload.NetWeb
```

## 6. 建议检查的单个组件

进入“单个组件”页，确认以下组件已安装。不同 Visual Studio 小版本中的显示名称可能略有差异。

### 6.1 必需组件

- .NET 10 SDK；
- .NET 10 Runtime；
- Windows 11 SDK；
- MSBuild；
- NuGet 包管理器；
- Git for Windows；
- C# 和 Visual Basic Roslyn 编译器；
- .NET 调试器；
- IntelliCode 或当前版本提供的等效代码辅助组件。

### 6.2 推荐组件

- .NET 分析工具；
- .NET 内存分析工具；
- ClickOnce 发布工具；
- MSIX 打包工具；
- GitHub 或 Azure DevOps 版本控制集成，根据团队代码托管平台选择。

ClickOnce 和 MSIX 可以同时安装。系统最终采用哪一种安装、升级方式，在部署方案确定后再决定。

## 7. 按需安装的工作负荷

以下工作负荷不需要默认勾选。

### 7.1 使用 C++ 的桌面开发

仅在以下情况下安装：

- PLC 厂商只提供 C/C++ SDK；
- 需要编译原生 DLL、C++/CLI 桥接层；
- 需要调试厂商提供的原生示例；
- 项目依赖必须本机编译的原生 NuGet 包。

如果 PLC 通过 OPC UA、Modbus TCP、S7 TCP 等纯托管 .NET 库通信，则通常不需要此工作负荷。

### 7.2 数据存储和处理

ForgeLink 默认使用 SQLite，不需要为了 SQLite 安装此工作负荷。

只有需要以下能力时再安装：

- SQL Server Data Tools；
- SQL Server 数据库项目；
- SSIS、SSAS 或 SSRS；
- 直接在 Visual Studio 中设计和发布 SQL Server 数据库。

### 7.3 Azure 开发

只有明确需要连接或部署 Azure 服务时安装。本地 PLC 采集和 SQLite 运行不依赖 Azure 工作负荷。

### 7.4 容器开发工具

轻量级单机部署方案默认不使用 Docker，因此不要求安装。只有需要开发服务端容器或集成测试环境时再添加。

### 7.5 .NET Multi-platform App UI 开发

ForgeLink 桌面端默认采用 WPF，不需要安装 .NET MAUI 工作负荷。

## 8. 不建议默认安装的内容

为控制安装体积和减少维护面，以下内容不作为标准开发环境的一部分：

- 游戏开发相关工作负荷；
- Unity 和 Unreal Engine 工具；
- Python 数据科学工作负荷；
- Node.js 开发工作负荷；
- Office/SharePoint 开发；
- 移动开发；
- Linux 和嵌入式 C++ 开发；
- Azure、Microsoft 365 等当前项目未使用的云工具。

后续有实际需求时，可以通过 Visual Studio Installer 的“修改”功能增量安装。

## 9. 安装步骤

1. 启动 Visual Studio Installer。
2. 选择 Visual Studio 2026 的 Community、Professional 或 Enterprise 版本。
3. 选择 Stable 通道。
4. 在“工作负荷”页勾选：
   - `.NET 桌面开发`；
   - `ASP.NET 和 Web 开发`。
5. 在“单个组件”页确认第 6 节列出的必需组件。
6. 在“语言包”页选择“中文（简体）”；建议同时保留英文语言包，方便核对英文报错和官方文档。
7. 将 Visual Studio IDE 安装在 SSD 上。
8. 选择“安装时下载”或按组织网络策略选择离线安装源。
9. 安装完成后重启系统，再执行本文的验证步骤。

## 10. 安装验证

### 10.1 验证 .NET SDK

打开 PowerShell，执行：

```powershell
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
```

检查结果：

- `dotnet --list-sdks` 中存在 `10.0.x`；
- 运行时列表中存在 `.NETCore.App 10.0.x`；
- 运行时列表中存在 `Microsoft.WindowsDesktop.App 10.0.x`；
- SDK 架构与开发机架构一致，通常应为 `x64`。

### 10.2 验证项目模板

执行：

```powershell
dotnet new list wpf
dotnet new list worker
dotnet new list webapi
```

三个命令均应显示可用模板。

### 10.3 验证 Visual Studio

在 Visual Studio 中打开“创建新项目”，确认可以搜索到：

- `WPF Application`；
- `Class Library`；
- `Worker Service`；
- `ASP.NET Core Web API`；
- `xUnit Test Project`。

创建临时 WPF 项目和 Worker Service 项目，目标框架选择 `.NET 10.0`，分别执行生成和调试。两个项目均能成功启动，即说明基础环境可用。

## 11. 团队环境统一

安装完成后，在 Visual Studio Installer 中导出安装配置，并将文件命名为：

```text
.vsconfig
```

建议把 `.vsconfig` 放在 ForgeLink 解决方案根目录并纳入版本控制。团队成员打开解决方案时，可以根据该文件补齐缺失组件。

导出的配置应至少包含：

```text
Microsoft.VisualStudio.Workload.ManagedDesktop
Microsoft.VisualStudio.Workload.NetWeb
```

不要在 `.vsconfig` 中加入尚未确认使用的数据库、云平台、移动端或游戏开发工作负荷。

## 12. 更新策略

- Visual Studio 使用 Stable 通道并及时安装安全更新；
- .NET 10 使用最新可用的 `10.0.x` SDK 和运行时补丁；
- 团队升级 Visual Studio 或 SDK 前，应先在一台开发机验证 ForgeLink 的生成、测试和发布；
- 使用 `global.json` 固定项目采用的 SDK 功能版本，避免开发机自动切换到不一致的 SDK；
- Visual Studio Preview 不参与正式构建和发布。

## 13. 常见问题

### 创建项目时找不到 Worker Service

打开 Visual Studio Installer，修改当前安装，确认已经安装“ASP.NET 和 Web 开发”工作负荷。

### 创建项目时找不到 WPF

确认已经安装“.NET 桌面开发”工作负荷，并检查 `Microsoft.WindowsDesktop.App 10.0.x` 运行时。

### 厂商 SDK 示例无法编译

先确认示例要求的目标架构和框架：

- x86 DLL 必须由 x86 进程加载；
- 旧版 SDK 可能要求 .NET Framework 4.8；
- C/C++ 示例可能要求“使用 C++ 的桌面开发”；
- 不要因为单个旧 SDK 就把整个 ForgeLink 解决方案改为 x86 或 .NET Framework，应优先考虑独立适配进程。

### 是否需要单独安装 SQLite

不需要。ForgeLink 后续通过 NuGet 引用 SQLite 驱动，并随应用一起发布。开发机和目标工控机都不要求单独安装 SQLite 服务。

## 14. 官方参考

- [Visual Studio 下载](https://visualstudio.microsoft.com/downloads/)
- [修改 Visual Studio 工作负荷和组件](https://learn.microsoft.com/visualstudio/install/modify-visual-studio)
- [Visual Studio 工作负荷和组件 ID](https://learn.microsoft.com/visualstudio/install/workload-component-id-vs-professional)
- [导入或导出 Visual Studio 安装配置](https://learn.microsoft.com/visualstudio/install/import-export-installation-configurations)
- [.NET 版本和支持周期](https://learn.microsoft.com/dotnet/core/releases-and-support)
- [.NET Worker Service](https://learn.microsoft.com/dotnet/core/extensions/workers)
- [使用 BackgroundService 创建 Windows Service](https://learn.microsoft.com/dotnet/core/extensions/windows-service)
- [WPF 官方文档](https://learn.microsoft.com/dotnet/desktop/wpf/)
