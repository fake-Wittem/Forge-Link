// 文件说明：把当前导航页名称转换为界面可见性。
// 责任边界：仅处理纯展示转换，不执行导航业务或服务调用。

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ForgeLink.Desktop.Converters;

/// <summary>仅显示与转换参数同名的页面区域。</summary>
public sealed class PageVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("页面可见性不支持反向转换。");
}
