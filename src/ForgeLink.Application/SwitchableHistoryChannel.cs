// 文件说明：为运行中的 Collector Service 提供可安全替换的历史通道代理。
// 责任边界：只交换通道实例，不决定配置持久化或门禁状态。

using ForgeLink.Domain;
using ForgeLink.History.Abstractions;

namespace ForgeLink.Application;

/// <summary>把业务层的稳定通道引用转发到当前已配置实现。</summary>
public sealed class SwitchableHistoryChannel(IHistoryChannel initialChannel) : IHistoryChannel
{
    private IHistoryChannel _current = initialChannel ?? throw new ArgumentNullException(nameof(initialChannel));

    /// <summary>原子替换后续调用使用的历史通道。</summary>
    public void Replace(IHistoryChannel channel) => Interlocked.Exchange(ref _current, channel ?? throw new ArgumentNullException(nameof(channel)));

    public Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).TestAsync(cancellationToken);

    public Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).WriteAsync(values, cancellationToken);

    public Task<HistoryQueryResult> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
        Volatile.Read(ref _current).QueryAsync(query, cancellationToken);
}
