// 文件说明：实现历史通道启用门禁与状态转换。
// 责任边界：只控制验证和启用状态，不持久化点位历史或凭据。

using ForgeLink.History.Abstractions;

namespace ForgeLink.Application;

/// <summary>确保历史通道只有完整测试成功后才可启用。</summary>
public sealed class HistoryGate(IHistoryChannel channel)
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private int _state = (int)HistoryGateState.NotConfigured;

    /// <summary>获取当前历史门禁状态。</summary>
    public HistoryGateState State => (HistoryGateState)Volatile.Read(ref _state);

    /// <summary>把完整配置置为待测试的禁用状态。</summary>
    public void MarkConfigured() => SetState(HistoryGateState.Disabled);

    /// <summary>连接参数变化后关闭通道并要求重新测试。</summary>
    public void MarkConfigurationChanged() => SetState(HistoryGateState.Disabled);

    /// <summary>服务启动时从已持久化的测试和启用状态恢复门禁。</summary>
    public void Restore(bool isConfigured, bool testPassed, bool isEnabled)
    {
        SetState(!isConfigured
            ? HistoryGateState.NotConfigured
            : testPassed
                ? isEnabled ? HistoryGateState.Enabled : HistoryGateState.Ready
                : HistoryGateState.Disabled);
    }

    /// <summary>执行通道测试并更新门禁状态。</summary>
    public async Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(HistoryGateState.Testing);
            HistoryChannelTestResult result = await channel.TestAsync(cancellationToken).ConfigureAwait(false);
            SetState(result.Succeeded ? HistoryGateState.Ready : HistoryGateState.Failed);
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
        SetState(HistoryGateState.Enabled);
    }

    /// <summary>显式关闭历史通道且不积累待补写数据。</summary>
    public void Disable() => SetState(HistoryGateState.Disabled);

    /// <summary>运行写入失败后进入降级状态并继续允许有界缓冲。</summary>
    public void MarkDegraded()
    {
        Interlocked.CompareExchange(ref _state, (int)HistoryGateState.Degraded, (int)HistoryGateState.Enabled);
    }

    /// <summary>降级后的首次成功写入恢复启用状态。</summary>
    public void MarkRecovered()
    {
        Interlocked.CompareExchange(ref _state, (int)HistoryGateState.Enabled, (int)HistoryGateState.Degraded);
    }

    /// <summary>判断当前值是否可以提交给历史通道。</summary>
    public bool CanWrite => State is HistoryGateState.Enabled or HistoryGateState.Degraded;

    private void SetState(HistoryGateState state) => Volatile.Write(ref _state, (int)state);
}
