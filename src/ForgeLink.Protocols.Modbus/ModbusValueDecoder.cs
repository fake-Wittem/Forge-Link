// 文件说明：把 NModbus 返回的线圈或 16 位寄存器转换为统一原始值。
// 责任边界：只处理类型、字节序、字序和 ASCII 字符串，不应用工程缩放。

using System.Buffers.Binary;
using System.Text;
using ForgeLink.Domain;

namespace ForgeLink.Protocols.Modbus;

/// <summary>提供不依赖 CPU 端序的 Modbus 数据转换。</summary>
public sealed class ModbusValueDecoder
{
    /// <summary>读取单个线圈或离散输入值。</summary>
    public object DecodeBit(bool[] values, int index) => values[index];

    /// <summary>从连续寄存器块中解析指定点位值。</summary>
    public object DecodeRegisters(ushort[] registers, int index, PointDefinition point, int registerCount)
    {
        ushort[] words = registers.AsSpan(index, registerCount).ToArray();
        if (point.WordOrder == RegisterWordOrder.LowWordFirst) Array.Reverse(words);
        byte[] bytes = new byte[words.Length * 2];
        for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            ushort word = words[wordIndex];
            if (point.ByteOrder == RegisterByteOrder.LittleEndian)
                word = BinaryPrimitives.ReverseEndianness(word);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(wordIndex * 2, 2), word);
        }

        return point.DataType switch
        {
            PointDataType.Boolean => BinaryPrimitives.ReadUInt16BigEndian(bytes) != 0,
            PointDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            PointDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            PointDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            PointDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            PointDataType.Int64 => BinaryPrimitives.ReadInt64BigEndian(bytes),
            PointDataType.Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            PointDataType.Double => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            PointDataType.String => DecodeString(bytes, point.StringLength),
            _ => throw new ArgumentOutOfRangeException(nameof(point), point.DataType, "不支持的 Modbus 数据类型。")
        };
    }

    /// <summary>按配置字节长度解码 ASCII，并把首个 NUL 视为 PLC 字符串结束符。</summary>
    private static string DecodeString(byte[] bytes, int configuredLength)
    {
        int length = configuredLength > 0 ? Math.Min(configuredLength, bytes.Length) : bytes.Length;
        int terminator = Array.IndexOf(bytes, (byte)0, 0, length);
        if (terminator >= 0) length = terminator;
        return Encoding.ASCII.GetString(bytes, 0, length);
    }
}
