// 文件说明：把点位请求合并为符合 Modbus 数量限制的连续读取块。
// 责任边界：只规划读取区间，不执行 I/O 或解析寄存器值。

using ForgeLink.Domain;

namespace ForgeLink.Protocols.Modbus;

/// <summary>表示一个点位及其归一化地址和元素数量。</summary>
public sealed record ModbusPointRequest(PointDefinition Point, ModbusAddress Address, int ElementCount);

/// <summary>表示同一区域内一次连续 Modbus 读取。</summary>
public sealed record ModbusReadBlock(ModbusArea Area, ushort Start, ushort Count, IReadOnlyList<ModbusPointRequest> Points);

/// <summary>按区域、连续性和协议上限合并读取请求。</summary>
public sealed class ModbusReadPlanner
{
    /// <summary>生成确定性排序的连续读取块。</summary>
    public IReadOnlyList<ModbusReadBlock> Plan(IReadOnlyList<ModbusPointRequest> requests)
    {
        List<ModbusReadBlock> blocks = [];
        foreach (IGrouping<ModbusArea, ModbusPointRequest> areaGroup in requests.GroupBy(static request => request.Address.Area))
        {
            int maximum = areaGroup.Key is ModbusArea.Coil or ModbusArea.DiscreteInput ? 2000 : 125;
            List<ModbusPointRequest> current = [];
            int start = 0;
            int endExclusive = 0;
            foreach (ModbusPointRequest request in areaGroup.OrderBy(static request => request.Address.Offset))
            {
                int requestEnd = request.Address.Offset + request.ElementCount;
                bool canMerge = current.Count > 0
                    && request.Address.Offset <= endExclusive
                    && requestEnd - start <= maximum;
                if (!canMerge && current.Count > 0)
                {
                    blocks.Add(CreateBlock(areaGroup.Key, start, endExclusive, current));
                    current = [];
                }
                if (current.Count == 0)
                {
                    start = request.Address.Offset;
                    endExclusive = requestEnd;
                }
                else endExclusive = Math.Max(endExclusive, requestEnd);
                current.Add(request);
            }
            if (current.Count > 0) blocks.Add(CreateBlock(areaGroup.Key, start, endExclusive, current));
        }
        return blocks.OrderBy(static block => block.Area).ThenBy(static block => block.Start).ToArray();
    }

    /// <summary>创建已验证在 ushort 范围内的读取块。</summary>
    private static ModbusReadBlock CreateBlock(ModbusArea area, int start, int endExclusive, List<ModbusPointRequest> points) =>
        new(area, checked((ushort)start), checked((ushort)(endExclusive - start)), points.ToArray());
}
