// 文件说明：解析 ForgeLink 支持的 Modbus 点位地址格式。
// 责任边界：仅把用户地址转换为功能区和零基偏移，不执行网络通信。

using ForgeLink.Domain;

namespace ForgeLink.Protocols.Modbus;

/// <summary>定义 Modbus 的四个标准读取区域。</summary>
public enum ModbusArea { Coil, DiscreteInput, HoldingRegister, InputRegister }

/// <summary>表示已经归一化的零基 Modbus 地址。</summary>
public readonly record struct ModbusAddress(ModbusArea Area, ushort Offset);

/// <summary>提供显式前缀地址和常见五位参考地址解析。</summary>
public sealed class ModbusAddressParser
{
    /// <summary>解析地址；显式格式中的数字始终按零基偏移解释。</summary>
    public bool TryParse(string text, out ModbusAddress address, out string? error)
    {
        address = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Modbus 地址不能为空。";
            return false;
        }

        string normalized = text.Trim();
        int separator = normalized.IndexOf(':');
        if (separator >= 0)
        {
            string prefix = normalized[..separator].Trim();
            string offsetText = normalized[(separator + 1)..].Trim();
            if (!ushort.TryParse(offsetText, out ushort offset))
            {
                error = "显式 Modbus 地址的偏移必须在 0 到 65535 之间。";
                return false;
            }
            if (!TryParseArea(prefix, out ModbusArea area))
            {
                error = "地址区域必须是 COIL、DI、HR 或 IR。";
                return false;
            }
            address = new(area, offset);
            return true;
        }

        if (normalized.Length != 5 || !int.TryParse(normalized, out int reference))
        {
            error = "地址应使用 HR:0、IR:0、COIL:0、DI:0，或五位参考地址 00001/10001/30001/40001。";
            return false;
        }

        if (reference is >= 1 and <= 9999) address = new(ModbusArea.Coil, checked((ushort)(reference - 1)));
        else if (reference is >= 10001 and <= 19999) address = new(ModbusArea.DiscreteInput, checked((ushort)(reference - 10001)));
        else if (reference is >= 30001 and <= 39999) address = new(ModbusArea.InputRegister, checked((ushort)(reference - 30001)));
        else if (reference is >= 40001 and <= 49999) address = new(ModbusArea.HoldingRegister, checked((ushort)(reference - 40001)));
        else
        {
            error = "该五位参考地址不属于支持的 Modbus 标准区域。";
            return false;
        }
        return true;
    }

    /// <summary>根据点位数据类型计算需要读取的线圈或寄存器数量。</summary>
    public int GetElementCount(PointDefinition point, ModbusArea area)
    {
        if (area is ModbusArea.Coil or ModbusArea.DiscreteInput) return 1;
        return point.DataType switch
        {
            PointDataType.Int32 or PointDataType.UInt32 or PointDataType.Float => 2,
            PointDataType.Int64 or PointDataType.Double => 4,
            PointDataType.String => Math.Max(1, (point.StringLength + 1) / 2),
            _ => 1
        };
    }

    /// <summary>把地址前缀映射到标准 Modbus 区域。</summary>
    private static bool TryParseArea(string prefix, out ModbusArea area)
    {
        switch (prefix.ToUpperInvariant())
        {
            case "C":
            case "COIL": area = ModbusArea.Coil; return true;
            case "DI":
            case "DISCRETE": area = ModbusArea.DiscreteInput; return true;
            case "HR":
            case "HOLDING": area = ModbusArea.HoldingRegister; return true;
            case "IR":
            case "INPUT": area = ModbusArea.InputRegister; return true;
            default: area = default; return false;
        }
    }
}
