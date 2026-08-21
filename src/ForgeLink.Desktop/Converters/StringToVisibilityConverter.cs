// 文件说明：把非空文本转换为可见状态。责任边界：仅用于视图显示。
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace ForgeLink.Desktop.Converters;

public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
