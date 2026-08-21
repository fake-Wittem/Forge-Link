// 文件说明：提供菜单页面统一生命周期和操作异常处理。
// 责任边界：不保存具体业务数据，不创建后台刷新任务。

using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using ForgeLink.Desktop.Navigation;
using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels;

/// <summary>为页面 ViewModel 提供一致的导航和消息行为。</summary>
public abstract class PageViewModelBase(NotificationService notification) : ObservableObject, INavigationAware
{
    protected NotificationService Notification { get; } = notification;

    /// <inheritdoc />
    public virtual Task OnNavigatedToAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task OnNavigatedFromAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>统一执行页面操作并转换为可展示消息。</summary>
    protected async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        try
        {
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
            await operation(timeoutSource.Token);
        }
        catch (CollectorApiException exception)
        {
            Notification.Show(exception.Message);
        }
        catch (HttpRequestException)
        {
            Notification.Show("Collector Service 未连接，请检查本机服务状态。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Notification.Show("操作已取消或超时。");
        }
        catch (IOException exception)
        {
            Notification.Show($"文件操作失败：{exception.Message}");
        }
    }
}
