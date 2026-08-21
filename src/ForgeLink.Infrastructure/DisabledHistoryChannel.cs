// 文件说明：实现默认关闭的历史通道。
// 责任边界：明确拒绝写入和查询，绝不把历史回退到 SQLite。

using ForgeLink.Domain;
using ForgeLink.History.Abstractions;

namespace ForgeLink.Infrastructure;

/// <summary>在未配置 InfluxDB 时提供明确且安全的历史行为。</summary>
public sealed class DisabledHistoryChannel : IHistoryChannel
{
    private const string Message = "历史存储未配置或未启用。";

    /// <inheritdoc />
    public Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HistoryChannelTestResult(false, Message));

    /// <inheritdoc />
    public Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken) =>
        Task.FromResult(new HistoryWriteResult(0, values.Count, Message));

    /// <inheritdoc />
    public Task<HistoryQueryResult> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new HistoryQueryResult([], Message));
}
