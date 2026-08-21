// 文件说明：提供 WPF 桌面应用的代码入口。
// 责任边界：不直接访问 PLC、SQLite 或 TDengine。

using ForgeLink.Desktop.Navigation;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels;
using ForgeLink.Desktop.ViewModels.Pages;

namespace ForgeLink.Desktop;

/// <summary>表示 ForgeLink 桌面应用实例。</summary>
public partial class App : System.Windows.Application
{
    /// <summary>在应用入口集中组装共享服务和页面 ViewModel。</summary>
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        CollectorApiClient apiClient = new();
        PointCsvFileService csvFiles = new();
        NotificationService notifications = new();
        SystemStatusMonitor status = new(apiClient);
        Dictionary<AppRoute, PageViewModelBase> pages = new()
        {
            [AppRoute.Overview] = new OverviewViewModel(status, notifications),
            [AppRoute.Devices] = new DevicesViewModel(apiClient, notifications),
            [AppRoute.Points] = new PointsViewModel(apiClient, csvFiles, notifications),
            [AppRoute.Realtime] = new RealtimeViewModel(apiClient, notifications),
            [AppRoute.History] = new HistoryViewModel(apiClient, notifications),
            [AppRoute.Transport] = new TransportViewModel(notifications),
            [AppRoute.Alarms] = new AlarmsViewModel(notifications),
            [AppRoute.Settings] = new SettingsViewModel(apiClient, notifications)
        };
        ShellViewModel shell = new(pages, status, notifications, apiClient);
        MainWindow window = new(shell);
        MainWindow = window;
        window.Show();
    }
}
