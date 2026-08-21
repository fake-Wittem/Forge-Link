// 文件说明：定义 ForgeLink 的核心设备、点位和值对象。
// 责任边界：仅表达领域数据和不变量，不依赖协议、存储或界面实现。

namespace ForgeLink.Domain;

/// <summary>表示 PLC 或工业设备配置。</summary>
public sealed record DeviceDefinition(
    Guid Id,
    string Name,
    string Protocol,
    string Host,
    int Port,
    bool IsEnabled,
    int DefaultScanIntervalMs,
    byte UnitId = 1,
    int ConnectionTimeoutMs = 3000,
    int ReadTimeoutMs = 2000)
{
    /// <summary>校验设备配置并返回错误集合。</summary>
    /// <returns>可直接展示的配置错误；空集合表示有效。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (Id == Guid.Empty) errors.Add("设备 ID 不能为空。");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("设备名称不能为空。");
        if (string.IsNullOrWhiteSpace(Protocol)) errors.Add("协议类型不能为空。");
        if (string.IsNullOrWhiteSpace(Host)) errors.Add("设备地址不能为空。");
        if (Port is < 1 or > 65535) errors.Add("端口必须在 1 到 65535 之间。");
        if (DefaultScanIntervalMs < 100) errors.Add("默认采集周期不得小于 100 毫秒。");
        if (ConnectionTimeoutMs < 100) errors.Add("连接超时不得小于 100 毫秒。");
        if (ReadTimeoutMs < 100) errors.Add("读取超时不得小于 100 毫秒。");
        return errors;
    }
}

/// <summary>表示点位配置及工程值转换规则。</summary>
public sealed record PointDefinition(
    Guid Id,
    Guid DeviceId,
    string Code,
    string Name,
    string Address,
    PointDataType DataType,
    double Scale,
    double Offset,
    string Unit,
    int ScanIntervalMs,
    double Deadband,
    HistoryRecordMode HistoryMode,
    bool IsEnabled,
    bool AllowWrite,
    RegisterByteOrder ByteOrder = RegisterByteOrder.BigEndian,
    RegisterWordOrder WordOrder = RegisterWordOrder.HighWordFirst,
    int StringLength = 0)
{
    /// <summary>校验点位配置并返回全部可修正错误。</summary>
    /// <returns>可直接展示的配置错误；空集合表示有效。</returns>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (Id == Guid.Empty) errors.Add("点位 ID 不能为空。");
        if (DeviceId == Guid.Empty) errors.Add("所属设备 ID 不能为空。");
        if (string.IsNullOrWhiteSpace(Code)) errors.Add("点位编码不能为空。");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("点位名称不能为空。");
        if (string.IsNullOrWhiteSpace(Address)) errors.Add("协议地址不能为空。");
        if (!double.IsFinite(Scale)) errors.Add("缩放系数必须是有限数值。");
        if (!double.IsFinite(Offset)) errors.Add("偏移量必须是有限数值。");
        if (ScanIntervalMs < 100) errors.Add("采集周期不得小于 100 毫秒。");
        if (!double.IsFinite(Deadband) || Deadband < 0) errors.Add("死区值必须是大于或等于零的有限数值。");
        if (StringLength < 0) errors.Add("字符串长度不得小于零。");
        if (StringLength > 250) errors.Add("Modbus 单次读取的字符串长度不得超过 250 字节。");
        return errors;
    }

    /// <summary>把可转换的原始值换算为工程值。</summary>
    /// <param name="rawValue">驱动解析后的原始值。</param>
    /// <returns>数值类型返回换算结果，非数值类型保持原值。</returns>
    public object? ToEngineeringValue(object? rawValue)
    {
        if (rawValue is null || rawValue is bool || rawValue is string) return rawValue;
        return Convert.ToDouble(rawValue, System.Globalization.CultureInfo.InvariantCulture) * Scale + Offset;
    }
}

/// <summary>定义点位的数据类型。</summary>
public enum PointDataType { Boolean, Int16, UInt16, Int32, UInt32, Int64, Float, Double, String }

/// <summary>定义 16 位寄存器内部两个字节的排列方式。</summary>
public enum RegisterByteOrder { BigEndian, LittleEndian }

/// <summary>定义多寄存器值的 16 位字排列方式。</summary>
public enum RegisterWordOrder { HighWordFirst, LowWordFirst }

/// <summary>定义统一的数据质量状态。</summary>
public enum DataQuality { Good, Uncertain, Bad, Timeout, Disconnected, ParseError, OutOfRange, Stale }

/// <summary>定义点位历史记录策略。</summary>
public enum HistoryRecordMode { None, EverySample, OnChange, Deadband, PeriodicSnapshot, ChangeWithHeartbeat }

/// <summary>表示一条经过标准化处理的实时点位值。</summary>
public sealed record PointValue(
    string InstanceId,
    Guid DeviceId,
    Guid PointId,
    object? RawValue,
    object? EngineeringValue,
    PointDataType DataType,
    string Unit,
    DataQuality Quality,
    DateTimeOffset? SourceTimestampUtc,
    DateTimeOffset CollectTimestampUtc,
    long SequenceNumber);

/// <summary>表示服务概览所需的运行状态快照。</summary>
public sealed record SystemSnapshot(
    DateTimeOffset StartedAtUtc,
    int DeviceCount,
    int OnlineDeviceCount,
    int EnabledPointCount,
    long CollectedValueCount,
    long FailedValueCount,
    int RealtimeValueCount,
    string HistoryStatus,
    int PipelineBacklog)
{
    /// <summary>计算采集成功率。</summary>
    public double SuccessRate => CollectedValueCount == 0
        ? 1D
        : (CollectedValueCount - FailedValueCount) / (double)CollectedValueCount;
}
