// 文件说明：提供跨页面统一操作提示状态。
// 责任边界：仅保存当前用户提示，不记录日志或处理异常策略。

using CommunityToolkit.Mvvm.ComponentModel;

namespace ForgeLink.Desktop.Services;

/// <summary>集中管理配置页面的 InfoBar 消息。</summary>
public partial class NotificationService : ObservableObject
{
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>指示当前是否有需要展示的消息。</summary>
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    /// <summary>显示新的操作消息。</summary>
    public void Show(string message) => Message = message;

    /// <summary>清除旧页面遗留的操作消息。</summary>
    public void Clear() => Message = string.Empty;
}
