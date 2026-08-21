// 文件说明：把统一点位值映射为 TDengine 超级表、子表和字段值。
// 责任边界：只生成确定性的 schema 映射，不发起网络请求。

using System.Globalization;
using ForgeLink.Domain;

namespace ForgeLink.History.TDengine;

/// <summary>表示一条可写入 TDengine 的标准化历史记录。</summary>
public sealed record TDenginePointRow(
    string StableName,
    string ChildTableName,
    long TimestampMilliseconds,
    object? Value,
    object? RawValue,
    string DataType,
    string Unit,
    string Quality,
    long? SourceTimestampMilliseconds,
    long CollectTimestampMilliseconds,
    long SequenceNumber,
    string InstanceId,
    string DeviceId,
    string PointId);

/// <summary>实现 ForgeLink 点位值到 TDengine 类型隔离表的映射。</summary>
public static class TDenginePointMapper
{
    /// <summary>把点位值映射为 TDengine 行，并为每个点位生成固定子表名。</summary>
    public static TDenginePointRow Map(PointValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string stableName = GetStableName(value);
        object? engineeringValue = stableName == "plc_event" ? null : NormalizeValue(value.EngineeringValue, value.DataType);
        object? rawValue = stableName == "plc_event" ? null : NormalizeValue(value.RawValue, value.DataType);
        return new(
            stableName,
            $"{stableName}_{value.PointId:N}",
            value.CollectTimestampUtc.ToUnixTimeMilliseconds(),
            engineeringValue,
            rawValue,
            value.DataType.ToString(),
            value.Unit ?? string.Empty,
            value.Quality.ToString(),
            value.SourceTimestampUtc?.ToUnixTimeMilliseconds(),
            value.CollectTimestampUtc.ToUnixTimeMilliseconds(),
            value.SequenceNumber,
            value.InstanceId,
            value.DeviceId.ToString("D"),
            value.PointId.ToString("D"));
    }

    /// <summary>按数据类型选择字段类型不会冲突的超级表。</summary>
    public static string GetStableName(PointValue value) => value.Quality != DataQuality.Good
        && value.EngineeringValue is null
            ? "plc_event"
            : value.DataType switch
            {
                PointDataType.Boolean => "plc_boolean",
                PointDataType.String => "plc_text",
                PointDataType.Int16 or PointDataType.UInt16 or PointDataType.Int32
                    or PointDataType.UInt32 or PointDataType.Int64 => "plc_integer",
                _ => "plc_numeric"
            };

    private static object? NormalizeValue(object? value, PointDataType dataType)
    {
        if (value is null) return null;
        return dataType switch
        {
            PointDataType.Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
            PointDataType.String => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            PointDataType.UInt16 or PointDataType.UInt32 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            PointDataType.Int16 or PointDataType.Int32 or PointDataType.Int64 => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
        };
    }
}
