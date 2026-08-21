// 文件说明：提供概览页面命令和共享系统状态。
// 责任边界：不创建独立轮询循环，复用应用级状态监视器。

using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示概览仪表盘。</summary>
public partial class OverviewViewModel(SystemStatusMonitor status, NotificationService notification)
    : PageViewModelBase(notification)
{
    public SystemStatusMonitor Status { get; } = status;

    /// <summary>立即刷新共享服务状态。</summary>
    [RelayCommand]
    private async Task RefreshAsync() => await Status.RefreshAsync(CancellationToken.None);
}
