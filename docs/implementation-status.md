# ForgeLink 实施状态

主进度基线已经合并到根目录《ForgeLink 系统总体设计方案》第 23 节。本文保留为便捷摘要；发生差异时以主设计方案为准。静态检查和模拟驱动结果不得描述为真实 PLC、InfluxDB 或外部系统验证。

## 阶段一：基础骨架

- [x] .NET 10 分层解决方案；
- [x] 独立 Collector Service；
- [x] WPF UI 桌面主框架；
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

## 阶段三：InfluxDB 历史

- [x] 历史通道抽象；
- [x] 默认禁用通道和启用门禁状态机；
- [x] SQLite 不含历史表的自动化约束测试；
- [x] 基于官方 `InfluxDB3.Client` 的 InfluxDB 3.x 基础适配器；
- [x] 3.x 参数校验、批量 PointData 写入、SQL 查询、完整连接测试代码和 schema 映射测试；
- [x] InfluxDB 2.x 已取消，不属于首版范围；
- [ ] DPAPI Token、配置持久化及 Collector Service 运行接入；
- [ ] 有界内存缓冲、缺口记录、查询 API 和趋势图；
- [ ] 真实 InfluxDB 3 服务器读写查询联调。

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
2. 需要可访问的 InfluxDB 3 URL、Database 和具备测试写入/查询权限的 Token，才能执行真实联调；
3. Windows Service 账户和安装目录权限在安装器阶段统一验收。
