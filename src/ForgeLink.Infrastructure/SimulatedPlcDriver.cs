// 文件说明：提供无现场设备环境下可运行的模拟 PLC 驱动。
// 责任边界：仅用于演示和自动化测试，不声称验证真实协议通信。

using ForgeLink.Domain;
using ForgeLink.Protocols.Abstractions;
using ForgeLink.Protocols.Modbus;

namespace ForgeLink.Infrastructure;

/// <summary>生成稳定趋势与微小随机波动的模拟驱动。</summary>
public sealed class SimulatedPlcDriver : IPlcDriver
{
    private readonly Random _random = new();
    private bool _connected;
    private double _phase;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DriverHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(
        new DriverHealth(_connected, _connected ? "模拟设备在线" : "模拟设备未连接", DateTimeOffset.UtcNow));

    /// <inheritdoc />
    public Task<IReadOnlyList<DriverTagValue>> ReadAsync(IReadOnlyList<PointDefinition> points, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_connected)
        {
            return Task.FromResult<IReadOnlyList<DriverTagValue>>(points
                .Select(static point => new DriverTagValue(point.Id, null, DataQuality.Disconnected, null)).ToArray());
        }

        _phase += 0.12;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DriverTagValue[] values = points.Select((point, index) => new DriverTagValue(
            point.Id,
            CreateRawValue(point, index),
            DataQuality.Good,
            now)).ToArray();
        return Task.FromResult<IReadOnlyList<DriverTagValue>>(values);
    }

    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<TagWriteRequest> requests, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("模拟驱动未启用 PLC 写入；系统默认禁止写操作。");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _connected = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>依据点位类型生成演示原始值。</summary>
    private object CreateRawValue(PointDefinition point, int index) => point.DataType switch
    {
        PointDataType.Boolean => Math.Sin(_phase) > -0.35,
        PointDataType.String => "SIM-OK",
        _ => 220 + (index * 70) + Math.Sin(_phase + index) * 18 + _random.NextDouble() * 2
    };
}

/// <summary>仅为 Simulation 协议创建设备驱动。</summary>
public sealed class SimulatedPlcDriverFactory : IPlcDriverFactory
{
    /// <inheritdoc />
    public IPlcDriver Create(DeviceDefinition device)
    {
        if (!string.Equals(device.Protocol, "Simulation", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"首版演示运行时尚未注册协议 {device.Protocol} 的现场驱动。");
        return new SimulatedPlcDriver();
    }
}

/// <summary>根据设备协议选择已注册的模拟或 Modbus TCP 驱动。</summary>
public sealed class PlcDriverFactory : IPlcDriverFactory
{
    /// <inheritdoc />
    public IPlcDriver Create(DeviceDefinition device)
    {
        string protocol = device.Protocol.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (string.Equals(protocol, "Simulation", StringComparison.OrdinalIgnoreCase)) return new SimulatedPlcDriver();
        if (string.Equals(protocol, "ModbusTcp", StringComparison.OrdinalIgnoreCase)) return new ModbusTcpPlcDriver(device);
        throw new NotSupportedException($"尚未注册协议 {device.Protocol} 的 PLC 驱动。");
    }
}
