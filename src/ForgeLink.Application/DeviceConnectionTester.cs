// 文件说明：实现不影响正式采集连接的设备连接测试用例。
// 责任边界：使用独立驱动实例测试生命周期，不写 PLC 或修改配置。

using ForgeLink.Domain;
using ForgeLink.Protocols.Abstractions;

namespace ForgeLink.Application;

/// <summary>表示设备连接测试的结构化结果。</summary>
public sealed record DeviceConnectionTestResult(bool Succeeded, string Message, DateTimeOffset TestedAtUtc);

/// <summary>为管理端提供带超时的设备连接测试。</summary>
public sealed class DeviceConnectionTester(IPlcDriverFactory driverFactory)
{
    /// <summary>使用临时驱动连接并检查健康状态。</summary>
    public async Task<DeviceConnectionTestResult> TestAsync(
        DeviceDefinition device,
        IReadOnlyList<PointDefinition> points,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = device.Validate();
        if (errors.Count > 0) return new(false, string.Join(' ', errors), DateTimeOffset.UtcNow);

        try
        {
            await using IPlcDriver driver = driverFactory.Create(device);
            await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
            DriverHealth health = await driver.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            if (!health.IsConnected) return new(false, health.Message, DateTimeOffset.UtcNow);

            PointDefinition? probe = points.FirstOrDefault(static point => point.IsEnabled);
            if (probe is not null)
            {
                DriverTagValue value = (await driver.ReadAsync([probe], cancellationToken).ConfigureAwait(false)).Single();
                if (value.Quality != DataQuality.Good)
                    return new(false, $"TCP 已连接，但测试点位 {probe.Code} 读取失败：{value.Quality}。", DateTimeOffset.UtcNow);
                await driver.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                return new(true, $"连接成功，测试点位 {probe.Code} 读取质量为 Good。", DateTimeOffset.UtcNow);
            }
            await driver.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            return new(true, $"{health.Message}；设备尚无启用点位，仅验证了传输连接。", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, "连接测试已取消或超时。", DateTimeOffset.UtcNow);
        }
        catch (NotSupportedException exception)
        {
            return new(false, exception.Message, DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            return new(false, $"连接测试失败：{exception.Message}", DateTimeOffset.UtcNow);
        }
    }
}
