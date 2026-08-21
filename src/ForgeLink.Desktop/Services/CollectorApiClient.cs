// 文件说明：封装桌面端对 Collector Service 本机管理 API 的访问。
// 责任边界：桌面端不得绕过此服务直连 PLC、TDengine 或 SQLite。

using System.Net.Http;
using System.Net.Http.Json;
using System.IO.Pipes;
using System.Net;
using System.Security.Principal;
using System.Text;
using ForgeLink.Desktop.Models;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.Services;

/// <summary>提供可取消、带超时的本机服务查询。</summary>
public sealed class CollectorApiClient : ICollectorApiClient, IDisposable
{
    private const string DefaultPipeName = "ForgeLink.Collector";
    private readonly HttpClient _client;

    /// <summary>创建只通过本机命名管道访问服务的客户端。</summary>
    public CollectorApiClient()
    {
        string pipeName = Environment.GetEnvironmentVariable("ForgeLink__PipeName") ?? DefaultPipeName;
        SocketsHttpHandler handler = new()
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                NamedPipeClientStream stream = new(
                    ".", pipeName, PipeDirection.InOut,
                    PipeOptions.WriteThrough | PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Anonymous);
                try
                {
                    await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        };
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>读取系统概览。</summary>
    public async Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken) =>
        await _client.GetFromJsonAsync<StatusDto>("/api/v1/status", cancellationToken)
        ?? throw new InvalidOperationException("Collector Service 返回了空状态响应。");

    /// <summary>读取设备配置列表。</summary>
    public async Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken) =>
        await _client.GetFromJsonAsync<List<DeviceDefinition>>("/api/v1/devices", cancellationToken) ?? [];

    /// <summary>读取点位配置列表。</summary>
    public async Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken) =>
        await _client.GetFromJsonAsync<List<PointDefinition>>("/api/v1/points", cancellationToken) ?? [];

    /// <summary>读取最新实时值快照。</summary>
    public async Task<IReadOnlyList<RealtimeValueDto>> GetRealtimeAsync(CancellationToken cancellationToken) =>
        await _client.GetFromJsonAsync<List<RealtimeValueDto>>("/api/v1/realtime", cancellationToken) ?? [];

    /// <summary>新增或更新设备配置。</summary>
    public async Task<DeviceDefinition> SaveDeviceAsync(DeviceDefinition device, bool isNew, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = isNew
            ? await _client.PostAsJsonAsync("/api/v1/devices", device, cancellationToken)
            : await _client.PutAsJsonAsync($"/api/v1/devices/{device.Id:D}", device, cancellationToken);
        return await ReadRequiredAsync<DeviceDefinition>(response, cancellationToken);
    }

    /// <summary>删除没有关联点位的设备。</summary>
    public async Task DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.DeleteAsync($"/api/v1/devices/{deviceId:D}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>使用独立连接执行设备连通性测试。</summary>
    public async Task<DeviceConnectionTestDto> TestDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.PostAsync($"/api/v1/devices/{deviceId:D}/test", null, cancellationToken);
        return await ReadRequiredAsync<DeviceConnectionTestDto>(response, cancellationToken);
    }

    /// <summary>新增或更新点位配置。</summary>
    public async Task<PointDefinition> SavePointAsync(PointDefinition point, bool isNew, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = isNew
            ? await _client.PostAsJsonAsync("/api/v1/points", point, cancellationToken)
            : await _client.PutAsJsonAsync($"/api/v1/points/{point.Id:D}", point, cancellationToken);
        return await ReadRequiredAsync<PointDefinition>(response, cancellationToken);
    }

    /// <summary>删除指定点位配置。</summary>
    public async Task DeletePointAsync(Guid pointId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _client.DeleteAsync($"/api/v1/points/{pointId:D}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>下载点位配置 CSV。</summary>
    public async Task<byte[]> ExportPointsAsync(CancellationToken cancellationToken) =>
        await _client.GetByteArrayAsync("/api/v1/points/export", cancellationToken);

    /// <summary>上传点位配置 CSV 并返回导入数量。</summary>
    public async Task<int> ImportPointsAsync(string csv, CancellationToken cancellationToken)
    {
        using StringContent content = new(csv, Encoding.UTF8, "text/csv");
        using HttpResponseMessage response = await _client.PostAsync("/api/v1/points/import", content, cancellationToken);
        ImportResultDto result = await ReadRequiredAsync<ImportResultDto>(response, cancellationToken);
        return result.ImportedCount;
    }

    /// <summary>读取成功响应中的必需 JSON 对象，并在失败时保留服务端原因。</summary>
    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new InvalidOperationException("Collector Service 返回了空响应。");
        }
    }

    /// <summary>把非成功 HTTP 响应转换为可展示的操作异常。</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new CollectorApiException(string.IsNullOrWhiteSpace(detail)
            ? $"Collector Service 返回 HTTP {(int)response.StatusCode}。"
            : detail);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}

/// <summary>表示 Collector Service 返回的业务操作错误。</summary>
public sealed class CollectorApiException(string message) : Exception(message);
