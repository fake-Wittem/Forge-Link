# TDengine 配置说明

## 当前开发环境

| 项目 | 当前值 |
| --- | --- |
| TDengine Server | `3.4.2.6.enterprise` |
| taosAdapter | `3.4.2.6` |
| 部署方式 | Windows EXE 安装包 |
| 安装目录 | `D:\TDengine` |
| WebSocket | `127.0.0.1:6041` |
| Native | `127.0.0.1:6030`，ForgeLink 不使用 |
| SSL | 当前关闭 |

ForgeLink 使用官方 `TDengine.Connector` 3.2.1 的 WebSocket 模式。运行时只访问 taosAdapter，不读取 `D:\TDengine` 下的 DLL、配置或数据文件，因此 TDengine 安装目录可以由部署人员调整。

## 版本兼容范围

| TDengine Server | ForgeLink 策略 |
| --- | --- |
| `3.3.6.0` 及以上 | 官方 C# WebSocket 兼容保证范围 |
| `3.0.0.0`–`3.3.5.x` | 尽力兼容；目标版本必须通过完整连接测试和真实读写验收 |
| 低于 `3.0.0.0` | 不支持，历史门禁拒绝启用 |

为了兼容更多 3.x 版本，首版只使用超级表、点位子表、基础 SQL、毫秒时间戳和 WebSocket 连接，不依赖 BLOB、DECIMAL、虚拟表或 Adapter HA 自动发现等较新能力。

## 需要准备的资源

真实联调前需要建立 ForgeLink 专用资源：

- 独立 Database，建议名为 `forgelink_history`；
- 独立数据库账号，不使用日常管理员账号；
- 建立超级表和子表的权限；
- INSERT、SELECT 和删除连接测试子表的权限；
- 如果启用 TLS，需要提供受信任的服务器证书链和对应主机名。

账号密码后续通过 Windows DPAPI 加密保存，不得写入仓库、普通日志或诊断包。

## 历史门禁测试

启用历史前，Collector Service 必须依次验证：

1. WebSocket 网络可达；
2. 用户名和密码认证成功；
3. Database 存在；
4. 服务端版本至少为 `3.0.0.0`；
5. 五类 ForgeLink 超级表可以幂等创建；
6. 唯一测试子表和测试点可以写入；
7. 测试点可以查询；
8. 测试子表可以清理。

任一步失败均不允许启用历史，也不会回退 SQLite 或补写未启用期间的数据。
