// 文件说明：验证 Modbus 显式地址、参考地址和错误输入解析。
// 责任边界：不创建网络连接或依赖具体 PLC 型号。

using ForgeLink.Protocols.Modbus;
using Xunit;

namespace ForgeLink.Protocol.Tests;

/// <summary>覆盖 ForgeLink 支持的 Modbus 地址规范。</summary>
public sealed class ModbusAddressParserTests
{
    /// <summary>确认显式零基格式和五位参考格式映射到相同区域偏移。</summary>
    [Theory]
    [InlineData("0", ModbusArea.HoldingRegister, 0)]
    [InlineData("2", ModbusArea.HoldingRegister, 2)]
    [InlineData("65535", ModbusArea.HoldingRegister, 65535)]
    [InlineData("HR:0", ModbusArea.HoldingRegister, 0)]
    [InlineData("40001", ModbusArea.HoldingRegister, 0)]
    [InlineData("IR:8", ModbusArea.InputRegister, 8)]
    [InlineData("30009", ModbusArea.InputRegister, 8)]
    [InlineData("COIL:10", ModbusArea.Coil, 10)]
    [InlineData("00011", ModbusArea.Coil, 10)]
    [InlineData("DI:2", ModbusArea.DiscreteInput, 2)]
    [InlineData("10003", ModbusArea.DiscreteInput, 2)]
    public void TryParse_ShouldNormalizeSupportedFormats(string text, ModbusArea area, ushort offset)
    {
        ModbusAddressParser parser = new();
        Assert.True(parser.TryParse(text, out ModbusAddress address, out string? error));
        Assert.Null(error);
        Assert.Equal(new ModbusAddress(area, offset), address);
    }

    /// <summary>确认含糊或越界地址会返回明确错误。</summary>
    [Theory]
    [InlineData("D100")]
    [InlineData("HR:65536")]
    [InlineData("20001")]
    [InlineData("")]
    public void TryParse_ShouldRejectUnsupportedFormats(string text)
    {
        ModbusAddressParser parser = new();
        Assert.False(parser.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
