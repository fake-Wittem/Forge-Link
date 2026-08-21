// 文件说明：定义桌面端从 Collector Service 读取的只读传输模型。
// 责任边界：仅用于本机 API 序列化，不作为领域持久化模型。

using ForgeLink.Domain;

namespace ForgeLink.Desktop.Models;

/// <summary>表示服务概览响应。</summary>
public sealed record StatusDto(
    DateTimeOffset StartedAtUtc,
    int DeviceCount,
    int OnlineDeviceCount,
    int EnabledPointCount,
    long CollectedValueCount,
    long FailedValueCount,
    int RealtimeValueCount,
    string HistoryStatus,
    int PipelineBacklog,
    double SuccessRate);

/// <summary>表示实时页面中的一行点位数据。</summary>
public sealed record RealtimeValueDto(
    Guid DeviceId,
    Guid PointId,
    object? RawValue,
    object? EngineeringValue,
    PointDataType DataType,
    string Unit,
    DataQuality Quality,
    DateTimeOffset CollectTimestampUtc,
    long SequenceNumber);

/// <summary>表示设备连接测试响应。</summary>
public sealed record DeviceConnectionTestDto(bool Succeeded, string Message, DateTimeOffset TestedAtUtc);

/// <summary>表示点位 CSV 导入响应。</summary>
public sealed record ImportResultDto(int ImportedCount);
