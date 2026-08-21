// 文件说明：验证强类型导航只激活当前页面并执行离开生命周期。
// 责任边界：使用内存桩，不启动状态轮询或命名管道连接。

using ForgeLink.Desktop.Models;
using ForgeLink.Desktop.Navigation;
using ForgeLink.Desktop.Services;
using ForgeLink.Desktop.ViewModels;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Desktop.Tests;

public sealed class ShellNavigationTests
{
    [Fact]
    public async Task Navigate_ShouldDeactivateOldPageAndActivateNewPage()
    {
        NotificationService notifications = new();
        TrackingPage overview = new(notifications);
        TrackingPage devices = new(notifications);
        StubApiClient api = new();
        SystemStatusMonitor status = new(api);
        Dictionary<AppRoute, PageViewModelBase> pages = Enum.GetValues<AppRoute>()
            .ToDictionary(route => route, route => (PageViewModelBase)(route == AppRoute.Overview ? overview : devices));
        using StubDisposable resource = new();
        ShellViewModel shell = new(pages, status, notifications, resource);
        notifications.Show("旧页面消息");

        await shell.NavigateCommand.ExecuteAsync(AppRoute.Devices);

        Assert.Equal(1, overview.LeftCount);
        Assert.Equal(1, devices.EnteredCount);
        Assert.Equal(AppRoute.Devices, shell.CurrentRoute);
        Assert.Same(devices, shell.CurrentPage);
        Assert.False(notifications.HasMessage);
        await shell.DisposeAsync();
        Assert.True(resource.Disposed);
    }

    private sealed class TrackingPage(NotificationService notification) : PageViewModelBase(notification)
    {
        public int EnteredCount { get; private set; }
        public int LeftCount { get; private set; }
        public override Task OnNavigatedToAsync(CancellationToken cancellationToken) { EnteredCount++; return Task.CompletedTask; }
        public override Task OnNavigatedFromAsync(CancellationToken cancellationToken) { LeftCount++; return Task.CompletedTask; }
    }

    private sealed class StubDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class StubApiClient : ICollectorApiClient
    {
        public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new StatusDto(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, "Disabled", 0, 1));
        public Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceDefinition>>([]);
        public Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PointDefinition>>([]);
        public Task<IReadOnlyList<RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RealtimeValueDto>>([]);
        public Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken) => Task.FromResult(device);
        public Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult(new DeviceConnectionTestDto(true, "OK", DateTimeOffset.UtcNow));
        public Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken) => Task.FromResult(point);
        public Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<byte[]> ExportPointsAsync(CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
        public Task<int> ImportPointsAsync(string csv, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<HistoryConfigurationDto> GetHistoryConfigurationAsync(CancellationToken cancellationToken) => Task.FromResult(new HistoryConfigurationDto(false, "127.0.0.1", 6041, "", false, "", false, true, true, 10000, "NotConfigured"));
        public Task<HistoryConfigurationDto> SaveHistoryConfigurationAsync(HistoryConfigurationUpdateDto configuration, CancellationToken cancellationToken) => Task.FromResult(new HistoryConfigurationDto(true, configuration.Host, configuration.Port, configuration.Username, true, configuration.Database, configuration.UseSsl, configuration.EnableCompression, configuration.AutoReconnect, configuration.RequestTimeoutMs, "Disabled"));
        public Task<HistoryOperationDto> TestHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(new HistoryOperationDto(true, "OK", "Ready"));
        public Task<HistoryOperationDto> EnableHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(new HistoryOperationDto(true, "OK", "Enabled"));
        public Task<HistoryOperationDto> DisableHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(new HistoryOperationDto(true, "OK", "Disabled"));
        public Task<IReadOnlyList<PointValue>> QueryHistoryAsync(IReadOnlyList<Guid> pointIds, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PointValue>>([]);
    }
}
