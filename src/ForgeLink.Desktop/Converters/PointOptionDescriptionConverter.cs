// 文件说明：把点位编辑器中的领域枚举转换为面向用户的中文选项。
// 责任边界：只负责界面显示，不改变枚举值、持久化格式或接口契约。

using System.Globalization;
using System.Windows.Data;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.Converters;

/// <summary>提供历史策略和寄存器顺序的中文说明。</summary>
public sealed class PointOptionDescriptionConverter : IValueConverter
{
    /// <summary>返回领域枚举对应的中文说明。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        HistoryRecordMode.None => "不记录",
        HistoryRecordMode.EverySample => "每次采样都记录",
        HistoryRecordMode.OnChange => "数值变化时记录",
        HistoryRecordMode.Deadband => "超过死区值时记录",
        HistoryRecordMode.PeriodicSnapshot => "定期快照",
        HistoryRecordMode.ChangeWithHeartbeat => "变化时记录（含心跳）",
        RegisterByteOrder.BigEndian => "大端序（高字节在前）",
        RegisterByteOrder.LittleEndian => "小端序（低字节在前）",
        RegisterWordOrder.HighWordFirst => "高字在前",
        RegisterWordOrder.LowWordFirst => "低字在前",
        _ => value?.ToString() ?? string.Empty
    };

    /// <summary>选项只通过 SelectedItem 写回，不支持由说明文本反向转换。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
