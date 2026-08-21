// 文件说明：验证 InfluxDB 3 Measurement 隔离和字段映射。
// 责任边界：只检查官方客户端 PointData，不连接 InfluxDB 服务。

using ForgeLink.Domain;
using ForgeLink.History.InfluxDb3;
using InfluxDB3.Client.Write;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖 InfluxDB 3 schema 映射的关键类型边界。</summary>
public sealed class InfluxDb3PointMapperTests
{
    /// <summary>确认浮点值进入数值 Measurement 且保留稳定标识。</summary>
    [Fact]
    public void Map_ShouldCreateNumericPointWithStableTags()
    {
        Guid deviceId = Guid.NewGuid();
        Guid pointId = Guid.NewGuid();
        PointValue value = CreateValue(deviceId, pointId, PointDataType.Double, 12.5D, 12.6D);

        PointData point = InfluxDb3PointMapper.Map(value);

        Assert.Equal("plc_numeric", point.GetMeasurement());
        Assert.Equal(deviceId.ToString("D"), point.GetTag("device_id"));
        Assert.Equal(pointId.ToString("D"), point.GetTag("point_id"));
        Assert.Equal(12.6D, point.GetDoubleField("value"));
        Assert.Equal("Good", point.GetStringField("quality"));
    }

    /// <summary>确认整数、布尔和文本不会共享同一个 Field schema。</summary>
    [Theory]
    [InlineData(PointDataType.Int32, 42, "plc_integer")]
    [InlineData(PointDataType.Boolean, true, "plc_boolean")]
    [InlineData(PointDataType.String, "RUN", "plc_text")]
    public void Map_ShouldSeparateMeasurementsByDataType(PointDataType dataType, object value, string measurement)
    {
        PointValue pointValue = CreateValue(Guid.NewGuid(), Guid.NewGuid(), dataType, value, value);
        Assert.Equal(measurement, InfluxDb3PointMapper.Map(pointValue).GetMeasurement());
    }

    /// <summary>确认无有效值的异常质量记录进入事件 Measurement，而不是伪造零值。</summary>
    [Fact]
    public void Map_ShouldUseEventMeasurementForBadQualityWithoutValue()
    {
        PointValue value = CreateValue(Guid.NewGuid(), Guid.NewGuid(), PointDataType.Double, null, null) with
        {
            Quality = DataQuality.Timeout
        };
        PointData point = InfluxDb3PointMapper.Map(value);
        Assert.Equal("plc_event", point.GetMeasurement());
        Assert.Equal(string.Empty, point.GetStringField("value"));
        Assert.NotEqual(0D, point.GetField("value"));
        Assert.Equal("Timeout", point.GetStringField("quality"));
    }

    private static PointValue CreateValue(Guid deviceId, Guid pointId, PointDataType type, object? raw, object? engineering) =>
        new("instance-01", deviceId, pointId, raw, engineering, type, "unit", DataQuality.Good,
            null, new DateTimeOffset(2026, 8, 20, 1, 2, 3, TimeSpan.Zero), 7);
}
