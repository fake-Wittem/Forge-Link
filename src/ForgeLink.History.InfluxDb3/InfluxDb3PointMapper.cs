// 文件说明：把统一点位值映射为 InfluxDB 3 PointData。
// 责任边界：保持不同数据类型的 Field schema 隔离，不发起网络请求。

using ForgeLink.Domain;
using InfluxDB3.Client.Write;

namespace ForgeLink.History.InfluxDb3;

/// <summary>实现 ForgeLink 历史 Measurement 和字段映射。</summary>
public static class InfluxDb3PointMapper
{
    /// <summary>把点位值映射为官方客户端的 PointData。</summary>
    public static PointData Map(PointValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string measurement = GetMeasurement(value);
        PointData point = PointData.Measurement(measurement)
            .SetTag("instance_id", value.InstanceId)
            .SetTag("device_id", value.DeviceId.ToString("D"))
            .SetTag("point_id", value.PointId.ToString("D"))
            .SetField("quality", value.Quality.ToString())
            .SetField("data_type", value.DataType.ToString())
            .SetField("unit", value.Unit ?? string.Empty)
            .SetField("collect_timestamp", value.CollectTimestampUtc.UtcDateTime.ToString("O"))
            .SetField("sequence_number", value.SequenceNumber)
            .SetTimestamp(value.CollectTimestampUtc);

        point.SetField("source_timestamp", value.SourceTimestampUtc is DateTimeOffset sourceTimestamp
            ? sourceTimestamp.UtcDateTime.ToString("O")
            : string.Empty);
        if (measurement == "plc_event")
        {
            point.SetField("value", string.Empty);
            point.SetField("raw_value", string.Empty);
        }
        else
        {
            SetValue(point, "value", value.EngineeringValue, value.DataType);
            SetValue(point, "raw_value", value.RawValue, value.DataType);
        }
        return point;
    }

    /// <summary>按字段类型选择不会发生类型冲突的 Measurement。</summary>
    public static string GetMeasurement(PointValue value) => value.Quality != DataQuality.Good
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

    private static void SetValue(PointData point, string field, object? value, PointDataType dataType)
    {
        if (value is null) return;
        switch (dataType)
        {
            case PointDataType.Boolean:
                point.SetField(field, Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case PointDataType.String:
                point.SetField(field, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                break;
            case PointDataType.UInt16 or PointDataType.UInt32:
                point.SetField(field, Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case PointDataType.Int16 or PointDataType.Int32 or PointDataType.Int64:
                point.SetField(field, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                point.SetField(field, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }
}
