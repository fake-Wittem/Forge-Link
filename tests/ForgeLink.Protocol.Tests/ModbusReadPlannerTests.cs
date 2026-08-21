// 文件说明：验证连续 Modbus 点位的批量读取规划。
// 责任边界：只检查读取区间，不执行 NModbus 网络调用。

using ForgeLink.Domain;
using ForgeLink.Protocols.Modbus;
using Xunit;

namespace ForgeLink.Protocol.Tests;

/// <summary>覆盖连续合并、区域隔离和空洞拆分。</summary>
public sealed class ModbusReadPlannerTests
{
    /// <summary>确认相邻寄存器合并，而不同区域或有空洞的地址分块。</summary>
    [Fact]
    public void Plan_ShouldMergeOnlyContiguousRequestsInSameArea()
    {
        ModbusReadPlanner planner = new();
        IReadOnlyList<ModbusReadBlock> blocks = planner.Plan(
        [
            CreateRequest("A", ModbusArea.HoldingRegister, 0, PointDataType.Float, 2),
            CreateRequest("B", ModbusArea.HoldingRegister, 2, PointDataType.UInt16, 1),
            CreateRequest("C", ModbusArea.HoldingRegister, 10, PointDataType.UInt16, 1),
            CreateRequest("D", ModbusArea.InputRegister, 0, PointDataType.UInt16, 1)
        ]);
        Assert.Equal(3, blocks.Count);
        ModbusReadBlock first = Assert.Single(blocks, block => block.Area == ModbusArea.HoldingRegister && block.Start == 0);
        Assert.Equal(3, first.Count);
        Assert.Equal(2, first.Points.Count);
    }

    /// <summary>创建读取规划测试请求。</summary>
    private static ModbusPointRequest CreateRequest(string code, ModbusArea area, ushort offset, PointDataType type, int count)
    {
        PointDefinition point = new(Guid.NewGuid(), Guid.NewGuid(), code, code, $"HR:{offset}", type,
            1, 0, "", 1000, 0, HistoryRecordMode.None, true, false);
        return new(point, new ModbusAddress(area, offset), count);
    }
}
