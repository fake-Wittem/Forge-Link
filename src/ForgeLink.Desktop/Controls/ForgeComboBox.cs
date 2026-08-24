// 文件说明：封装 WPF UI 未提供独立类型的主题化下拉框。
// 责任边界：统一控件类型、主题入口和滚轮交互，不包含业务选项或页面逻辑。

using System.Windows;
using System.Windows.Input;

namespace ForgeLink.Desktop.Controls;

/// <summary>继承 WPF ComboBox，并通过全局资源统一应用 WPF UI 主题样式。</summary>
public sealed class ForgeComboBox : System.Windows.Controls.ComboBox
{
    /// <summary>下拉框关闭时把滚轮事件交给父级滚动容器，避免误改选项。</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (IsDropDownOpen)
        {
            base.OnPreviewMouseWheel(e);
            return;
        }

        e.Handled = true;
        MouseWheelEventArgs parentEvent = new(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = this
        };

        (Parent as UIElement)?.RaiseEvent(parentEvent);
    }
}
