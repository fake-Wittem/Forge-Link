// 文件说明：实现线程安全的实时值内存缓存。
// 责任边界：只保存每个点位的最新值，不持久化历史数据。

using System.Collections.Concurrent;
using ForgeLink.Domain;

namespace ForgeLink.Application;

/// <summary>保存各点位最后一次采集结果。</summary>
public sealed class RealtimeValueStore
{
    private readonly ConcurrentDictionary<Guid, PointValue> _values = new();

    /// <summary>覆盖指定点位的最新值。</summary>
    public void Update(PointValue value) => _values[value.PointId] = value;

    /// <summary>批量覆盖实时值。</summary>
    public void Update(IEnumerable<PointValue> values)
    {
        foreach (PointValue value in values) Update(value);
    }

    /// <summary>返回按采集时间倒序排列的稳定快照。</summary>
    public IReadOnlyList<PointValue> Snapshot() => _values.Values
        .OrderByDescending(static value => value.CollectTimestampUtc)
        .ToArray();

    /// <summary>返回缓存中的点位数量。</summary>
    public int Count => _values.Count;

    /// <summary>移除已删除或禁用点位遗留的实时缓存。</summary>
    public void RetainOnly(IEnumerable<Guid> activePointIds)
    {
        HashSet<Guid> active = activePointIds.ToHashSet();
        foreach (Guid pointId in _values.Keys)
        {
            if (!active.Contains(pointId)) _values.TryRemove(pointId, out _);
        }
    }
}
