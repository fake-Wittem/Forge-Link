// 文件说明：使用 NModbus 本机从站验证 ForgeLink Modbus TCP 驱动的真实网络读取。
// 责任边界：仅监听随机回环端口，不连接现场 PLC 或固定网络地址。

using System.Net;
using System.Net.Sockets;
using ForgeLink.Domain;
using ForgeLink.Protocols.Modbus;
using NModbus;
using NModbus.Data;
using Xunit;

namespace ForgeLink.Protocol.Tests;

/// <summary>验证 NModbus 主从站之间的功能码 01 和 03 读取。</summary>
public sealed class ModbusTcpPlcDriverIntegrationTests
{
    /// <summary>确认驱动可批量读取线圈、16 位寄存器和 32 位浮点数。</summary>
    [Fact]
    public async Task ReadAsync_ShouldReadValuesFromNModbusTcpSlave()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ModbusFactory factory = new();
        DefaultSlaveDataStore store = new();
        store.HoldingRegisters.WritePoints(0, [0x4148, 0x0000, 1234]);
        store.CoilDiscretes.WritePoints(5, [true]);
        using IModbusSlaveNetwork network = factory.CreateSlaveNetwork(listener);
        network.AddSlave(factory.CreateSlave(1, store));
        Task server = network.ListenAsync(token);

        DeviceDefinition device = new(Guid.NewGuid(), "本机 Modbus 从站", "Modbus TCP", "127.0.0.1", port,
            true, 1000, 1, 2000, 2000);
        PointDefinition floatPoint = CreatePoint("FLOAT", "HR:0", PointDataType.Float);
        PointDefinition ushortPoint = CreatePoint("UINT16", "40003", PointDataType.UInt16);
        PointDefinition coilPoint = CreatePoint("COIL", "COIL:5", PointDataType.Boolean);
        await using ModbusTcpPlcDriver driver = new(device);
        await driver.ConnectAsync(token);
        IReadOnlyList<ForgeLink.Protocols.Abstractions.DriverTagValue> values = await driver.ReadAsync(
            [floatPoint, ushortPoint, coilPoint], token);

        Assert.All(values, value => Assert.Equal(DataQuality.Good, value.Quality));
        Assert.Equal(12.5F, values.Single(value => value.PointId == floatPoint.Id).RawValue);
        Assert.Equal((ushort)1234, values.Single(value => value.PointId == ushortPoint.Id).RawValue);
        Assert.Equal(true, values.Single(value => value.PointId == coilPoint.Id).RawValue);

        await driver.DisconnectAsync(token);
        listener.Stop();
        try { await server.WaitAsync(TimeSpan.FromSeconds(2), token); }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    /// <summary>创建本机集成测试点位。</summary>
    private static PointDefinition CreatePoint(string code, string address, PointDataType type) => new(
        Guid.NewGuid(), Guid.NewGuid(), code, code, address, type, 1, 0, "", 1000, 0,
        HistoryRecordMode.None, true, false);
}
