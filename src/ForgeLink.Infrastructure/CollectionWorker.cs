// 文件说明：按设备执行异步采集并通过有界队列更新实时缓存。
// 责任边界：当前首版使用统一周期，真实协议的连续地址合并由后续驱动实现。

using System.Threading.Channels;
using ForgeLink.Application;
using ForgeLink.Domain;
using ForgeLink.Protocols.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForgeLink.Infrastructure;

/// <summary>承载采集调度、值转换和实时缓存更新的后台服务。</summary>
public sealed class CollectionWorker(
    IConfigurationRepository repository,
    IPlcDriverFactory driverFactory,
    RealtimeValueStore realtimeStore,
    ICollectorMetrics metrics,
    IConfigurationChangeSignal configurationChanges,
    HistoryGate historyGate,
    HistoryRecordPolicy historyPolicy,
    HistoryBuffer historyBuffer,
    ILogger<CollectionWorker> logger) : BackgroundService
{
    private readonly Channel<PointValue> _pipeline = Channel.CreateBounded<PointValue>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true
    });
    private long _sequence;
    private IReadOnlyDictionary<Guid, PointDefinition> _historyDefinitions = new Dictionary<Guid, PointDefinition>();
    private int _historyPolicyResetRequested;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await repository.InitializeAsync(stoppingToken).ConfigureAwait(false);
        Task consumer = ConsumePipelineAsync(stoppingToken);
        long observedRevision = configurationChanges.Revision;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<DeviceDefinition> devices = await repository.GetDevicesAsync(stoppingToken).ConfigureAwait(false);
                IReadOnlyList<PointDefinition> points = await repository.GetPointsAsync(stoppingToken).ConfigureAwait(false);
                DeviceDefinition[] enabledDevices = devices.Where(static device => device.IsEnabled).ToArray();
                PointDefinition[] enabledPoints = points.Where(static point => point.IsEnabled).ToArray();
                Volatile.Write(ref _historyDefinitions, enabledPoints.ToDictionary(static point => point.Id));
                Interlocked.Exchange(ref _historyPolicyResetRequested, 1);
                realtimeStore.RetainOnly(enabledPoints.Select(static point => point.Id));
                // 在线数会在真实驱动阶段改为连接事件驱动；当前模拟驱动连接为确定性成功。
                metrics.UpdateConfiguration(devices.Count, enabledDevices.Length, enabledPoints.Length);

                using CancellationTokenSource generation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                List<Task> collectors = enabledDevices.Select(device => CollectDeviceAsync(
                    device,
                    enabledPoints.Where(point => point.DeviceId == device.Id).ToArray(),
                    generation.Token)).ToList();

                try
                {
                    observedRevision = await configurationChanges.WaitForChangeAsync(observedRevision, stoppingToken).ConfigureAwait(false);
                    logger.LogInformation("检测到配置修订 {Revision}，正在安全重建采集任务", observedRevision);
                }
                finally
                {
                    await generation.CancelAsync().ConfigureAwait(false);
                    await Task.WhenAll(collectors).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("采集服务配置循环已停止");
        }
        finally
        {
            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 服务停止时实时值消费队列随宿主令牌退出。
            }
        }
    }

    /// <summary>维持单台设备连接并按周期采集。</summary>
    private async Task CollectDeviceAsync(DeviceDefinition device, IReadOnlyList<PointDefinition> points, CancellationToken cancellationToken)
    {
        if (points.Count == 0) return;
        IPlcDriver driver;
        try
        {
            driver = driverFactory.Create(device);
        }
        catch (NotSupportedException exception)
        {
            logger.LogError(exception, "设备 {DeviceName} 的协议未注册，采集任务不会启动", device.Name);
            return;
        }

        await using (driver.ConfigureAwait(false))
        {
            int consecutiveFailures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    consecutiveFailures = 0;
                    await RunCollectionScheduleAsync(device, points, driver, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("设备 {DeviceName} 的采集任务已停止", device.Name);
                    break;
                }
                catch (Exception exception)
                {
                    consecutiveFailures++;
                    int exponent = Math.Min(consecutiveFailures - 1, 6);
                    int delayMs = Math.Min(60_000, (1 << exponent) * 1000) + Random.Shared.Next(0, 500);
                    logger.LogWarning(exception,
                        "设备 {DeviceName} 连接或采集失败，将在 {DelayMs} 毫秒后重试",
                        device.Name,
                        delayMs);
                    try
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                finally
                {
                    await driver.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>按每个点位自身周期选择到期点位，同一时刻到期的点位仍批量读取。</summary>
    private async Task RunCollectionScheduleAsync(
        DeviceDefinition device,
        IReadOnlyList<PointDefinition> points,
        IPlcDriver driver,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, DateTimeOffset> nextDue = points.ToDictionary(
            static point => point.Id,
            static _ => DateTimeOffset.UtcNow);
        while (!cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PointDefinition[] duePoints = points.Where(point => nextDue[point.Id] <= now).ToArray();
            if (duePoints.Length > 0)
            {
                await CollectOnceAsync(device, duePoints, driver, cancellationToken).ConfigureAwait(false);
                DateTimeOffset completedAt = DateTimeOffset.UtcNow;
                foreach (PointDefinition point in duePoints)
                    nextDue[point.Id] = completedAt.AddMilliseconds(point.ScanIntervalMs);
            }

            DateTimeOffset earliest = nextDue.Values.Min();
            TimeSpan delay = earliest - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.FromMilliseconds(10)) delay = TimeSpan.FromMilliseconds(10);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>读取单批点位并写入有界处理队列。</summary>
    private async Task CollectOnceAsync(DeviceDefinition device, IReadOnlyList<PointDefinition> points, IPlcDriver driver, CancellationToken cancellationToken)
    {
        IReadOnlyList<DriverTagValue> rawValues = await driver.ReadAsync(points, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, PointDefinition> definitions = points.ToDictionary(static point => point.Id);
        int failures = 0;
        foreach (DriverTagValue raw in rawValues)
        {
            PointDefinition definition = definitions[raw.PointId];
            if (raw.Quality != DataQuality.Good) failures++;
            PointValue value = new(
                Environment.MachineName,
                device.Id,
                raw.PointId,
                raw.RawValue,
                raw.Quality == DataQuality.Good ? definition.ToEngineeringValue(raw.RawValue) : null,
                definition.DataType,
                definition.Unit,
                raw.Quality,
                raw.SourceTimestampUtc,
                DateTimeOffset.UtcNow,
                Interlocked.Increment(ref _sequence));
            await _pipeline.Writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
        }
        metrics.RecordCollection(rawValues.Count, failures);
    }

    /// <summary>单线程消费处理队列以形成一致的实时快照。</summary>
    private async Task ConsumePipelineAsync(CancellationToken cancellationToken)
    {
        await foreach (PointValue value in _pipeline.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            realtimeStore.Update(value);
            if (Interlocked.Exchange(ref _historyPolicyResetRequested, 0) == 1) historyPolicy.ResetAll();
            IReadOnlyDictionary<Guid, PointDefinition> definitions = Volatile.Read(ref _historyDefinitions);
            if (historyGate.CanWrite && definitions.TryGetValue(value.PointId, out PointDefinition? definition))
            {
                if (historyPolicy.ShouldRecord(definition, value)) historyBuffer.Enqueue(value);
            }
            else
            {
                historyPolicy.Reset(value.PointId);
            }
            metrics.SetPipelineBacklog(_pipeline.Reader.Count);
        }
    }
}
