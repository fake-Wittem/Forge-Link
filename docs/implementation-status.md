# ForgeLink 实施状态

主进度基线已经合并到根目录《ForgeLink 系统总体设计方案》第 23 节。本文保留为便捷摘要；发生差异时以主设计方案为准。静态检查和模拟驱动结果不得描述为真实 PLC、TDengine 或外部系统验证。

## 阶段一：基础骨架

- [x] .NET 10 分层解决方案；
- [x] 独立 Collector Service；
- [x] WPF UI 桌面主框架；
- [x] Shell + 8 个独立 UserControl 页面和强类型导航；
- [x] 设备/点位编辑器 ViewModel 拆分及页面进入/离开生命周期；
- [x] 8 个菜单 UI Automation 切换冒烟；
- [x] SQLite 配置库和版本迁移；
- [x] 基于 `ForgeLink.Collector` 命名管道的 Desktop-Service 管理 API；
- [x] 健康检查与结构化日志；
- [x] 命名管道服务端 HTTP/1.1 健康探针进程冒烟；
- [ ] Windows Service 安装、服务账户权限和安装后启停验收。

## 阶段二：PLC 采集

- [x] 协议无关驱动接口；
- [x] 模拟设备驱动；
- [x] 有界异步处理管道；
- [x] 设备和点位 CRUD；
- [x] 配置校验、关联删除保护和点位编码唯一性；
- [x] 配置保存后的采集任务热加载；
- [x] 独立设备连接测试；
- [x] CSV 导入、导出和批量事务；
- [x] 实时值页面及删除/禁用后的缓存收敛；
- [x] 基于 NModbus 的 Modbus TCP 驱动；
- [x] COIL、DI、HR、IR 地址解析及五位参考地址兼容；
- [x] 连续地址合并读取与 Modbus 单次读取数量限制；
- [x] Unit ID、连接超时、读取超时、字节序和字序配置；
- [x] 点位独立采集周期调度；
- [x] 本机 NModbus TCP 从站集成测试；
- [ ] 具体型号 PLC 的现场连接和数据对照验收；
- [ ] 真实设备断线重连与现场故障测试。

## 阶段三：TDengine 历史

- [x] 历史通道抽象；
- [x] 默认禁用通道和启用门禁状态机；
- [x] SQLite 不含历史表的自动化约束测试；
- [x] 删除 InfluxDB 项目、客户端依赖、测试和产品文案；
- [x] 基于官方 `TDengine.Connector` 3.2.1 的 TDengine 3.x WebSocket 基础适配器；
- [x] 主机/端口/账号/Database 参数校验、五类超级表、点位子表、1,000 行分批写入、SQL 查询及完整连接测试代码；
- [x] 本地 schema 映射、SQL 转义、同子表批量合并和查询边界测试；
- [x] 版本兼容分级：3.3.6.0+ 官方保证、3.0.0.0–3.3.5.x 尽力兼容、低于 3.0 拒绝；
- [ ] DPAPI 密码保护、配置持久化及 Collector Service 运行接入；
- [ ] 有界内存缓冲、缺口记录、查询 API 和趋势图；
- [~] 已确认本机 TDengine/taosAdapter 3.4.2.6 服务运行且 6041 监听；待专用 Database 和账号后执行真实读写查询联调。

## 阶段四：外部传输

- [ ] SQLite Outbox 与死信；
- [ ] REST 批量通道；
- [ ] MQTT 通道；
- [ ] 字段映射、重试和人工重发。

## 阶段五：工程化交付

- [ ] DPAPI 凭据保护与配置脱敏导出；
- [ ] 配置备份和失败恢复；
- [ ] WiX 完整离线安装包；
- [ ] Windows Service 安装、升级和卸载；
- [ ] 故障注入与 72 小时稳定性测试。

## 当前待外部条件

1. 现场 PLC 型号确定后，需要提供地址表用于真实数据对照验收；
2. TDengine 已确认为本机 3.4.2.6 Enterprise、`D:\TDengine`、WebSocket `127.0.0.1:6041`、SSL 关闭；仍需提供 ForgeLink 专用 Database 及具备建表/写入/查询/删除测试表权限的账号；
3. Windows Service 账户和安装目录权限在安装器阶段统一验收。
