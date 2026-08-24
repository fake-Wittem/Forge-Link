// 文件说明：提供历史点位选择、时间范围校验、查询和趋势数据。
// 责任边界：只调用 Collector Service，不直接连接 TDengine。

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

public partial class HistoryViewModel : PageViewModelBase
{
    private readonly ICollectorApiClient _apiClient;
    [ObservableProperty] private PointDefinition? _selectedPoint;
    [ObservableProperty] private DevicePointFilterItem? _selectedDeviceFilter;
    [ObservableProperty] private PointGroupFilterItem? _selectedPointGroupFilter;
    [ObservableProperty] private DateTime? _fromLocalDateTime;
    [ObservableProperty] private DateTime? _toLocalDateTime;
    [ObservableProperty] private string _resultSummary = "请选择点位并查询";
    [ObservableProperty] private string _historyAvailabilityMessage = string.Empty;
    [ObservableProperty] private bool _isHistoryAvailable;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public ObservableCollection<PointDefinition> Points { get; } = [];
    public ObservableCollection<PointDefinition> FilteredPoints { get; } = [];
    public ObservableCollection<DevicePointFilterItem> DeviceFilters { get; } = [];
    public ObservableCollection<PointGroupFilterItem> PointGroupFilters { get; } = [];
    public ObservableCollection<PointValue> Values { get; } = [];
    public NotificationService Messages => Notification;

    /// <summary>创建历史页面，并以系统本地时间初始化最近一小时的查询范围。</summary>
    public HistoryViewModel(ICollectorApiClient apiClient, NotificationService notification) : base(notification)
    {
        _apiClient = apiClient;
        DateTime now = DateTime.Now;
        DateTime from = now.AddHours(-1);
        FromLocalDateTime = from;
        ToLocalDateTime = now;
    }

    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadPointsAsync, cancellationToken);

    [RelayCommand]
    private async Task QueryAsync() => await RunOperationAsync(async token =>
    {
        if (!IsHistoryAvailable) { Notification.Show(HistoryAvailabilityMessage); return; }
        if (SelectedPoint is null) { Notification.Show("请先选择一个历史点位。"); return; }
        if (!TryComposeLocalTimestamp(FromLocalDateTime, out DateTimeOffset from)
            || !TryComposeLocalTimestamp(ToLocalDateTime, out DateTimeOffset to))
        {
            Notification.Show("请选择完整且有效的开始日期、开始时间、结束日期和结束时间。");
            return;
        }
        if (from > to || to - from > TimeSpan.FromDays(7))
        {
            Notification.Show("查询开始时间不得晚于结束时间，单次范围不得超过 7 天。");
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyList<PointValue> values = await _apiClient.QueryHistoryAsync(
                [SelectedPoint.Id], from.ToUniversalTime(), to.ToUniversalTime(), 10_000, token);
            Values.Clear();
            foreach (PointValue value in values) Values.Add(value);
            ResultSummary = $"返回 {values.Count:N0} 条 · {SelectedPoint.Name}";
        }
        finally { IsBusy = false; }
    }, timeout: TimeSpan.FromSeconds(35));

    private async Task LoadPointsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceDefinition> devices = await _apiClient.GetDevicesAsync(cancellationToken);
        IReadOnlyList<PointDefinition> points = await _apiClient.GetPointsAsync(cancellationToken);
        HistoryConfigurationDto configuration = await _apiClient.GetHistoryConfigurationAsync(cancellationToken);
        Replace(Devices, devices);
        Replace(Points, points.Where(static point => point.IsEnabled && point.HistoryMode != HistoryRecordMode.None));
        IsHistoryAvailable = configuration.IsConfigured && (string.Equals(configuration.State, "Enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration.State, "Degraded", StringComparison.OrdinalIgnoreCase));
        HistoryAvailabilityMessage = !configuration.IsConfigured
            ? "历史存储尚未配置，请先在系统设置中配置并测试 TDengine。"
            : IsHistoryAvailable
                ? string.Empty
                : "历史存储当前未启用，请在系统设置中完成连接测试并启用；停用期间的数据不会补写。";
        RebuildDevices();
    }

    /// <summary>切换设备后只呈现该设备内启用了历史记录的分组。</summary>
    partial void OnSelectedDeviceFilterChanged(DevicePointFilterItem? value) => RebuildPointGroups();

    /// <summary>切换分组后刷新可选历史点位。</summary>
    partial void OnSelectedPointGroupFilterChanged(PointGroupFilterItem? value) => ApplyPointFilter();

    private void RebuildDevices()
    {
        DeviceFilters.Clear();
        foreach (DeviceDefinition device in Devices.Where(static device => device.IsEnabled))
        {
            int count = Points.Count(point => point.DeviceId == device.Id);
            if (count > 0) DeviceFilters.Add(new(device, count));
        }
        SelectedDeviceFilter = DeviceFilters.FirstOrDefault();
    }

    private void RebuildPointGroups()
    {
        PointGroupFilters.Clear();
        if (SelectedDeviceFilter is null) { FilteredPoints.Clear(); SelectedPoint = null; return; }
        PointDefinition[] devicePoints = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id).ToArray();
        PointGroupFilters.Add(new(null, "全部分组", devicePoints.Length, true));
        foreach (IGrouping<string, PointDefinition> group in devicePoints.GroupBy(static point => point.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key.Length == 0).ThenBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            PointGroupFilters.Add(new(group.Key, group.Key.Length == 0 ? "未分组" : group.Key, group.Count()));
        SelectedPointGroupFilter = PointGroupFilters.FirstOrDefault();
    }

    private void ApplyPointFilter()
    {
        FilteredPoints.Clear();
        Values.Clear();
        ResultSummary = "请选择点位并查询";
        if (SelectedDeviceFilter is null || SelectedPointGroupFilter is null) { SelectedPoint = null; return; }
        IEnumerable<PointDefinition> points = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id);
        if (!SelectedPointGroupFilter.IsAll)
            points = points.Where(point => string.Equals(point.GroupName.Trim(), SelectedPointGroupFilter.Key, StringComparison.OrdinalIgnoreCase));
        foreach (PointDefinition point in points.OrderBy(static point => point.Code, StringComparer.CurrentCultureIgnoreCase)) FilteredPoints.Add(point);
        SelectedPoint = FilteredPoints.FirstOrDefault();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }

    /// <summary>把日期与时间选择值组合为系统时区的 DateTimeOffset。</summary>
    private static bool TryComposeLocalTimestamp(DateTime? selectedDateTime, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (selectedDateTime is null) return false;
        DateTime local = DateTime.SpecifyKind(selectedDateTime.Value, DateTimeKind.Local);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) return false;
        timestamp = new DateTimeOffset(local);
        return true;
    }
}
