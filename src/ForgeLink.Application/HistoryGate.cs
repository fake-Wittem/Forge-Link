// 文件说明：实现历史通道启用门禁与状态转换。
// 责任边界：只控制验证和启用状态，不持久化点位历史或凭据。

using ForgeLink.History.Abstractions;

namespace ForgeLink.Application;

/// <summary>确保历史通道只有完整测试成功后才可启用。</summary>
public sealed class HistoryGate(IHistoryChannel channel)
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>获取当前历史门禁状态。</summary>
    public HistoryGateState State { get; private set; } = HistoryGateState.NotConfigured;

    /// <summary>把完整配置置为待测试的禁用状态。</summary>
    public void MarkConfigured() => State = HistoryGateState.Disabled;

    /// <summary>连接参数变化后关闭通道并要求重新测试。</summary>
    public void MarkConfigurationChanged() => State = HistoryGateState.Disabled;

    /// <summary>执行通道测试并更新门禁状态。</summary>
    public async Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            State = HistoryGateState.Testing;
            HistoryChannelTestResult result = await channel.TestAsync(cancellationToken).ConfigureAwait(false);
            State = result.Succeeded ? HistoryGateState.Ready : HistoryGateState.Failed;
            return result;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>在测试通过后显式启用历史通道。</summary>
    /// <exception cref="InvalidOperationException">尚未测试通过时抛出。</exception>
    public void Enable()
    {
        if (State != HistoryGateState.Ready) throw new InvalidOperationException("历史通道必须先通过完整连接测试。");
        State = HistoryGateState.Enabled;
    }

    /// <summary>显式关闭历史通道且不积累待补写数据。</summary>
    public void Disable() => State = HistoryGateState.Disabled;

    /// <summary>判断当前值是否可以提交给历史通道。</summary>
    public bool CanWrite => State is HistoryGateState.Enabled or HistoryGateState.Degraded;
}
