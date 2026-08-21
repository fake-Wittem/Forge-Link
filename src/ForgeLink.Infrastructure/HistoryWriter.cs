// 文件说明：把有界内存缓冲中的历史值批量写入当前历史通道。
// 责任边界：只负责刷新、重试和运行日志，不筛选点位记录策略。

using ForgeLink.Application;
using ForgeLink.History.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ForgeLink.Infrastructure;

/// <summary>按门禁状态批量刷新 TDengine，并在失败时指数退避。</summary>
public sealed class HistoryWriter(
    HistoryBuffer buffer,
    HistoryGate gate,
    IHistoryChannel channel,
    ILogger<HistoryWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retryDelay = buffer.Options.EffectiveInitialRetryInterval;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!gate.CanWrite)
                {
                    await Task.Delay(buffer.Options.EffectiveFlushInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                IReadOnlyList<ForgeLink.Domain.PointValue> batch = buffer.TakeBatch(DateTimeOffset.UtcNow);
                if (batch.Count == 0)
                {
                    await Task.Delay(buffer.Options.EffectiveFlushInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                HistoryWriteResult result = await channel.WriteAsync(batch, stoppingToken).ConfigureAwait(false);
                if (result.FailedCount > 0 || !string.IsNullOrWhiteSpace(result.Error))
                {
                    buffer.RequeueFront(batch, DateTimeOffset.UtcNow);
                    gate.MarkDegraded();
                    HistoryBufferSnapshot snapshot = buffer.Snapshot();
                    logger.LogWarning("TDengine 历史批量写入失败，缓冲 {BufferedCount} 条，{RetrySeconds} 秒后重试。原因：{Error}",
                        snapshot.BufferedCount, retryDelay.TotalSeconds, result.Error ?? "通道返回失败");
                    await Task.Delay(retryDelay, stoppingToken).ConfigureAwait(false);
                    retryDelay = TimeSpan.FromMilliseconds(Math.Min(
                        buffer.Options.EffectiveMaximumRetryInterval.TotalMilliseconds,
                        retryDelay.TotalMilliseconds * 2));
                    continue;
                }

                gate.MarkRecovered();
                retryDelay = buffer.Options.EffectiveInitialRetryInterval;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 服务停止时内存缓冲允许丢失，但必须留下明确日志。
        }
        finally
        {
            int discarded = buffer.DiscardAll();
            if (discarded > 0) logger.LogWarning("Collector Service 停止，丢弃尚未写入的内存历史值 {DiscardedCount} 条", discarded);
        }
    }
}
