// 文件说明：验证 Modbus 寄存器的数值、字节序和字序转换。
// 责任边界：只处理内存数据，不依赖 NModbus 或网络环境。

using ForgeLink.Domain;
using ForgeLink.Protocols.Modbus;
using Xunit;

namespace ForgeLink.Protocol.Tests;

/// <summary>覆盖常见 PLC 寄存器排列方式。</summary>
public sealed class ModbusValueDecoderTests
{
    /// <summary>确认标准高字在前的浮点数正确转换。</summary>
    [Fact]
    public void DecodeRegisters_ShouldDecodeStandardFloat()
    {
        ModbusValueDecoder decoder = new();
        PointDefinition point = CreatePoint(PointDataType.Float);
        Assert.Equal(12.5F, decoder.DecodeRegisters([0x4148, 0x0000], 0, point, 2));
    }

    /// <summary>确认低字在前时先交换寄存器顺序。</summary>
    [Fact]
    public void DecodeRegisters_ShouldApplyLowWordFirst()
    {
        ModbusValueDecoder decoder = new();
        PointDefinition point = CreatePoint(PointDataType.Float) with { WordOrder = RegisterWordOrder.LowWordFirst };
        Assert.Equal(12.5F, decoder.DecodeRegisters([0x0000, 0x4148], 0, point, 2));
    }

    /// <summary>确认寄存器内部低字节在前时交换两个字节。</summary>
    [Fact]
    public void DecodeRegisters_ShouldApplyLittleEndianBytes()
    {
        ModbusValueDecoder decoder = new();
        PointDefinition point = CreatePoint(PointDataType.UInt16) with { ByteOrder = RegisterByteOrder.LittleEndian };
        Assert.Equal((ushort)0x1234, decoder.DecodeRegisters([0x3412], 0, point, 1));
    }

    /// <summary>确认 PLC 字符串在首个 NUL 结束，后续寄存器填充值不会进入历史文本。</summary>
    [Fact]
    public void DecodeRegisters_ShouldStopStringAtFirstNullByte()
    {
        ModbusValueDecoder decoder = new();
        PointDefinition point = CreatePoint(PointDataType.String) with { StringLength = 4 };

        object value = decoder.DecodeRegisters([0x002B, 0x4142], 0, point, 2);

        Assert.Equal(string.Empty, value);
    }

    /// <summary>创建默认寄存器顺序的测试点位。</summary>
    private static PointDefinition CreatePoint(PointDataType type) => new(
        Guid.NewGuid(), Guid.NewGuid(), "P", "P", "HR:0", type, 1, 0, "", 1000, 0,
        HistoryRecordMode.None, true, false);
}
