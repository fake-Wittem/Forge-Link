// 文件说明：验证设备表格批量勾选、删除和字段点击编辑命令。
// 责任边界：使用内存桩，不访问 Collector Service 或真实设备。

using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Pages;
using ForgeLink.Domain;
using Xunit;
using DesktopModels = ForgeLink.Desktop.Models;

namespace ForgeLink.Desktop.Tests;

public sealed class DevicesViewModelTests
{
    [Fact]
    public async Task CheckedRows_ShouldEnableBatchDeleteAndDeleteAllSelectedDevices()
    {
        DeviceDefinition first = CreateDevice("一号 PLC", 502);
        DeviceDefinition second = CreateDevice("二号 PLC", 503);
        DeviceDefinition third = CreateDevice("三号 PLC", 504);
        StubApiClient api = new([first, second, third]);
        DevicesViewModel viewModel = new(api, new NotificationService());
        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.HasCheckedDevices);
        viewModel.EditDeviceCommand.Execute(second);
        Assert.Equal(second.Id, viewModel.Editor.Id);
        viewModel.Editor.IsOpen = false;

        viewModel.DeviceRows[0].IsChecked = true;
        viewModel.DeviceRows[2].IsChecked = true;
        Assert.True(viewModel.HasCheckedDevices);
        Assert.Equal(2, viewModel.CheckedDeviceCount);

        viewModel.RequestDeleteDeviceCommand.Execute(null);
        Assert.True(viewModel.IsDeletePending);
        await viewModel.ConfirmDeleteDeviceCommand.ExecuteAsync(null);

        Assert.Equal([first.Id, third.Id], api.DeletedDeviceIds);
        Assert.Equal(second.Id, Assert.Single(viewModel.Devices).Id);
        Assert.False(viewModel.HasCheckedDevices);
    }

    private static DeviceDefinition CreateDevice(string name, int port) =>
        new(Guid.NewGuid(), name, "Simulation", "127.0.0.1", port, true, 1000);

    private sealed class StubApiClient(IReadOnlyList<DeviceDefinition> devices) : ICollectorApiClient
    {
        private readonly List<DeviceDefinition> _devices = devices.ToList();
        public List<Guid> DeletedDeviceIds { get; } = [];

        public Task<DesktopModels.StatusDto> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceDefinition>>(_devices.ToArray());
        public Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DesktopModels.RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
        {
            DeletedDeviceIds.Add(deviceId);
            _devices.RemoveAll(device => device.Id == deviceId);
            return Task.CompletedTask;
        }
        public Task<DesktopModels.DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> ExportPointsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ImportPointsAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.HistoryConfigurationDto> GetHistoryConfigurationAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.HistoryConfigurationDto> SaveHistoryConfigurationAsync(DesktopModels.HistoryConfigurationUpdateDto configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.HistoryOperationDto> TestHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.HistoryOperationDto> EnableHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.HistoryOperationDto> DisableHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PointValue>> QueryHistoryAsync(IReadOnlyList<Guid> pointIds, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
