// 文件说明：配置 Collector Service 依赖、后台采集和仅限本机的管理 API。
// 责任边界：服务是配置、实时值和运行状态的唯一进程级入口。

using ForgeLink.Application;
using ForgeLink.History.Abstractions;
using ForgeLink.Infrastructure;
using ForgeLink.Persistence.Sqlite;
using ForgeLink.Protocols.Abstractions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

WebApplicationOptions options = new()
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
};
WebApplicationBuilder builder = WebApplication.CreateBuilder(options);
builder.Host.UseWindowsService(serviceOptions => serviceOptions.ServiceName = "ForgeLink Collector");
string pipeName = builder.Configuration["ForgeLink:PipeName"] ?? "ForgeLink.Collector";
builder.WebHost.UseNamedPipes(pipeOptions =>
{
    PipeSecurity security = new();
    SecurityIdentifier localUsers = new(WellKnownSidType.BuiltinUsersSid, null);
    security.AddAccessRule(new PipeAccessRule(
        localUsers,
        PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
        AccessControlType.Allow));
    pipeOptions.CurrentUserOnly = false;
    pipeOptions.PipeSecurity = security;
});
builder.WebHost.ConfigureKestrel(serverOptions =>
    serverOptions.ListenNamedPipe(pipeName, listenOptions => listenOptions.Protocols = HttpProtocols.Http1));

string dataRoot = builder.Configuration["ForgeLink:DataRoot"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ForgeLink");
string configDatabase = Path.Combine(dataRoot, "config", "forgelink-config.db");

builder.Services.AddSingleton<ISecretProtector, WindowsDpapiSecretProtector>();
builder.Services.AddSingleton<IConfigurationRepository>(services =>
    new SqliteConfigurationRepository(configDatabase, services.GetRequiredService<ISecretProtector>()));
builder.Services.AddSingleton<RealtimeValueStore>();
builder.Services.AddSingleton<ICollectorMetrics, CollectorMetrics>();
builder.Services.AddSingleton<IConfigurationChangeSignal, ConfigurationChangeSignal>();
builder.Services.AddSingleton<IPlcDriverFactory, PlcDriverFactory>();
builder.Services.AddSingleton<DeviceConnectionTester>();
builder.Services.AddSingleton<PointCsvCodec>();
builder.Services.AddSingleton<DisabledHistoryChannel>();
builder.Services.AddSingleton(services => new SwitchableHistoryChannel(services.GetRequiredService<DisabledHistoryChannel>()));
builder.Services.AddSingleton<IHistoryChannel>(services => services.GetRequiredService<SwitchableHistoryChannel>());
builder.Services.AddSingleton<HistoryGate>();
builder.Services.AddSingleton<HistoryRecordPolicy>();
builder.Services.AddSingleton<HistoryBuffer>();
builder.Services.AddSingleton<HistoryConfigurationManager>();
builder.Services.AddHostedService<CollectionWorker>();
builder.Services.AddHostedService<HistoryWriter>();
builder.Services.AddHealthChecks();

WebApplication app = builder.Build();
await app.Services.GetRequiredService<HistoryConfigurationManager>().InitializeAsync(CancellationToken.None);
app.MapHealthChecks("/health");
app.MapGet("/api/v1/status", (RealtimeValueStore store, ICollectorMetrics metrics, HistoryGate history) =>
    Results.Ok(metrics.CreateSnapshot(store.Count, history.State.ToString())));
app.MapGet("/api/v1/devices", async (IConfigurationRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetDevicesAsync(cancellationToken)));
app.MapPost("/api/v1/devices", async (DeviceUpdateRequest request, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    DeviceUpdateRequest normalized = request with { Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id };
    ForgeLink.Domain.DeviceDefinition device = normalized.ToDomain();
    IReadOnlyList<string> errors = device.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(CreateValidationErrors(errors));
    await repository.SaveDeviceAsync(device, cancellationToken);
    changes.RequestReload();
    return Results.Created($"/api/v1/devices/{device.Id:D}", device);
});
app.MapPut("/api/v1/devices/{id:guid}", async (Guid id, DeviceUpdateRequest request, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    ForgeLink.Domain.DeviceDefinition device = (request with { Id = id }).ToDomain();
    IReadOnlyList<string> errors = device.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(CreateValidationErrors(errors));
    await repository.SaveDeviceAsync(device, cancellationToken);
    changes.RequestReload();
    return Results.Ok(device);
});
app.MapDelete("/api/v1/devices/{id:guid}", async (Guid id, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    IReadOnlyList<ForgeLink.Domain.PointDefinition> points = await repository.GetPointsAsync(cancellationToken);
    if (points.Any(point => point.DeviceId == id))
        return Results.Conflict(new { message = "设备仍有关联点位，请先删除或迁移这些点位。" });
    bool deleted = await repository.DeleteDeviceAsync(id, cancellationToken);
    if (!deleted) return Results.NotFound();
    changes.RequestReload();
    return Results.NoContent();
});
app.MapPost("/api/v1/devices/{id:guid}/test", async (Guid id, IConfigurationRepository repository, DeviceConnectionTester tester, CancellationToken cancellationToken) =>
{
    ForgeLink.Domain.DeviceDefinition? device = (await repository.GetDevicesAsync(cancellationToken)).SingleOrDefault(item => item.Id == id);
    if (device is null) return Results.NotFound();
    IReadOnlyList<ForgeLink.Domain.PointDefinition> points = (await repository.GetPointsAsync(cancellationToken))
        .Where(point => point.DeviceId == id).ToArray();
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(10));
    DeviceConnectionTestResult result = await tester.TestAsync(device, points, timeout.Token);
    return result.Succeeded ? Results.Ok(result) : Results.UnprocessableEntity(result);
});
app.MapGet("/api/v1/points", async (IConfigurationRepository repository, CancellationToken cancellationToken) =>
    Results.Ok(await repository.GetPointsAsync(cancellationToken)));
app.MapPost("/api/v1/points", async (PointUpdateRequest request, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    PointUpdateRequest normalized = request with { Id = request.Id == Guid.Empty ? Guid.NewGuid() : request.Id };
    ForgeLink.Domain.PointDefinition point = normalized.ToDomain();
    IResult? validation = await ValidatePointAsync(point, repository, cancellationToken);
    if (validation is not null) return validation;
    await repository.SavePointAsync(point, cancellationToken);
    changes.RequestReload();
    return Results.Created($"/api/v1/points/{point.Id:D}", point);
});
app.MapPut("/api/v1/points/{id:guid}", async (Guid id, PointUpdateRequest request, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    ForgeLink.Domain.PointDefinition point = (request with { Id = id }).ToDomain();
    IResult? validation = await ValidatePointAsync(point, repository, cancellationToken);
    if (validation is not null) return validation;
    await repository.SavePointAsync(point, cancellationToken);
    changes.RequestReload();
    return Results.Ok(point);
});
app.MapDelete("/api/v1/points/{id:guid}", async (Guid id, IConfigurationRepository repository, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    bool deleted = await repository.DeletePointAsync(id, cancellationToken);
    if (!deleted) return Results.NotFound();
    changes.RequestReload();
    return Results.NoContent();
});
app.MapGet("/api/v1/points/export", async (IConfigurationRepository repository, PointCsvCodec codec, CancellationToken cancellationToken) =>
{
    string csv = codec.Export(await repository.GetPointsAsync(cancellationToken));
    byte[] payload = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv);
    return Results.File(payload, "text/csv; charset=utf-8", "forgelink-points.csv");
});
app.MapPost("/api/v1/points/import", async (HttpRequest request, IConfigurationRepository repository, PointCsvCodec codec, IConfigurationChangeSignal changes, CancellationToken cancellationToken) =>
{
    const int maximumBytes = 5 * 1024 * 1024;
    if (request.ContentLength > maximumBytes)
        return Results.BadRequest(new { errors = new[] { "CSV 文件不得超过 5 MB。" } });

    using StreamReader reader = new(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
    string csv = await reader.ReadToEndAsync(cancellationToken);
    if (Encoding.UTF8.GetByteCount(csv) > maximumBytes)
        return Results.BadRequest(new { errors = new[] { "CSV 文件不得超过 5 MB。" } });

    PointCsvParseResult parsed = codec.Parse(csv.TrimStart('\uFEFF'));
    if (parsed.Errors.Count > 0) return Results.BadRequest(new { errors = parsed.Errors });

    IReadOnlyList<ForgeLink.Domain.DeviceDefinition> devices = await repository.GetDevicesAsync(cancellationToken);
    IReadOnlyList<ForgeLink.Domain.PointDefinition> existingPoints = await repository.GetPointsAsync(cancellationToken);
    HashSet<Guid> deviceIds = devices.Select(static device => device.Id).ToHashSet();
    List<string> errors = [];
    foreach (IGrouping<Guid, ForgeLink.Domain.PointDefinition> group in parsed.Points.GroupBy(static point => point.Id).Where(static group => group.Count() > 1))
        errors.Add($"点位 ID {group.Key:D} 在 CSV 中重复。");
    foreach (IGrouping<string, ForgeLink.Domain.PointDefinition> group in parsed.Points.GroupBy(
        static point => $"{point.DeviceId:D}|{point.Code}", StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1))
        errors.Add($"设备与点位编码组合 {group.Key} 在 CSV 中重复。");
    foreach (ForgeLink.Domain.PointDefinition point in parsed.Points.Where(point => !deviceIds.Contains(point.DeviceId)))
        errors.Add($"点位 {point.Code} 的所属设备 {point.DeviceId:D} 不存在。");
    foreach (ForgeLink.Domain.PointDefinition point in parsed.Points.Where(point => existingPoints.Any(existing =>
        existing.Id != point.Id && existing.DeviceId == point.DeviceId
        && string.Equals(existing.Code, point.Code, StringComparison.OrdinalIgnoreCase))))
        errors.Add($"点位 {point.Code} 与数据库中的同设备点位编码重复。");
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    await repository.SavePointsAsync(parsed.Points, cancellationToken);
    changes.RequestReload();
    return Results.Ok(new { importedCount = parsed.Points.Count });
});
app.MapGet("/api/v1/realtime", (RealtimeValueStore store) => Results.Ok(store.Snapshot()));
app.MapGet("/api/v1/history/status", (HistoryGate history, HistoryBuffer buffer) =>
{
    HistoryBufferSnapshot snapshot = buffer.Snapshot();
    return Results.Ok(new
    {
        state = history.State.ToString(),
        history.CanWrite,
        snapshot.BufferedCount,
        snapshot.DroppedCount,
        snapshot.GapFromUtc,
        snapshot.GapToUtc
    });
});
app.MapGet("/api/v1/history/configuration", async (HistoryConfigurationManager manager, CancellationToken cancellationToken) =>
    Results.Ok(await manager.GetAsync(cancellationToken)));
app.MapPut("/api/v1/history/configuration", async (HistoryConfigurationRequest request, HistoryConfigurationManager manager, CancellationToken cancellationToken) =>
{
    (HistoryConfigurationResponse? response, IReadOnlyList<string> errors) = await manager.SaveAsync(request, cancellationToken);
    return errors.Count > 0 ? Results.ValidationProblem(CreateValidationErrors(errors)) : Results.Ok(response);
});
app.MapPost("/api/v1/history/test", async (HistoryConfigurationManager manager, CancellationToken cancellationToken) =>
{
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(120));
    HistoryOperationResponse response = await manager.TestAsync(timeout.Token);
    return response.Succeeded ? Results.Ok(response) : Results.UnprocessableEntity(response);
});
app.MapPost("/api/v1/history/enable", async (HistoryConfigurationManager manager, CancellationToken cancellationToken) =>
{
    HistoryOperationResponse response = await manager.EnableAsync(cancellationToken);
    return response.Succeeded ? Results.Ok(response) : Results.Conflict(response);
});
app.MapPost("/api/v1/history/disable", async (HistoryConfigurationManager manager, CancellationToken cancellationToken) =>
{
    HistoryOperationResponse response = await manager.DisableAsync(cancellationToken);
    return response.Succeeded ? Results.Ok(response) : Results.Conflict(response);
});
app.MapGet("/api/v1/history/query", async (
    string pointIds,
    DateTimeOffset fromUtc,
    DateTimeOffset toUtc,
    int maxPoints,
    HistoryGate history,
    IHistoryChannel channel,
    CancellationToken cancellationToken) =>
{
    if (!history.CanWrite)
        return Results.Problem(title: "历史存储未启用", detail: "请先配置、完整测试并启用 TDengine。", statusCode: StatusCodes.Status409Conflict);
    Guid[] ids = pointIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(static text => Guid.TryParse(text, out Guid id) ? id : Guid.Empty).ToArray();
    if (ids.Length is < 1 or > 100 || ids.Contains(Guid.Empty))
        return Results.ValidationProblem(CreateValidationErrors(["历史查询必须包含 1 到 100 个有效点位 ID。"]));
    if (fromUtc > toUtc || toUtc - fromUtc > TimeSpan.FromDays(7))
        return Results.ValidationProblem(CreateValidationErrors(["历史查询时间范围必须有效且不得超过 7 天。"]));
    if (maxPoints is < 1 or > 100_000)
        return Results.ValidationProblem(CreateValidationErrors(["历史查询最大点数必须在 1 到 100000 之间。"]));

    HistoryQueryResult result = await channel.QueryAsync(new(ids, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime(), maxPoints), cancellationToken);
    return string.IsNullOrWhiteSpace(result.Error)
        ? Results.Ok(result.Values)
        : Results.Problem(title: "TDengine 历史查询失败", detail: result.Error, statusCode: StatusCodes.Status502BadGateway);
});

await app.RunAsync();

/// <summary>把领域校验错误转换为标准 ValidationProblem 响应结构。</summary>
static Dictionary<string, string[]> CreateValidationErrors(IReadOnlyList<string> errors) => new()
{
    ["configuration"] = errors.ToArray()
};

/// <summary>校验点位自身约束、所属设备和同设备编码唯一性。</summary>
static async Task<IResult?> ValidatePointAsync(
    ForgeLink.Domain.PointDefinition point,
    IConfigurationRepository repository,
    CancellationToken cancellationToken)
{
    IReadOnlyList<string> errors = point.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(CreateValidationErrors(errors));

    IReadOnlyList<ForgeLink.Domain.DeviceDefinition> devices = await repository.GetDevicesAsync(cancellationToken);
    if (devices.All(device => device.Id != point.DeviceId))
        return Results.ValidationProblem(CreateValidationErrors(["所属设备不存在。"]));

    IReadOnlyList<ForgeLink.Domain.PointDefinition> points = await repository.GetPointsAsync(cancellationToken);
    bool duplicated = points.Any(existing => existing.Id != point.Id
        && existing.DeviceId == point.DeviceId
        && string.Equals(existing.Code, point.Code, StringComparison.OrdinalIgnoreCase));
    return duplicated
        ? Results.ValidationProblem(CreateValidationErrors(["同一设备下的点位编码必须唯一。"]))
        : null;
}

/// <summary>表示管理 API 接收的设备更新数据。</summary>
internal sealed record DeviceUpdateRequest(
    Guid Id,
    string Name,
    string Protocol,
    string Host,
    int Port,
    bool IsEnabled,
    int DefaultScanIntervalMs,
    byte UnitId = 1,
    int ConnectionTimeoutMs = 3000,
    int ReadTimeoutMs = 2000)
{
    /// <summary>转换为已与传入路径 ID 对齐的领域配置。</summary>
    internal ForgeLink.Domain.DeviceDefinition ToDomain() => new(
        Id, Name, Protocol, Host, Port, IsEnabled, DefaultScanIntervalMs, UnitId, ConnectionTimeoutMs, ReadTimeoutMs);
}

/// <summary>表示管理 API 接收的点位更新数据。</summary>
internal sealed record PointUpdateRequest(
    Guid Id,
    Guid DeviceId,
    string Code,
    string Name,
    string Address,
    ForgeLink.Domain.PointDataType DataType,
    double Scale,
    double Offset,
    string Unit,
    int ScanIntervalMs,
    double Deadband,
    ForgeLink.Domain.HistoryRecordMode HistoryMode,
    bool IsEnabled,
    bool AllowWrite,
    ForgeLink.Domain.RegisterByteOrder ByteOrder = ForgeLink.Domain.RegisterByteOrder.BigEndian,
    ForgeLink.Domain.RegisterWordOrder WordOrder = ForgeLink.Domain.RegisterWordOrder.HighWordFirst,
    int StringLength = 0)
{
    /// <summary>转换为领域点位配置。</summary>
    internal ForgeLink.Domain.PointDefinition ToDomain() => new(
        Id, DeviceId, Code, Name, Address, DataType, Scale, Offset, Unit,
        ScanIntervalMs, Deadband, HistoryMode, IsEnabled, AllowWrite, ByteOrder, WordOrder, StringLength);
}
