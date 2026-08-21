// 文件说明：声明 PLC 协议驱动的统一生命周期和读写契约。
// 责任边界：不包含具体协议库类型或现场连接实现。

using ForgeLink.Domain;

namespace ForgeLink.Protocols.Abstractions;

/// <summary>表示驱动健康状态。</summary>
public sealed record DriverHealth(bool IsConnected, string Message, DateTimeOffset CheckedAtUtc);

/// <summary>表示驱动返回的点位原始值。</summary>
public sealed record DriverTagValue(Guid PointId, object? RawValue, DataQuality Quality, DateTimeOffset? SourceTimestampUtc);

/// <summary>表示受控 PLC 写入请求。</summary>
public sealed record TagWriteRequest(Guid PointId, string Address, object Value);

/// <summary>定义协议无关的 PLC 驱动。</summary>
public interface IPlcDriver : IAsyncDisposable
{
    /// <summary>建立设备连接。</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>断开设备连接。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>检查当前连接健康状态。</summary>
    Task<DriverHealth> CheckHealthAsync(CancellationToken cancellationToken);

    /// <summary>批量读取点位。</summary>
    Task<IReadOnlyList<DriverTagValue>> ReadAsync(IReadOnlyList<PointDefinition> points, CancellationToken cancellationToken);

    /// <summary>批量写入白名单点位。</summary>
    Task WriteAsync(IReadOnlyList<TagWriteRequest> requests, CancellationToken cancellationToken);
}

/// <summary>按设备配置创建设备驱动。</summary>
public interface IPlcDriverFactory
{
    /// <summary>为指定设备创建独立驱动实例。</summary>
    IPlcDriver Create(DeviceDefinition device);
}
