// 文件说明：提供设备页面列表、连接测试和 CRUD 协调。
// 责任边界：设备表单状态由 DeviceEditorViewModel 管理，通信通过 ICollectorApiClient。

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Editors;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Pages;

/// <summary>表示设备管理页面。</summary>
public partial class DevicesViewModel(ICollectorApiClient apiClient, NotificationService notification)
    : PageViewModelBase(notification)
{
    [ObservableProperty] private DeviceDefinition? _selectedDevice;
    [ObservableProperty] private bool _isDeletePending;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DeviceDefinition> Devices { get; } = [];
    public DeviceEditorViewModel Editor { get; } = new();
    public NotificationService Messages => Notification;

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
    private void EditDevice()
    {
        if (SelectedDevice is null) { Notification.Show("请先选择一台设备。"); return; }
        Editor.Open(SelectedDevice);
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
        if (SelectedDevice is null) Notification.Show("请先选择一台设备。");
        else IsDeletePending = true;
    }

    [RelayCommand]
    private void CancelDeleteDevice() => IsDeletePending = false;

    [RelayCommand]
    private async Task ConfirmDeleteDeviceAsync()
    {
        if (SelectedDevice is null) return;
        Guid id = SelectedDevice.Id;
        await RunOperationAsync(async token =>
        {
            await apiClient.DeleteDeviceAsync(id, token);
            IsDeletePending = false;
            Notification.Show("设备已删除。");
            await LoadAsync(token);
        });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            IReadOnlyList<DeviceDefinition> devices = await apiClient.GetDevicesAsync(cancellationToken);
            Replace(Devices, devices);
            SelectedDevice = null;
        }
        finally { IsBusy = false; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (T item in source) target.Add(item);
    }
}
