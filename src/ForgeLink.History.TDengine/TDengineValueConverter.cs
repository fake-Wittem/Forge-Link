// 文件说明：统一转换 TDengine.Connector 返回的文本与 BINARY 字段值。
// 责任边界：只处理驱动返回类型差异，不执行 SQL 或领域映射。

using System.Globalization;
using System.Text;

namespace ForgeLink.History.TDengine;

/// <summary>兼容 WebSocket 驱动可能以字节数组返回 BINARY 字段的行为。</summary>
public static class TDengineValueConverter
{
    public static string ToText(object value) => value switch
    {
        string text => text,
        byte[] bytes => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
        ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span).TrimEnd('\0'),
        char[] characters => new string(characters).TrimEnd('\0'),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
