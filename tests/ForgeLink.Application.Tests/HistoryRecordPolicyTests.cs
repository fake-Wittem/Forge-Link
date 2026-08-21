// 文件说明：验证点位历史记录模式、死区和心跳边界。
// 责任边界：不连接 TDengine，不启动采集服务。

using ForgeLink.Application;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Application.Tests;

public sealed class HistoryRecordPolicyTests
{
    [Fact]
    public void ChangeWithHeartbeat_ShouldApplyDeadbandHeartbeatAndQualityRules()
    {
        Guid pointId = Guid.NewGuid();
        PointDefinition definition = CreateDefinition(pointId, HistoryRecordMode.ChangeWithHeartbeat, 1D);
        HistoryRecordPolicy policy = new(TimeSpan.FromSeconds(60));
        DateTimeOffset start = DateTimeOffset.UtcNow;

        Assert.True(policy.ShouldRecord(definition, CreateValue(pointId, 10D, DataQuality.Good, start)));
        Assert.False(policy.ShouldRecord(definition, CreateValue(pointId, 10D, DataQuality.Good, start.AddSeconds(5))));
        Assert.False(policy.ShouldRecord(definition, CreateValue(pointId, 10.5D, DataQuality.Good, start.AddSeconds(10))));
        Assert.True(policy.ShouldRecord(definition, CreateValue(pointId, 10.5D, DataQuality.Good, start.AddSeconds(60))));
        Assert.True(policy.ShouldRecord(definition, CreateValue(pointId, null, DataQuality.Timeout, start.AddSeconds(61))));
    }

    [Fact]
    public void None_ShouldNeverRecordIncludingBadQuality()
    {
        Guid pointId = Guid.NewGuid();
        HistoryRecordPolicy policy = new();
        Assert.False(policy.ShouldRecord(
            CreateDefinition(pointId, HistoryRecordMode.None, 0),
            CreateValue(pointId, null, DataQuality.Bad, DateTimeOffset.UtcNow)));
    }

    private static PointDefinition CreateDefinition(Guid pointId, HistoryRecordMode mode, double deadband) => new(
        pointId, Guid.NewGuid(), "P1", "点位", "HR:0", PointDataType.Double, 1, 0, "", 1000,
        deadband, mode, true, false);

    private static PointValue CreateValue(Guid pointId, object? value, DataQuality quality, DateTimeOffset time) => new(
        "test", Guid.Empty, pointId, value, value, PointDataType.Double, "", quality, null, time, 1);
}
