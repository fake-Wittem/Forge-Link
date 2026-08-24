// 文件说明：仅在实时页面激活期间刷新实时值。
// 责任边界：离开页面立即取消轮询，不刷新系统概览或配置列表。

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示实时数据页面及其激活生命周期。</summary>
public partial class RealtimeViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    private CancellationTokenSource? _activation;
    private Task? _refreshLoop;
    private IReadOnlyList<RealtimeValueDto> _latestValues = [];
    private DateTimeOffset _nextMetadataRefreshUtc = DateTimeOffset.MinValue;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private DevicePointFilterItem? _selectedDeviceFilter;
    [ObservableProperty] private PointGroupFilterItem? _selectedPointGroupFilter;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public ObservableCollection<PointDefinition> Points { get; } = [];
    public ObservableCollection<DevicePointFilterItem> DeviceFilters { get; } = [];
    public ObservableCollection<PointGroupFilterItem> PointGroupFilters { get; } = [];
    public ObservableCollection<RealtimePointRow> Values { get; } = [];

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken)
    {
        if (_refreshLoop is not null) return;
        await RunOperationAsync(LoadMetadataAsync, cancellationToken);
        _activation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _refreshLoop = RefreshLoopAsync(_activation.Token);
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
                    if (DateTimeOffset.UtcNow >= _nextMetadataRefreshUtc) await LoadMetadataAsync(cancellationToken);
                    _latestValues = await apiClient.GetRealtimeAsync(cancellationToken);
                    ApplyFilter();
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

    /// <summary>刷新设备与点位元数据，使实时值展示编码、名称和分组而不是内部 GUID。</summary>
    private async Task LoadMetadataAsync(CancellationToken cancellationToken)
    {
        Guid? preferredDeviceId = SelectedDeviceFilter?.Device.Id;
        string? preferredGroup = SelectedPointGroupFilter is { IsAll: false } group ? group.Key : null;
        Replace(Devices, await apiClient.GetDevicesAsync(cancellationToken));
        Replace(Points, (await apiClient.GetPointsAsync(cancellationToken)).Where(static point => point.IsEnabled));
        DeviceFilters.Clear();
        foreach (DeviceDefinition device in Devices.Where(static device => device.IsEnabled))
            DeviceFilters.Add(new(device, Points.Count(point => point.DeviceId == device.Id)));
        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(item => item.Device.Id == preferredDeviceId) ?? DeviceFilters.FirstOrDefault();
        if (preferredGroup is not null)
            SelectedPointGroupFilter = PointGroupFilters.FirstOrDefault(group => !group.IsAll && string.Equals(group.Key, preferredGroup, StringComparison.OrdinalIgnoreCase))
                ?? PointGroupFilters.FirstOrDefault();
        _nextMetadataRefreshUtc = DateTimeOffset.UtcNow.AddSeconds(10);
    }

    /// <summary>切换设备时，只重建该设备下的点位分组。</summary>
    partial void OnSelectedDeviceFilterChanged(DevicePointFilterItem? value) => RebuildPointGroups();

    /// <summary>切换分组时，过滤已经取得的实时快照。</summary>
    partial void OnSelectedPointGroupFilterChanged(PointGroupFilterItem? value) => ApplyFilter();

    private void RebuildPointGroups()
    {
        PointGroupFilters.Clear();
        if (SelectedDeviceFilter is null) { Values.Clear(); return; }
        PointDefinition[] devicePoints = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id).ToArray();
        PointGroupFilters.Add(new(null, "全部点位", devicePoints.Length, true));
        foreach (IGrouping<string, PointDefinition> group in devicePoints.GroupBy(static point => point.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key.Length == 0).ThenBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            PointGroupFilters.Add(new(group.Key, group.Key.Length == 0 ? "未分组" : group.Key, group.Count()));
        SelectedPointGroupFilter = PointGroupFilters.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        Values.Clear();
        if (SelectedDeviceFilter is null || SelectedPointGroupFilter is null) return;
        Dictionary<Guid, RealtimeValueDto> valuesByPoint = _latestValues.ToDictionary(static value => value.PointId);
        IEnumerable<PointDefinition> points = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id);
        if (!SelectedPointGroupFilter.IsAll)
            points = points.Where(point => string.Equals(point.GroupName.Trim(), SelectedPointGroupFilter.Key, StringComparison.OrdinalIgnoreCase));
        foreach (PointDefinition point in points.OrderBy(static point => point.GroupName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(static point => point.Code, StringComparer.CurrentCultureIgnoreCase))
            if (valuesByPoint.TryGetValue(point.Id, out RealtimeValueDto? value)) Values.Add(new(SelectedDeviceFilter.Device, point, value));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }
}
