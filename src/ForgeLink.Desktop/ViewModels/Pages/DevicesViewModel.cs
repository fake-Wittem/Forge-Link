// 文件说明：提供设备页面列表、连接测试和 CRUD 协调。
// 责任边界：设备表单状态由 DeviceEditorViewModel 管理，通信通过 ICollectorApiClient。

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Editors;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示设备管理页面。</summary>
public partial class DevicesViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    [ObservableProperty] private DeviceDefinition? _selectedDevice;
    [ObservableProperty] private SelectableRow<DeviceDefinition>? _selectedDeviceRow;
    [ObservableProperty] private bool _isDeletePending;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public ObservableCollection<SelectableRow<DeviceDefinition>> DeviceRows { get; } = [];
    public DeviceEditorViewModel Editor { get; } = new();
    public NotificationService Messages => Notification;
    public int CheckedDeviceCount => DeviceRows.Count(static row => row.IsChecked);
    public bool HasCheckedDevices => CheckedDeviceCount > 0;

    /// <inheritdoc />
    public override async Task OnNavigatedToAsync(CancellationToken cancellationToken) =>
        await RunOperationAsync(LoadAsync, cancellationToken);

    [RelayCommand]
    private void NewDevice()
    {
        Editor.OpenNew();
        IsDeletePending = false;
    }

    [RelayCommand]
    private void EditDevice(DeviceDefinition? device)
    {
        if (device is null) return;
        Editor.Open(device);
        IsDeletePending = false;
    }

    [RelayCommand]
    private void CancelDeviceEdit() => Editor.IsOpen = false;

    [RelayCommand]
    private async Task SaveDeviceAsync() => await RunOperationAsync(async token =>
    {
        (DeviceDefinition device, IReadOnlyList<string> errors) = Editor.Build();
        if (errors.Count > 0) { Notification.Show(string.Join(' ', errors)); return; }
        await apiClient.SaveDeviceAsync(device, Editor.IsNew, token);
        Editor.IsOpen = false;
        Notification.Show("设备配置已保存，采集任务正在热加载。");
        await LoadAsync(token);
    });

    [RelayCommand]
    private async Task TestDeviceAsync()
    {
        if (SelectedDevice is null) { Notification.Show("请先选择一台设备。"); return; }
        await RunOperationAsync(async token =>
        {
            Models.DeviceConnectionTestDto result = await apiClient.TestDeviceAsync(SelectedDevice.Id, token);
            Notification.Show(result.Message);
        });
    }

    [RelayCommand]
    private void RequestDeleteDevice()
    {
        if (!HasCheckedDevices) Notification.Show("请先勾选要删除的设备。");
        else IsDeletePending = true;
    }

    [RelayCommand]
    private void CancelDeleteDevice() => IsDeletePending = false;

    [RelayCommand]
    private async Task ConfirmDeleteDeviceAsync()
    {
        DeviceDefinition[] selected = DeviceRows.Where(static row => row.IsChecked).Select(static row => row.Item).ToArray();
        if (selected.Length == 0) return;
        await RunOperationAsync(async token =>
        {
            int deleted = 0;
            List<string> failures = [];
            foreach (DeviceDefinition device in selected)
            {
                try
                {
                    await apiClient.DeleteDeviceAsync(device.Id, token);
                    deleted++;
                }
                catch (CollectorApiException exception)
                {
                    failures.Add($"{device.Name}：{exception.Message}");
                }
            }
            IsDeletePending = false;
            await LoadAsync(token);
            Notification.Show(failures.Count == 0
                ? $"已删除 {deleted} 台设备。"
                : $"已删除 {deleted} 台设备，{failures.Count} 台删除失败。{string.Join(' ', failures)}");
        });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<DeviceDefinition> devices = await apiClient.GetDevicesAsync(cancellationToken);
            Replace(Devices, devices);
            ReplaceRows(DeviceRows, devices);
            SelectedDevice = null;
            SelectedDeviceRow = null;
        }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }

    /// <summary>同步表格行包装器并监听批量勾选状态。</summary>
    private void ReplaceRows(ObservableCollection<SelectableRow<DeviceDefinition>> target, IEnumerable<DeviceDefinition> source)
    {
        foreach (SelectableRow<DeviceDefinition> row in target) row.PropertyChanged -= OnRowPropertyChanged;
        target.Clear();
        foreach (DeviceDefinition device in source)
        {
            SelectableRow<DeviceDefinition> row = new(device);
            row.PropertyChanged += OnRowPropertyChanged;
            target.Add(row);
        }
        NotifyCheckedStateChanged();
    }

    /// <summary>当前焦点行变化时保持连接测试所需的设备选择。</summary>
    partial void OnSelectedDeviceRowChanged(SelectableRow<DeviceDefinition>? value) => SelectedDevice = value?.Item;

    /// <summary>复选框变化后刷新删除按钮状态和确认数量。</summary>
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableRow<DeviceDefinition>.IsChecked)) NotifyCheckedStateChanged();
    }

    private void NotifyCheckedStateChanged()
    {
        OnPropertyChanged(nameof(CheckedDeviceCount));
        OnPropertyChanged(nameof(HasCheckedDevices));
    }
}
