// 文件说明：为桌面表格行提供独立于当前行焦点的批量勾选状态。
// 责任边界：只包装展示数据和勾选状态，不执行删除或其他业务操作。

using CommunityToolkit.Mvvm.ComponentModel;

namespace ForgeLink.Desktop.Models;

/// <summary>表示一个可由复选框独立选择的表格行。</summary>
public sealed partial class SelectableRow<T>(T item) : ObservableObject
{
    public T Item { get; } = item;

    [ObservableProperty]
    private bool _isChecked;
}
