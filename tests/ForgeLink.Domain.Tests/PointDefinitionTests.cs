// 文件说明：验证点位工程值换算与设备配置边界。
// 责任边界：仅测试纯领域行为，不连接设备或数据库。

using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Domain.Tests;

/// <summary>覆盖核心配置对象的正常和边界路径。</summary>
public sealed class PointDefinitionTests
{
    /// <summary>确认数值原始值按缩放和偏移换算。</summary>
    [Fact]
    public void ToEngineeringValue_ShouldApplyScaleAndOffset()
    {
        PointDefinition point = CreatePoint(scale: 0.1, offset: -5);
        object? result = point.ToEngineeringValue(250);
        Assert.Equal(20D, result);
    }

    /// <summary>确认布尔量不会被错误转换为数值。</summary>
    [Fact]
    public void ToEngineeringValue_ShouldKeepBoolean()
    {
        PointDefinition point = CreatePoint(scale: 10, offset: 3);
        Assert.Equal(true, point.ToEngineeringValue(true));
    }

    /// <summary>确认无效端口和过短周期会产生明确错误。</summary>
    [Fact]
    public void Validate_ShouldRejectInvalidDeviceConfiguration()
    {
        DeviceDefinition device = new(Guid.Empty, "", "", "", 70000, true, 50);
        Assert.Equal(6, device.Validate().Count);
    }

    /// <summary>确认点位校验会同时返回标识、地址、周期和数值边界错误。</summary>
    [Fact]
    public void Validate_ShouldRejectInvalidPointConfiguration()
    {
        PointDefinition point = new(Guid.Empty, Guid.Empty, "", "", "", PointDataType.Double,
            double.NaN, double.PositiveInfinity, "", 50, -1, HistoryRecordMode.None, true, false);
        Assert.Equal(9, point.Validate().Count);
    }

    /// <summary>创建测试所需的最小点位定义。</summary>
    private static PointDefinition CreatePoint(double scale, double offset) => new(
        Guid.NewGuid(), Guid.NewGuid(), "P1", "测试点位", "D0", PointDataType.Double,
        scale, offset, "", 1000, 0, HistoryRecordMode.None, true, false);
}
