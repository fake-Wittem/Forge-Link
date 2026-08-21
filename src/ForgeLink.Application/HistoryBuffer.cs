// 文件说明：提供 TDengine 故障期间的有界进程内历史缓冲和缺口统计。
// 责任边界：不持久化历史，不决定点位记录策略，也不执行网络写入。

using ForgeLink.Domain;

namespace ForgeLink.Application;

public sealed record HistoryBufferOptions(
    int MaximumValueCount = 100_000,
    TimeSpan? MaximumAge = null,
    int BatchSize = 1_000,
    TimeSpan? FlushInterval = null,
    TimeSpan? InitialRetryInterval = null,
    TimeSpan? MaximumRetryInterval = null)
{
    public TimeSpan EffectiveMaximumAge => MaximumAge ?? TimeSpan.FromMinutes(10);
    public TimeSpan EffectiveFlushInterval => FlushInterval ?? TimeSpan.FromSeconds(1);
    public TimeSpan EffectiveInitialRetryInterval => InitialRetryInterval ?? TimeSpan.FromSeconds(2);
    public TimeSpan EffectiveMaximumRetryInterval => MaximumRetryInterval ?? TimeSpan.FromSeconds(60);
}

public sealed record HistoryBufferSnapshot(
    int BufferedCount,
    long DroppedCount,
    DateTimeOffset? GapFromUtc,
    DateTimeOffset? GapToUtc);

/// <summary>按最旧优先丢弃维护固定容量，并累计本进程历史缺口。</summary>
public sealed class HistoryBuffer
{
    private readonly object _sync = new();
    private readonly LinkedList<PointValue> _values = [];
    private long _droppedCount;
    private DateTimeOffset? _gapFromUtc;
    private DateTimeOffset? _gapToUtc;

    public HistoryBufferOptions Options { get; }

    public HistoryBuffer(HistoryBufferOptions? options = null)
    {
        Options = options ?? new();
        if (Options.MaximumValueCount < 1) throw new ArgumentOutOfRangeException(nameof(options));
        if (Options.BatchSize is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public void Enqueue(PointValue value)
    {
        lock (_sync)
        {
            _values.AddLast(value);
            DropExpiredCore(value.CollectTimestampUtc);
            while (_values.Count > Options.MaximumValueCount) DropFirstCore();
        }
    }

    public IReadOnlyList<PointValue> TakeBatch(DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            DropExpiredCore(nowUtc);
            List<PointValue> batch = new(Math.Min(Options.BatchSize, _values.Count));
            while (batch.Count < Options.BatchSize && _values.First is not null)
            {
                batch.Add(_values.First.Value);
                _values.RemoveFirst();
            }
            return batch;
        }
    }

    public void RequeueFront(IReadOnlyList<PointValue> values, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            for (int index = values.Count - 1; index >= 0; index--) _values.AddFirst(values[index]);
            DropExpiredCore(nowUtc);
            while (_values.Count > Options.MaximumValueCount) DropFirstCore();
        }
    }

    public int DiscardAll()
    {
        lock (_sync)
        {
            int count = _values.Count;
            while (_values.First is not null) DropFirstCore();
            return count;
        }
    }

    public HistoryBufferSnapshot Snapshot()
    {
        lock (_sync) return new(_values.Count, _droppedCount, _gapFromUtc, _gapToUtc);
    }

    private void DropExpiredCore(DateTimeOffset nowUtc)
    {
        DateTimeOffset threshold = nowUtc - Options.EffectiveMaximumAge;
        while (_values.First is not null && _values.First.Value.CollectTimestampUtc < threshold) DropFirstCore();
    }

    private void DropFirstCore()
    {
        PointValue dropped = _values.First!.Value;
        _values.RemoveFirst();
        _droppedCount++;
        _gapFromUtc = _gapFromUtc is null || dropped.CollectTimestampUtc < _gapFromUtc ? dropped.CollectTimestampUtc : _gapFromUtc;
        _gapToUtc = _gapToUtc is null || dropped.CollectTimestampUtc > _gapToUtc ? dropped.CollectTimestampUtc : _gapToUtc;
    }
}
