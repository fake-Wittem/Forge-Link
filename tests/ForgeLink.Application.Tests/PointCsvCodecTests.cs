// 文件说明：验证点位 CSV 的可重复导入导出及错误定位。
// 责任边界：只测试文本格式，不读写用户文件或配置数据库。

using ForgeLink.Application;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Application.Tests;

/// <summary>覆盖包含逗号、引号和换行字段的 CSV 边界。</summary>
public sealed class PointCsvCodecTests
{
    /// <summary>确认导出后的复杂文本字段能够无损解析。</summary>
    [Fact]
    public void ExportThenParse_ShouldRoundTripQuotedFields()
    {
        PointDefinition expected = new(Guid.NewGuid(), Guid.NewGuid(), "TEMP,01", "入口\"温度\"\r\n主测点",
            "D100", PointDataType.Double, 0.1, -2, "°C", 1000, 0.2,
            HistoryRecordMode.ChangeWithHeartbeat, true, false);
        PointCsvCodec codec = new();
        PointCsvParseResult result = codec.Parse(codec.Export([expected]));
        Assert.Empty(result.Errors);
        Assert.Equal(expected, Assert.Single(result.Points));
    }

    /// <summary>确认错误列数会携带准确行号而不是产生部分点位。</summary>
    [Fact]
    public void Parse_ShouldReportMalformedRow()
    {
        PointCsvCodec codec = new();
        string csv = "Id,DeviceId,Code,Name,Address,DataType,Scale,Offset,Unit,ScanIntervalMs,Deadband,HistoryMode,IsEnabled,AllowWrite,ByteOrder,WordOrder,StringLength\r\nonly-one-column";
        PointCsvParseResult result = codec.Parse(csv);
        Assert.Empty(result.Points);
        Assert.Contains("第 2 行", Assert.Single(result.Errors), StringComparison.Ordinal);
    }
}
