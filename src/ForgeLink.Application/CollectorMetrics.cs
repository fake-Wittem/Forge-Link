// 文件说明：实现低开销的采集运行指标聚合。
// 责任边界：只维护进程内计数，不承担长期指标存储。

using ForgeLink.Domain;

namespace ForgeLink.Application;

/// <summary>通过原子计数维护服务概览指标。</summary>
public sealed class CollectorMetrics : ICollectorMetrics
{
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private long _collected;
    private long _failed;
    private int _devices;
    private int _onlineDevices;
    private int _points;
    private int _backlog;

    /// <inheritdoc />
    public void RecordCollection(int totalCount, int failedCount)
    {
        Interlocked.Add(ref _collected, totalCount);
        Interlocked.Add(ref _failed, failedCount);
    }

    /// <inheritdoc />
    public void UpdateConfiguration(int deviceCount, int onlineDeviceCount, int enabledPointCount)
    {
        Volatile.Write(ref _devices, deviceCount);
        Volatile.Write(ref _onlineDevices, onlineDeviceCount);
        Volatile.Write(ref _points, enabledPointCount);
    }

    /// <inheritdoc />
    public void SetPipelineBacklog(int count) => Volatile.Write(ref _backlog, count);

    /// <inheritdoc />
    public SystemSnapshot CreateSnapshot(int realtimeValueCount, string historyStatus) => new(
        _startedAtUtc,
        Volatile.Read(ref _devices),
        Volatile.Read(ref _onlineDevices),
        Volatile.Read(ref _points),
        Interlocked.Read(ref _collected),
        Interlocked.Read(ref _failed),
        realtimeValueCount,
        historyStatus,
        Volatile.Read(ref _backlog));
}
