// 文件说明：验证点位管理页面的设备与自定义分组筛选。
// 责任边界：使用内存桩，不访问 Collector Service、文件系统或真实设备。

using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Pages;
using ForgeLink.Domain;
using Xunit;
using DesktopModels = ForgeLink.Desktop.Models;

namespace ForgeLink.Desktop.Tests;

public sealed class PointsViewModelTests
{
    /// <summary>确认页面先按设备隔离点位，再按设备内自定义分组筛选。</summary>
    [Fact]
    public async Task Navigation_ShouldFilterByDeviceAndPointGroup()
    {
        DeviceDefinition firstDevice = new(Guid.NewGuid(), "一号 PLC", "Simulation", "127.0.0.1", 502, true, 1000);
        DeviceDefinition secondDevice = new(Guid.NewGuid(), "二号 PLC", "Simulation", "127.0.0.1", 503, true, 1000);
        PointDefinition[] points =
        [
            CreatePoint(firstDevice.Id, "TEMP_01", "温控"),
            CreatePoint(firstDevice.Id, "PRESSURE_01", "空压"),
            CreatePoint(firstDevice.Id, "RUNNING_01", ""),
            CreatePoint(secondDevice.Id, "FLOW_01", "流量")
        ];
        PointsViewModel viewModel = new(new StubApiClient([firstDevice, secondDevice], points), new StubCsvFileService(), new NotificationService());

        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);

        Assert.Equal(firstDevice.Id, viewModel.SelectedDeviceFilter?.Device.Id);
        Assert.Equal(3, viewModel.VisiblePoints.Count);
        Assert.Contains(viewModel.PointGroupFilters, group => group.DisplayName == "未分组" && group.PointCount == 1);

        viewModel.SelectedPointGroupFilter = viewModel.PointGroupFilters.Single(group => group.Key == "温控");
        Assert.Equal("TEMP_01", Assert.Single(viewModel.VisiblePoints).Code);

        viewModel.SelectedDeviceFilter = viewModel.DeviceFilters.Single(item => item.Device.Id == secondDevice.Id);
        Assert.Equal("FLOW_01", Assert.Single(viewModel.VisiblePoints).Code);
        Assert.Equal(["全部点位", "流量"], viewModel.PointGroupFilters.Select(static group => group.DisplayName));
    }

    /// <summary>确认勾选状态驱动批量删除，并支持由点位编码直接打开编辑器。</summary>
    [Fact]
    public async Task CheckedRows_ShouldEnableBatchDeleteAndDeleteAllSelectedPoints()
    {
        DeviceDefinition device = new(Guid.NewGuid(), "一号 PLC", "Simulation", "127.0.0.1", 502, true, 1000);
        PointDefinition first = CreatePoint(device.Id, "TEMP_01", "温控");
        PointDefinition second = CreatePoint(device.Id, "TEMP_02", "温控");
        PointDefinition third = CreatePoint(device.Id, "TEMP_03", "温控");
        StubApiClient api = new([device], [first, second, third]);
        PointsViewModel viewModel = new(api, new StubCsvFileService(), new NotificationService());
        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.HasCheckedPoints);
        viewModel.EditPointCommand.Execute(second);
        Assert.Equal(second.Id, viewModel.Editor.Id);
        viewModel.Editor.IsOpen = false;

        viewModel.VisiblePointRows[0].IsChecked = true;
        viewModel.VisiblePointRows[2].IsChecked = true;
        Assert.True(viewModel.HasCheckedPoints);
        Assert.Equal(2, viewModel.CheckedPointCount);

        viewModel.RequestDeletePointCommand.Execute(null);
        Assert.True(viewModel.IsDeletePending);
        await viewModel.ConfirmDeletePointCommand.ExecuteAsync(null);

        Assert.Equal([first.Id, third.Id], api.DeletedPointIds);
        Assert.Equal(second.Id, Assert.Single(viewModel.VisiblePoints).Id);
        Assert.False(viewModel.HasCheckedPoints);
    }

    private static PointDefinition CreatePoint(Guid deviceId, string code, string groupName) => new(
        Guid.NewGuid(), deviceId, code, code, "D100", PointDataType.Double, 1, 0, string.Empty, 1000, 0,
        HistoryRecordMode.None, true, false, GroupName: groupName);

    private sealed class StubCsvFileService : IPointCsvFileService
    {
        public Task<bool> SaveAsync(byte[] content, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<string?> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class StubApiClient : ICollectorApiClient
    {
        private readonly IReadOnlyList<DeviceDefinition> _devices;
        private readonly List<PointDefinition> _points;

        public StubApiClient(IReadOnlyList<DeviceDefinition> devices, IReadOnlyList<PointDefinition> points)
        {
            _devices = devices;
            _points = points.ToList();
        }

        public List<Guid> DeletedPointIds { get; } = [];

        public Task<DesktopModels.StatusDto> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult(_devices);
        public Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PointDefinition>>(_points.ToArray());
        public Task<IReadOnlyList<DesktopModels.RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopModels.DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken)
        {
            DeletedPointIds.Add(pointId);
            _points.RemoveAll(point => point.Id == pointId);
            return Task.CompletedTask;
        }
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
