// 文件说明：声明应用层访问配置与运行状态所需的端口。
// 责任边界：接口不绑定 SQLite、Web 或桌面实现。

using ForgeLink.Domain;

namespace ForgeLink.Application;

/// <summary>定义设备与点位配置仓储。</summary>
public interface IConfigurationRepository
{
    /// <summary>初始化配置存储并执行版本迁移。</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>读取全部设备配置。</summary>
    Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken);

    /// <summary>新增或更新设备配置。</summary>
    Task SaveDeviceAsync(DeviceDefinition device, CancellationToken cancellationToken);

    /// <summary>删除没有关联点位的设备。</summary>
    /// <returns>删除成功返回 true；设备不存在或仍有关联点位时返回 false。</returns>
    Task<bool> DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>读取全部点位配置。</summary>
    Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken);

    /// <summary>新增或更新点位配置。</summary>
    Task SavePointAsync(PointDefinition point, CancellationToken cancellationToken);

    /// <summary>在单一事务中新增或更新多个点位。</summary>
    Task SavePointsAsync(IReadOnlyList<PointDefinition> points, CancellationToken cancellationToken);

    /// <summary>删除指定点位。</summary>
    /// <returns>找到并删除时返回 true，否则返回 false。</returns>
    Task<bool> DeletePointAsync(Guid pointId, CancellationToken cancellationToken);
}

/// <summary>通知采集运行时重新加载已提交的配置。</summary>
public interface IConfigurationChangeSignal
{
    /// <summary>获取当前配置修订号。</summary>
    long Revision { get; }

    /// <summary>发布一次配置已变化通知。</summary>
    void RequestReload();

    /// <summary>等待修订号超过调用方已观察到的版本。</summary>
    Task<long> WaitForChangeAsync(long observedRevision, CancellationToken cancellationToken);
}

/// <summary>提供只读的服务运行指标。</summary>
public interface ICollectorMetrics
{
    /// <summary>增加采集值总数并记录失败数量。</summary>
    void RecordCollection(int totalCount, int failedCount);

    /// <summary>更新已加载配置数量。</summary>
    void UpdateConfiguration(int deviceCount, int onlineDeviceCount, int enabledPointCount);

    /// <summary>设置采集处理队列的估算积压。</summary>
    void SetPipelineBacklog(int count);

    /// <summary>生成当前系统概览。</summary>
    SystemSnapshot CreateSnapshot(int realtimeValueCount, string historyStatus);
}
