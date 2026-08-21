// 文件说明：声明桌面页面访问 Collector Service 的统一契约。
// 责任边界：页面只依赖该接口，不感知命名管道和 HTTP 传输细节。

using ForgeLink.Desktop.Models;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.Services;

/// <summary>定义桌面端所需的 Collector Service 操作。</summary>
public interface ICollectorApiClient
{
    Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken);
    Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken);
    Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken);
    Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken);
    Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken);
    Task<byte[]> ExportPointsAsync(CancellationToken cancellationToken);
    Task<int> ImportPointsAsync(string csv, CancellationToken cancellationToken);
}
