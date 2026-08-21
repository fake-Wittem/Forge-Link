// 文件说明：提供点位页面列表、CRUD 和 CSV 协调。
// 责任边界：点位表单状态由 PointEditorViewModel 管理，文件与 API 通过接口访问。

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _isDeletePending;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public ObservableCollection<PointDefinition> Points { get; } = [];
    public PointEditorViewModel Editor { get; } = new();
    public NotificationService Messages => Notification;

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadAsync, cancellationToken);

    [RelayCommand]
    private void NewPoint()
    {
        if (Devices.Count == 0) { Notification.Show("请先创建设备。"); return; }
        Editor.OpenNew(Devices[0].Id);
        IsDeletePending = false;
    }

    [RelayCommand]
    private void EditPoint()
    {
        if (SelectedPoint is null) { Notification.Show("请先选择一个点位。"); return; }
        Editor.Open(SelectedPoint);
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
        await LoadAsync(token);
    });

    [RelayCommand]
    private void RequestDeletePoint()
    {
        if (SelectedPoint is null) Notification.Show("请先选择一个点位。");
        else IsDeletePending = true;
    }

    [RelayCommand]
    private void CancelDeletePoint() => IsDeletePending = false;

    [RelayCommand]
    private async Task ConfirmDeletePointAsync()
    {
        if (SelectedPoint is null) return;
        Guid id = SelectedPoint.Id;
        await RunOperationAsync(async token =>
        {
            await apiClient.DeletePointAsync(id, token);
            IsDeletePending = false;
            Notification.Show("点位已删除。");
            await LoadAsync(token);
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
        IsBusy = true;
        try
        {
            IReadOnlyList<DeviceDefinition> devices = await apiClient.GetDevicesAsync(cancellationToken);
            IReadOnlyList<PointDefinition> points = await apiClient.GetPointsAsync(cancellationToken);
            Replace(Devices, devices);
            Replace(Points, points);
            SelectedPoint = null;
        }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }
}
