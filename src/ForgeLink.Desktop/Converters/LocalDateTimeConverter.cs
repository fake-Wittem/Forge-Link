// 文件说明：统一把 Collector 返回的 UTC 时间转换成系统本地时间文本。
// 责任边界：只负责桌面展示格式，不修改采集值或历史查询时间。

using System.Globalization;
using System.Windows.Data;

namespace ForgeLink.Desktop.Converters;

/// <summary>以系统时区和毫秒精度显示采集时间。</summary>
public sealed class LocalDateTimeConverter : IValueConverter
{
    /// <summary>把 DateTimeOffset 转换为系统本地时间并固定输出三位毫秒。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset timestamp => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        _ => string.Empty
    };

    /// <summary>时间列只读展示，不支持反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
