// 文件说明：集中轮询 Collector Service 概览状态并提供可绑定快照。
// 责任边界：只读取状态 API，不刷新设备、点位或实时值列表。

using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ForgeLink.Desktop.Services;

/// <summary>为主壳层和概览页面共享唯一的服务状态刷新循环。</summary>
public partial class SystemStatusMonitor(ICollectorApiClient apiClient) : ObservableObject, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task? _refreshLoop;

    [ObservableProperty] private string _serviceState = "正在连接服务…";
    [ObservableProperty] private string _lastRefreshText = "尚未刷新";
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private int _onlineDeviceCount;
    [ObservableProperty] private int _enabledPointCount;
    [ObservableProperty] private int _realtimeValueCount;
    [ObservableProperty] private string _successRateText = "—";
    [ObservableProperty] private string _historyStatus = "NotConfigured";
    [ObservableProperty] private string _runtimeText = "—";
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>指示是否存在需要展示的服务连接错误。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>启动应用级唯一状态刷新循环。</summary>
    public void Start() => _refreshLoop ??= RefreshLoopAsync(_lifetime.Token);

    /// <summary>立即读取一次服务状态。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            Models.StatusDto status = await apiClient.GetStatusAsync(timeout.Token);
            DeviceCount = status.DeviceCount;
            OnlineDeviceCount = status.OnlineDeviceCount;
            EnabledPointCount = status.EnabledPointCount;
            RealtimeValueCount = status.RealtimeValueCount;
            SuccessRateText = status.SuccessRate.ToString("P1", System.Globalization.CultureInfo.CurrentCulture);
            HistoryStatus = status.HistoryStatus;
            RuntimeText = FormatRuntime(DateTimeOffset.UtcNow - status.StartedAtUtc);
            ServiceState = "Collector Service 运行中";
            ErrorMessage = string.Empty;
            LastRefreshText = $"最后刷新：{DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ServiceState = "Collector Service 响应超时";
            ErrorMessage = "本机服务在 5 秒内未响应，请检查服务状态。";
        }
        catch (HttpRequestException)
        {
            ServiceState = "Collector Service 未连接";
            ErrorMessage = "请先启动 ForgeLink.Collector.Service，再刷新管理端。";
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            do { await RefreshAsync(cancellationToken); }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static string FormatRuntime(TimeSpan runtime) => runtime.TotalDays >= 1
        ? $"{(int)runtime.TotalDays} 天 {runtime.Hours} 小时"
        : $"{runtime.Hours} 小时 {runtime.Minutes} 分钟 {runtime.Seconds} 秒";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        if (_refreshLoop is not null)
        {
            try { await _refreshLoop; }
            catch (OperationCanceledException) { }
        }
        _refreshLock.Dispose();
        _lifetime.Dispose();
    }
}
