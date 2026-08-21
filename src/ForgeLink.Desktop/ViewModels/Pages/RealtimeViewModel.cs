// 文件说明：仅在实时页面激活期间刷新实时值。
// 责任边界：离开页面立即取消轮询，不刷新系统概览或配置列表。

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示实时数据页面及其激活生命周期。</summary>
public partial class RealtimeViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    private CancellationTokenSource? _activation;
    private Task? _refreshLoop;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<RealtimeValueDto> Values { get; } = [];

    /// <inheritdoc />
    public override Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (_refreshLoop is not null) return Task.CompletedTask;
        _activation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _refreshLoop = RefreshLoopAsync(_activation.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task OnNavigatedFromAsync(CancellationToken cancellationToken)
    {
        if (_activation is null || _refreshLoop is null) return;
        await _activation.CancelAsync();
        try { await _refreshLoop.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        _activation.Dispose();
        _activation = null;
        _refreshLoop = null;
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            do
            {
                try
                {
                    IReadOnlyList<RealtimeValueDto> values = await apiClient.GetRealtimeAsync(cancellationToken);
                    Values.Clear();
                    foreach (RealtimeValueDto value in values) Values.Add(value);
                    ErrorMessage = string.Empty;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    ErrorMessage = $"实时数据刷新失败：{exception.Message}";
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
