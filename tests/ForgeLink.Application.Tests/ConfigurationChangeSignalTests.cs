// 文件说明：验证配置变化信号的修订递增和等待行为。
// 责任边界：仅测试进程内协调，不启动采集任务或访问数据库。

using ForgeLink.Application;
using Xunit;

namespace ForgeLink.Application.Tests;

/// <summary>覆盖配置热加载信号的核心并发语义。</summary>
public sealed class ConfigurationChangeSignalTests
{
    /// <summary>确认发布变化后等待方能够观察到更高修订号。</summary>
    [Fact]
    public async Task RequestReload_ShouldAdvanceRevisionAndReleaseWaiter()
    {
        ConfigurationChangeSignal signal = new();
        long observed = signal.Revision;
        signal.RequestReload();
        long revision = await signal.WaitForChangeAsync(observed, TestContext.Current.CancellationToken);
        Assert.Equal(1, revision);
        Assert.Equal(1, signal.Revision);
    }
}
