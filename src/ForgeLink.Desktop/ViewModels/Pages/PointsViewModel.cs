// 文件说明：提供点位页面列表、CRUD 和 CSV 协调。
// 责任边界：点位表单状态由 PointEditorViewModel 管理，文件与 API 通过接口访问。

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Editors;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示点位管理页面。</summary>
public partial class PointsViewModel(
    ICollectorApiClient apiClient,
    IPointCsvFileService csvFiles,
    NotificationService notification) : PageViewModelBase(notification)
{
    [ObservableProperty] private PointDefinition? _selectedPoint;
    [ObservableProperty] private SelectableRow<PointDefinition>? _selectedPointRow;
    [ObservableProperty] private DevicePointFilterItem? _selectedDeviceFilter;
    [ObservableProperty] private PointGroupFilterItem? _selectedPointGroupFilter;
    [ObservableProperty] private bool _isDeletePending;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public ObservableCollection<PointDefinition> Points { get; } = [];
    public ObservableCollection<PointDefinition> VisiblePoints { get; } = [];
    public ObservableCollection<SelectableRow<PointDefinition>> VisiblePointRows { get; } = [];
    public ObservableCollection<DevicePointFilterItem> DeviceFilters { get; } = [];
    public ObservableCollection<PointGroupFilterItem> PointGroupFilters { get; } = [];
    public PointEditorViewModel Editor { get; } = new();
    public NotificationService Messages => Notification;
    public int CheckedPointCount => VisiblePointRows.Count(static row => row.IsChecked);
    public bool HasCheckedPoints => CheckedPointCount > 0;

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadAsync, cancellationToken);

    [RelayCommand]
    private void NewPoint()
    {
        if (Devices.Count == 0) { Notification.Show("请先创建设备。"); return; }
        DeviceDefinition device = SelectedDeviceFilter?.Device ?? Devices[0];
        Editor.OpenNew(device, Points);
        if (SelectedPointGroupFilter is { IsAll: false, Key.Length: > 0 } group)
            Editor.GroupName = group.Key;
        IsDeletePending = false;
    }

    [RelayCommand]
    private void EditPoint(PointDefinition? point)
    {
        if (point is null) return;
        Editor.Open(point, Devices, Points);
        IsDeletePending = false;
    }

    [RelayCommand]
    private void CancelPointEdit() => Editor.IsOpen = false;

    [RelayCommand]
    private async Task SavePointAsync() => await RunOperationAsync(async token =>
    {
        (PointDefinition point, IReadOnlyList<string> errors) = Editor.Build();
        if (errors.Count > 0) { Notification.Show(string.Join(' ', errors)); return; }
        await apiClient.SavePointAsync(point, Editor.IsNew, token);
        Editor.IsOpen = false;
        Notification.Show("点位配置已保存，采集任务正在热加载。");
        await LoadAsync(token, point.DeviceId, point.GroupName.Trim());
    });

    [RelayCommand]
    private void RequestDeletePoint()
    {
        if (!HasCheckedPoints) Notification.Show("请先勾选要删除的点位。");
        else IsDeletePending = true;
    }

    [RelayCommand]
    private void CancelDeletePoint() => IsDeletePending = false;

    [RelayCommand]
    private async Task ConfirmDeletePointAsync()
    {
        PointDefinition[] selected = VisiblePointRows.Where(static row => row.IsChecked).Select(static row => row.Item).ToArray();
        if (selected.Length == 0) return;
        await RunOperationAsync(async token =>
        {
            int deleted = 0;
            List<string> failures = [];
            foreach (PointDefinition point in selected)
            {
                try
                {
                    await apiClient.DeletePointAsync(point.Id, token);
                    deleted++;
                }
                catch (CollectorApiException exception)
                {
                    failures.Add($"{point.Code}：{exception.Message}");
                }
            }
            IsDeletePending = false;
            await LoadAsync(token);
            Notification.Show(failures.Count == 0
                ? $"已删除 {deleted} 个点位。"
                : $"已删除 {deleted} 个点位，{failures.Count} 个删除失败。{string.Join(' ', failures)}");
        });
    }

    [RelayCommand]
    private async Task ExportPointsAsync() => await RunOperationAsync(async token =>
    {
        byte[] csv = await apiClient.ExportPointsAsync(token);
        if (await csvFiles.SaveAsync(csv, token)) Notification.Show("点位 CSV 已导出。");
    });

    [RelayCommand]
    private async Task ImportPointsAsync() => await RunOperationAsync(async token =>
    {
        string? csv = await csvFiles.OpenAsync(token);
        if (csv is null) return;
        int count = await apiClient.ImportPointsAsync(csv, token);
        Notification.Show($"已导入 {count} 个点位，采集任务正在热加载。");
        await LoadAsync(token);
    });

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Guid? preferredDeviceId = SelectedDeviceFilter?.Device.Id;
        string? preferredGroup = SelectedPointGroupFilter is { IsAll: false } group ? group.Key : null;
        await LoadAsync(cancellationToken, preferredDeviceId, preferredGroup);
    }

    /// <summary>加载配置并尽量恢复指定的设备与点位分组。</summary>
    private async Task LoadAsync(CancellationToken cancellationToken, Guid? preferredDeviceId, string? preferredGroup)
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<DeviceDefinition> devices = await apiClient.GetDevicesAsync(cancellationToken);
            IReadOnlyList<PointDefinition> points = await apiClient.GetPointsAsync(cancellationToken);
            Replace(Devices, devices);
            Replace(Points, points);
            SelectedPoint = null;
            RebuildNavigation(preferredDeviceId, preferredGroup);
        }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }

    /// <summary>切换设备后重建设备内分组并刷新点位列表。</summary>
    partial void OnSelectedDeviceFilterChanged(DevicePointFilterItem? value) => RebuildPointGroups();

    /// <summary>切换点位分组后只展示当前设备和分组的数据。</summary>
    partial void OnSelectedPointGroupFilterChanged(PointGroupFilterItem? value) => ApplyPointFilter();

    /// <summary>当前焦点行变化时保留与表格交互一致的点位选择。</summary>
    partial void OnSelectedPointRowChanged(SelectableRow<PointDefinition>? value) => SelectedPoint = value?.Item;

    /// <summary>根据最新设备和点位数据重建设备级导航。</summary>
    private void RebuildNavigation(Guid? preferredDeviceId, string? preferredGroup)
    {
        SelectedDeviceFilter = null;
        SelectedPointGroupFilter = null;
        DeviceFilters.Clear();
        foreach (DeviceDefinition device in Devices)
        {
            int count = Points.Count(point => point.DeviceId == device.Id);
            DeviceFilters.Add(new(device, count));
        }

        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(item => item.Device.Id == preferredDeviceId)
            ?? DeviceFilters.FirstOrDefault();
        SelectPreferredGroup(preferredGroup);
    }

    /// <summary>为当前设备生成“全部点位”和自定义分组导航。</summary>
    private void RebuildPointGroups()
    {
        PointGroupFilters.Clear();
        if (SelectedDeviceFilter is null)
        {
            VisiblePoints.Clear();
            return;
        }

        PointDefinition[] devicePoints = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id).ToArray();
        PointGroupFilters.Add(new(null, "全部点位", devicePoints.Length, true));
        foreach (IGrouping<string, PointDefinition> group in devicePoints
                     .GroupBy(static point => point.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key.Length == 0)
                     .ThenBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            PointGroupFilters.Add(new(group.Key, group.Key.Length == 0 ? "未分组" : group.Key, group.Count()));
        }

        SelectedPointGroupFilter = PointGroupFilters[0];
    }

    /// <summary>恢复刷新前选中的自定义分组；找不到时回到全部点位。</summary>
    private void SelectPreferredGroup(string? preferredGroup)
    {
        if (preferredGroup is null || SelectedDeviceFilter is null) return;
        SelectedPointGroupFilter = PointGroupFilters.FirstOrDefault(group => !group.IsAll
            && string.Equals(group.Key, preferredGroup, StringComparison.OrdinalIgnoreCase))
            ?? PointGroupFilters.FirstOrDefault();
    }

    /// <summary>应用两级导航筛选，并保持点位按分组和编码稳定排序。</summary>
    private void ApplyPointFilter()
    {
        foreach (SelectableRow<PointDefinition> row in VisiblePointRows) row.PropertyChanged -= OnPointRowPropertyChanged;
        VisiblePoints.Clear();
        VisiblePointRows.Clear();
        NotifyCheckedPointStateChanged();
        if (SelectedDeviceFilter is null || SelectedPointGroupFilter is null) return;
        IEnumerable<PointDefinition> points = Points.Where(point => point.DeviceId == SelectedDeviceFilter.Device.Id);
        if (!SelectedPointGroupFilter.IsAll)
            points = points.Where(point => string.Equals(point.GroupName.Trim(), SelectedPointGroupFilter.Key, StringComparison.OrdinalIgnoreCase));
        foreach (PointDefinition point in points.OrderBy(static point => point.GroupName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(static point => point.Code, StringComparer.CurrentCultureIgnoreCase))
        {
            VisiblePoints.Add(point);
            SelectableRow<PointDefinition> row = new(point);
            row.PropertyChanged += OnPointRowPropertyChanged;
            VisiblePointRows.Add(row);
        }
        SelectedPointRow = null;
        if (SelectedPoint is not null && !VisiblePoints.Contains(SelectedPoint)) SelectedPoint = null;
    }

    /// <summary>点位勾选变化后刷新批量删除状态。</summary>
    private void OnPointRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableRow<PointDefinition>.IsChecked)) NotifyCheckedPointStateChanged();
    }

    private void NotifyCheckedPointStateChanged()
    {
        OnPropertyChanged(nameof(CheckedPointCount));
        OnPropertyChanged(nameof(HasCheckedPoints));
    }
}
