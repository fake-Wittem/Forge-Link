// 文件说明：协调主窗口导航、页面生命周期和应用级共享状态。
// 责任边界：不包含设备、点位、实时或历史页面业务逻辑。

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Navigation;
using ForgeLink.Desktop.Services;

namespace ForgeLink.Desktop.ViewModels;

/// <summary>表示桌面应用壳层。</summary>
public partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IReadOnlyDictionary<AppRoute, PageViewModelBase> _pages;
    private readonly IDisposable _applicationResource;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private bool _started;

    [ObservableProperty] private AppRoute _currentRoute = AppRoute.Overview;
    [ObservableProperty] private PageViewModelBase _currentPage;

    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; } =
    [
        new(AppRoute.Overview, "概览仪表盘", "⌂"),
        new(AppRoute.Devices, "设备管理", "▣"),
        new(AppRoute.Points, "点位管理", "⌖"),
        new(AppRoute.Realtime, "实时数据", "◉"),
        new(AppRoute.History, "历史趋势", "⌁"),
        new(AppRoute.Transport, "数据转发", "⇄"),
        new(AppRoute.Alarms, "告警与事件", "△"),
        new(AppRoute.Settings, "系统设置", "⚙")
    ];

    /// <summary>为菜单选择提供双向绑定入口，实际页面切换仍由异步导航命令完成。</summary>
    public AppRoute SelectedRoute
    {
        get => CurrentRoute;
        set
        {
            if (value != CurrentRoute)
            {
                NavigateCommand.Execute(value);
            }
        }
    }

    public SystemStatusMonitor Status { get; }
    public NotificationService Notifications { get; }

    /// <summary>组装页面映射和共享服务。</summary>
    public ShellViewModel(
        IReadOnlyDictionary<AppRoute, PageViewModelBase> pages,
        SystemStatusMonitor status,
        NotificationService notifications,
        IDisposable applicationResource)
    {
        _pages = pages;
        Status = status;
        Notifications = notifications;
        _applicationResource = applicationResource;
        _currentPage = pages[AppRoute.Overview];
    }

    /// <summary>启动状态监视并激活初始页面。</summary>
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        Status.Start();
        await CurrentPage.OnNavigatedToAsync(_lifetime.Token);
    }

    /// <summary>切换到指定菜单页面并执行进入/离开生命周期。</summary>
    [RelayCommand]
    private async Task NavigateAsync(AppRoute route)
    {
        if (route == CurrentRoute) return;
        await _navigationLock.WaitAsync(_lifetime.Token);
        try
        {
            await CurrentPage.OnNavigatedFromAsync(_lifetime.Token);
            Notifications.Clear();
            CurrentRoute = route;
            CurrentPage = _pages[route];
            await CurrentPage.OnNavigatedToAsync(_lifetime.Token);
        }
        finally { _navigationLock.Release(); }
    }

    partial void OnCurrentRouteChanged(AppRoute value) => OnPropertyChanged(nameof(SelectedRoute));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        try { await CurrentPage.OnNavigatedFromAsync(CancellationToken.None); }
        catch (OperationCanceledException) { }
        await Status.DisposeAsync();
        _navigationLock.Dispose();
        _lifetime.Dispose();
        _applicationResource.Dispose();
    }
}

/// <summary>描述左侧菜单中的一个强类型导航项。</summary>
public sealed record ShellNavigationItem(AppRoute Route, string Title, string Glyph);
