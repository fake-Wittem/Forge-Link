// 文件说明：封装 WPF UI 未提供独立类型的主题化下拉框。
// 责任边界：只统一控件类型和主题入口，不包含业务选项或页面逻辑。

namespace ForgeLink.Desktop.Controls;

/// <summary>继承 WPF ComboBox，并通过全局资源统一应用 WPF UI 主题样式。</summary>
public sealed class ForgeComboBox : System.Windows.Controls.ComboBox;
