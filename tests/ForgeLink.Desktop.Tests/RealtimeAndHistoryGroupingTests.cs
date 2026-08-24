// 文件说明：验证实时数据与历史趋势页面复用设备、点位分组导航。
// 责任边界：只使用内存桩，不连接 PLC、Collector Service 或 TDengine。

using System.Globalization;
using ForgeLink.Desktop.Converters;
using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels.Pages;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Desktop.Tests;

public sealed class RealtimeAndHistoryGroupingTests
{
    /// <summary>确认实时快照会关联点位可读信息，并可按设备内分组切换。</summary>
    [Fact]
    public async Task Realtime_ShouldJoinMetadataAndFilterByGroup()
    {
        DeviceDefinition device = CreateDevice("一号 PLC");
        PointDefinition temperature = CreatePoint(device.Id, "TEMP_01", "入口温度", "温控", HistoryRecordMode.EverySample);
        PointDefinition pressure = CreatePoint(device.Id, "PRESSURE_01", "管路压力", "空压", HistoryRecordMode.EverySample);
        StubApiClient api = new([device], [temperature, pressure],
        [
            CreateRealtime(temperature, 25.2),
            CreateRealtime(pressure, 0.68)
        ], new(true, "127.0.0.1", 6041, "root", true, "forge_link", false, true, true, 10000, "Enabled"));
        RealtimeViewModel viewModel = new(api, new NotificationService());

        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, viewModel.Values.Count);
        Assert.Contains(viewModel.Values, row => row.Point.Code == "TEMP_01" && Equals(row.Value.EngineeringValue, 25.2));
        viewModel.SelectedPointGroupFilter = viewModel.PointGroupFilters.Single(group => group.Key == "空压");
        Assert.Equal("PRESSURE_01", Assert.Single(viewModel.Values).Point.Code);

        await viewModel.OnNavigatedFromAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>确认历史点位先按设备和分组筛选，并明确反馈历史通道未启用。</summary>
    [Fact]
    public async Task History_ShouldGroupEligiblePointsAndExposeDisabledState()
    {
        DeviceDefinition device = CreateDevice("一号 PLC");
        PointDefinition temperature = CreatePoint(device.Id, "TEMP_01", "入口温度", "温控", HistoryRecordMode.EverySample);
        PointDefinition pressure = CreatePoint(device.Id, "PRESSURE_01", "管路压力", "空压", HistoryRecordMode.ChangeWithHeartbeat);
        PointDefinition noHistory = CreatePoint(device.Id, "RUNNING_01", "运行状态", "状态", HistoryRecordMode.None);
        StubApiClient api = new([device], [temperature, pressure, noHistory], [],
            new(true, "127.0.0.1", 6041, "root", true, "forge_link", false, true, true, 10000, "Disabled"));
        HistoryViewModel viewModel = new(api, new NotificationService());

        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsHistoryAvailable);
        Assert.Contains("未启用", viewModel.HistoryAvailabilityMessage);
        Assert.DoesNotContain(viewModel.Points, point => point.Id == noHistory.Id);
        viewModel.SelectedPointGroupFilter = viewModel.PointGroupFilters.Single(group => group.Key == "温控");
        Assert.Equal("TEMP_01", Assert.Single(viewModel.FilteredPoints).Code);
    }

    /// <summary>确认日期时间选择值按系统时区组合，并以 UTC 范围提交历史查询。</summary>
    [Fact]
    public async Task History_ShouldComposePickerValuesAndQueryInUtc()
    {
        DeviceDefinition device = CreateDevice("一号 PLC");
        PointDefinition point = CreatePoint(device.Id, "TEMP_01", "入口温度", "温控", HistoryRecordMode.EverySample);
        StubApiClient api = new([device], [point], [],
            new(true, "127.0.0.1", 6041, "root", true, "forge_link", false, true, true, 10000, "Enabled"));
        HistoryViewModel viewModel = new(api, new NotificationService());
        await viewModel.OnNavigatedToAsync(TestContext.Current.CancellationToken);
        DateTime fromLocal = new(2026, 8, 24, 10, 15, 30, DateTimeKind.Local);
        DateTime toLocal = fromLocal.AddMinutes(15);
        viewModel.FromLocalDateTime = fromLocal;
        viewModel.ToLocalDateTime = toLocal;

        await viewModel.QueryCommand.ExecuteAsync(null);

        Assert.Equal(new DateTimeOffset(fromLocal).ToUniversalTime(), api.LastHistoryFromUtc);
        Assert.Equal(new DateTimeOffset(toLocal).ToUniversalTime(), api.LastHistoryToUtc);
    }

    /// <summary>确认 UTC 采集时间使用系统时区和固定三位毫秒展示。</summary>
    [Fact]
    public void LocalDateTimeConverter_ShouldUseSystemTimezoneAndMillisecondFormat()
    {
        DateTimeOffset utc = new(2026, 8, 24, 11, 47, 12, 345, TimeSpan.Zero);
        LocalDateTimeConverter converter = new();

        object text = converter.Convert(utc, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), text);
    }

    private static DeviceDefinition CreateDevice(string name) => new(Guid.NewGuid(), name, "Modbus TCP", "127.0.0.1", 502, true, 1000);

    private static PointDefinition CreatePoint(Guid deviceId, string code, string name, string group, HistoryRecordMode historyMode) =>
        new(Guid.NewGuid(), deviceId, code, name, "0", PointDataType.Float, 1, 0, "", 1000, 0, historyMode, true, false, GroupName: group);

    private static RealtimeValueDto CreateRealtime(PointDefinition point, object value) =>
        new(point.DeviceId, point.Id, value, value, point.DataType, point.Unit, DataQuality.Good, DateTimeOffset.UtcNow, 1);

    private sealed class StubApiClient(
        IReadOnlyList<DeviceDefinition> devices,
        IReadOnlyList<PointDefinition> points,
        IReadOnlyList<RealtimeValueDto> realtime,
        HistoryConfigurationDto historyConfiguration) : ICollectorApiClient
    {
        public DateTimeOffset? LastHistoryFromUtc { get; private set; }
        public DateTimeOffset? LastHistoryToUtc { get; private set; }

        public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult(devices);
        public Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken) => Task.FromResult(points);
        public Task<IReadOnlyList<RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken) => Task.FromResult(realtime);
        public Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> ExportPointsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> ImportPointsAsync(string csv, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryConfigurationDto> GetHistoryConfigurationAsync(CancellationToken cancellationToken) => Task.FromResult(historyConfiguration);
        public Task<HistoryConfigurationDto> SaveHistoryConfigurationAsync(HistoryConfigurationUpdateDto configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryOperationDto> TestHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryOperationDto> EnableHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HistoryOperationDto> DisableHistoryAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PointValue>> QueryHistoryAsync(IReadOnlyList<Guid> pointIds, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken cancellationToken)
        {
            LastHistoryFromUtc = fromUtc;
            LastHistoryToUtc = toUtc;
            return Task.FromResult<IReadOnlyList<PointValue>>([]);
        }
    }
}
