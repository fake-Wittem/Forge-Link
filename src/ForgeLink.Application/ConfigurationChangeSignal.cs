// 文件说明：实现进程内配置变化通知，使采集任务可安全热加载。
// 责任边界：只传递修订信号，不保存配置内容或启动后台任务。

using System.Threading.Channels;

namespace ForgeLink.Application;

/// <summary>通过单元素有界通道合并短时间内的重复重载请求。</summary>
public sealed class ConfigurationChangeSignal : IConfigurationChangeSignal
{
    private readonly Channel<long> _changes = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private long _revision;

    /// <inheritdoc />
    public long Revision => Interlocked.Read(ref _revision);

    /// <inheritdoc />
    public void RequestReload()
    {
        long revision = Interlocked.Increment(ref _revision);
        _changes.Writer.TryWrite(revision);
    }

    /// <inheritdoc />
    public async Task<long> WaitForChangeAsync(long observedRevision, CancellationToken cancellationToken)
    {
        long current = Revision;
        if (current > observedRevision) return current;

        while (true)
        {
            long revision = await _changes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (revision > observedRevision) return revision;
        }
    }
}
