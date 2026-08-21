// 文件说明：声明历史通道、门禁状态和查询写入契约。
// 责任边界：不泄露 InfluxDB 版本专用的客户端类型。

using ForgeLink.Domain;

namespace ForgeLink.History.Abstractions;

/// <summary>定义历史存储门禁状态。</summary>
public enum HistoryGateState { NotConfigured, Disabled, Testing, Failed, Ready, Enabled, Degraded }

/// <summary>表示历史通道测试结果。</summary>
public sealed record HistoryChannelTestResult(bool Succeeded, string Message);

/// <summary>表示历史批量写入结果。</summary>
public sealed record HistoryWriteResult(int SucceededCount, int FailedCount, string? Error);

/// <summary>表示历史查询条件。</summary>
public sealed record HistoryQuery(IReadOnlyList<Guid> PointIds, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int MaxPoints);

/// <summary>表示历史查询结果。</summary>
public sealed record HistoryQueryResult(IReadOnlyList<PointValue> Values, string? Error);

/// <summary>定义可替换的历史数据通道。</summary>
public interface IHistoryChannel
{
    /// <summary>验证连接、权限及读写能力。</summary>
    Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken);

    /// <summary>批量写入历史点位值。</summary>
    Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken);

    /// <summary>按时间范围查询历史点位值。</summary>
    Task<HistoryQueryResult> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);
}
