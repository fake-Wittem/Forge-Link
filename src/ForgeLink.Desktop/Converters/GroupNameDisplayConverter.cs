// 文件说明：统一点位分组名称在列表中的显示文本。
// 责任边界：只把空分组显示为“未分组”，不修改持久化值。

using System.Globalization;
using System.Windows.Data;

namespace ForgeLink.Desktop.Converters;

/// <summary>把空白分组名称转换为明确的未分组标签。</summary>
public sealed class GroupNameDisplayConverter : IValueConverter
{
    /// <summary>返回分组名称或未分组占位文本。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? "未分组" : value;

    /// <summary>列表列为只读展示，不支持反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
