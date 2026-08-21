// 文件说明：验证历史内存缓冲的容量、时效、重排和缺口统计。
// 责任边界：不持久化数据，不连接 TDengine。

using ForgeLink.Application;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Application.Tests;

public sealed class HistoryBufferTests
{
    [Fact]
    public void Enqueue_ShouldDropOldestAndTrackGap()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        HistoryBuffer buffer = new(new(MaximumValueCount: 2, MaximumAge: TimeSpan.FromMinutes(10), BatchSize: 2));
        buffer.Enqueue(CreateValue(start, 1));
        buffer.Enqueue(CreateValue(start.AddSeconds(1), 2));
        buffer.Enqueue(CreateValue(start.AddSeconds(2), 3));

        HistoryBufferSnapshot snapshot = buffer.Snapshot();
        Assert.Equal(2, snapshot.BufferedCount);
        Assert.Equal(1, snapshot.DroppedCount);
        Assert.Equal(start, snapshot.GapFromUtc);
        Assert.Equal([2L, 3L], buffer.TakeBatch(start.AddSeconds(2)).Select(static value => value.SequenceNumber));
    }

    [Fact]
    public void RequeueFront_ShouldPreserveChronologicalOrderAndExpireOldValues()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        HistoryBuffer buffer = new(new(MaximumValueCount: 4, MaximumAge: TimeSpan.FromSeconds(5), BatchSize: 2));
        PointValue first = CreateValue(start, 1);
        PointValue second = CreateValue(start.AddSeconds(1), 2);
        buffer.Enqueue(first);
        buffer.Enqueue(second);
        IReadOnlyList<PointValue> batch = buffer.TakeBatch(start.AddSeconds(1));
        buffer.Enqueue(CreateValue(start.AddSeconds(2), 3));
        buffer.RequeueFront(batch, start.AddSeconds(2));
        Assert.Equal([1L, 2L], buffer.TakeBatch(start.AddSeconds(2)).Select(static value => value.SequenceNumber));

        buffer.TakeBatch(start.AddSeconds(10));
        Assert.Equal(1, buffer.Snapshot().DroppedCount);
    }

    private static PointValue CreateValue(DateTimeOffset time, long sequence) => new(
        "test", Guid.Empty, Guid.NewGuid(), sequence, sequence, PointDataType.Int64, "", DataQuality.Good,
        null, time, sequence);
}
