// 文件说明：使用 NModbus 实现 Modbus TCP 连接、批量读取和质量映射。
// 责任边界：NModbus 负责协议帧；本类负责 ForgeLink 生命周期、超时、规划和领域值转换。

using System.Net.Sockets;
using ForgeLink.Domain;
using ForgeLink.Protocols.Abstractions;
using NModbus;

namespace ForgeLink.Protocols.Modbus;

/// <summary>通过单连接串行执行 Modbus TCP 请求的 PLC 驱动。</summary>
public sealed class ModbusTcpPlcDriver : IPlcDriver
{
    private readonly DeviceDefinition _device;
    private readonly ModbusAddressParser _addressParser = new();
    private readonly ModbusReadPlanner _planner = new();
    private readonly ModbusValueDecoder _decoder = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private TcpClient? _tcpClient;
    private IModbusMaster? _master;

    /// <summary>使用设备配置创建尚未连接的驱动实例。</summary>
    public ModbusTcpPlcDriver(DeviceDefinition device) => _device = device;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ResetConnection();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DriverHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool connected = IsSocketConnected();
            return new(connected,
                connected ? $"Modbus TCP 已连接，Unit ID {_device.UnitId}" : "Modbus TCP 未连接",
                DateTimeOffset.UtcNow);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriverTagValue>> ReadAsync(
        IReadOnlyList<PointDefinition> points,
        CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsSocketConnected())
            {
                try
                {
                    await ConnectCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return CreateFailedValues(points, DataQuality.Disconnected);
                }
            }

            Dictionary<Guid, DriverTagValue> results = [];
            List<ModbusPointRequest> requests = [];
            foreach (PointDefinition point in points)
            {
                if (!_addressParser.TryParse(point.Address, out ModbusAddress address, out _))
                {
                    results[point.Id] = new(point.Id, null, DataQuality.ParseError, null);
                    continue;
                }
                int count = _addressParser.GetElementCount(point, address.Area);
                if (address.Area is ModbusArea.Coil or ModbusArea.DiscreteInput && point.DataType != PointDataType.Boolean)
                {
                    results[point.Id] = new(point.Id, null, DataQuality.ParseError, null);
                    continue;
                }
                if (address.Offset + count > ushort.MaxValue + 1)
                {
                    results[point.Id] = new(point.Id, null, DataQuality.OutOfRange, null);
                    continue;
                }
                requests.Add(new(point, address, count));
            }

            foreach (ModbusReadBlock block in _planner.Plan(requests))
            {
                if (_master is null)
                {
                    AddFailedBlock(results, block, DataQuality.Disconnected);
                    continue;
                }
                try
                {
                    ModbusBlockData data = await ReadBlockAsync(block, cancellationToken).ConfigureAwait(false);
                    DecodeBlock(results, block, data);
                }
                catch (SlaveException exception) when (exception.SlaveExceptionCode == SlaveExceptionCodes.IllegalDataAddress)
                {
                    AddFailedBlock(results, block, DataQuality.OutOfRange);
                }
                catch (SlaveException)
                {
                    AddFailedBlock(results, block, DataQuality.Bad);
                }
                catch (TimeoutException)
                {
                    AddFailedBlock(results, block, DataQuality.Timeout);
                    ResetConnection();
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
                {
                    AddFailedBlock(results, block, DataQuality.Disconnected);
                    ResetConnection();
                }
            }

            return points.Select(point => results.TryGetValue(point.Id, out DriverTagValue? value)
                ? value
                : new DriverTagValue(point.Id, null, DataQuality.Bad, null)).ToArray();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<TagWriteRequest> requests, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Modbus TCP 写入尚未通过应用层白名单授权，驱动保持只读。 ");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ResetConnection();
        }
        finally
        {
            _ioLock.Release();
        }
        _ioLock.Dispose();
    }

    /// <summary>在已持有 I/O 锁时建立 TCP 连接并创建 NModbus 主站。</summary>
    private async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        ResetConnection();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_device.ConnectionTimeoutMs));
        TcpClient client = new() { NoDelay = true };
        try
        {
            await client.ConnectAsync(_device.Host, _device.Port, timeout.Token).ConfigureAwait(false);
            client.ReceiveTimeout = _device.ReadTimeoutMs;
            client.SendTimeout = _device.ReadTimeoutMs;
            IModbusMaster master = new ModbusFactory().CreateMaster(client);
            master.Transport.ReadTimeout = _device.ReadTimeoutMs;
            master.Transport.WriteTimeout = _device.ReadTimeoutMs;
            master.Transport.Retries = 0;
            _tcpClient = client;
            _master = master;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>按功能区调用 NModbus 异步读取方法并施加可取消超时。</summary>
    private async Task<ModbusBlockData> ReadBlockAsync(ModbusReadBlock block, CancellationToken cancellationToken)
    {
        IModbusMaster master = _master ?? throw new InvalidOperationException("Modbus TCP 尚未连接。");
        TimeSpan timeout = TimeSpan.FromMilliseconds(_device.ReadTimeoutMs);
        return block.Area switch
        {
            ModbusArea.Coil => new(await master.ReadCoilsAsync(_device.UnitId, block.Start, block.Count)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false), null),
            ModbusArea.DiscreteInput => new(await master.ReadInputsAsync(_device.UnitId, block.Start, block.Count)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false), null),
            ModbusArea.HoldingRegister => new(null, await master.ReadHoldingRegistersAsync(_device.UnitId, block.Start, block.Count)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false)),
            ModbusArea.InputRegister => new(null, await master.ReadInputRegistersAsync(_device.UnitId, block.Start, block.Count)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false)),
            _ => throw new ArgumentOutOfRangeException(nameof(block), block.Area, "未知 Modbus 区域。")
        };
    }

    /// <summary>把一个读取块拆分并转换为各点位原始值。</summary>
    private void DecodeBlock(Dictionary<Guid, DriverTagValue> results, ModbusReadBlock block, ModbusBlockData data)
    {
        foreach (ModbusPointRequest request in block.Points)
        {
            try
            {
                int index = request.Address.Offset - block.Start;
                object raw = data.Bits is not null
                    ? _decoder.DecodeBit(data.Bits, index)
                    : _decoder.DecodeRegisters(data.Registers!, index, request.Point, request.ElementCount);
                results[request.Point.Id] = new(request.Point.Id, raw, DataQuality.Good, null);
            }
            catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                results[request.Point.Id] = new(request.Point.Id, null, DataQuality.ParseError, null);
            }
        }
    }

    /// <summary>为读取块中的全部点位写入同一失败质量。</summary>
    private static void AddFailedBlock(Dictionary<Guid, DriverTagValue> results, ModbusReadBlock block, DataQuality quality)
    {
        foreach (ModbusPointRequest request in block.Points)
            results[request.Point.Id] = new(request.Point.Id, null, quality, null);
    }

    /// <summary>为一组点位创建不伪装为零的失败结果。</summary>
    private static IReadOnlyList<DriverTagValue> CreateFailedValues(IReadOnlyList<PointDefinition> points, DataQuality quality) =>
        points.Select(point => new DriverTagValue(point.Id, null, quality, null)).ToArray();

    /// <summary>检查 TCP 套接字是否仍处于可用连接状态。</summary>
    private bool IsSocketConnected()
    {
        if (_tcpClient?.Connected != true) return false;
        Socket socket = _tcpClient.Client;
        return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
    }

    /// <summary>释放 NModbus 主站和底层 TCP 连接并清空状态。</summary>
    private void ResetConnection()
    {
        _master?.Dispose();
        _master = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    /// <summary>承载单次读取返回的位数组或寄存器数组。</summary>
    private sealed record ModbusBlockData(bool[]? Bits, ushort[]? Registers);
}
