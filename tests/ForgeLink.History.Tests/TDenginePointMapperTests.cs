// 文件说明：验证 TDengine 超级表隔离、子表命名和字段映射。
// 责任边界：只检查本地映射，不连接 TDengine 服务。

using ForgeLink.Domain;
using ForgeLink.History.TDengine;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖 TDengine schema 映射的关键类型边界。</summary>
public sealed class TDenginePointMapperTests
{
    /// <summary>确认浮点值进入数值超级表且保留稳定标识。</summary>
    [Fact]
    public void Map_ShouldCreateNumericRowWithStableTags()
    {
        Guid deviceId = Guid.NewGuid();
        Guid pointId = Guid.NewGuid();
        PointValue value = CreateValue(deviceId, pointId, PointDataType.Double, 12.5D, 12.6D);

        TDenginePointRow row = TDenginePointMapper.Map(value);

        Assert.Equal("plc_numeric", row.StableName);
        Assert.Equal($"plc_numeric_{pointId:N}", row.ChildTableName);
        Assert.Equal(deviceId.ToString("D"), row.DeviceId);
        Assert.Equal(pointId.ToString("D"), row.PointId);
        Assert.Equal(12.6D, row.Value);
        Assert.Equal("Good", row.Quality);
    }

    /// <summary>确认整数、布尔和文本不会共享同一个字段类型。</summary>
    [Theory]
    [InlineData(PointDataType.Int32, 42, "plc_integer")]
    [InlineData(PointDataType.Boolean, true, "plc_boolean")]
    [InlineData(PointDataType.String, "RUN", "plc_text")]
    public void Map_ShouldSeparateStablesByDataType(PointDataType dataType, object value, string stable)
    {
        PointValue pointValue = CreateValue(Guid.NewGuid(), Guid.NewGuid(), dataType, value, value);
        Assert.Equal(stable, TDenginePointMapper.Map(pointValue).StableName);
    }

    /// <summary>确认无有效值的异常质量记录进入事件超级表，而不是伪造零值。</summary>
    [Fact]
    public void Map_ShouldUseEventStableForBadQualityWithoutValue()
    {
        PointValue value = CreateValue(Guid.NewGuid(), Guid.NewGuid(), PointDataType.Double, null, null) with
        {
            Quality = DataQuality.Timeout
        };

        TDenginePointRow row = TDenginePointMapper.Map(value);

        Assert.Equal("plc_event", row.StableName);
        Assert.Null(row.Value);
        Assert.Null(row.RawValue);
        Assert.Equal("Timeout", row.Quality);
    }

    private static PointValue CreateValue(Guid deviceId, Guid pointId, PointDataType type, object? raw, object? engineering) =>
        new("instance-01", deviceId, pointId, raw, engineering, type, "unit", DataQuality.Good,
            null, new DateTimeOffset(2026, 8, 20, 1, 2, 3, TimeSpan.Zero), 7);
}
