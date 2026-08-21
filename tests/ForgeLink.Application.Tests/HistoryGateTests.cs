// 文件说明：验证历史门禁状态机与启用约束。
// 责任边界：使用内存桩通道，不访问真实 InfluxDB。

using ForgeLink.Application;
using ForgeLink.Domain;
using ForgeLink.History.Abstractions;
using Xunit;

namespace ForgeLink.Application.Tests;

/// <summary>覆盖历史通道的关键安全状态转换。</summary>
public sealed class HistoryGateTests
{
    /// <summary>确认未测试通过时不能启用历史写入。</summary>
    [Fact]
    public void Enable_ShouldFailBeforeSuccessfulTest()
    {
        HistoryGate gate = new(new StubHistoryChannel(true));
        gate.MarkConfigured();
        Assert.Throws<InvalidOperationException>(gate.Enable);
    }

    /// <summary>确认测试成功后仍需用户显式启用。</summary>
    [Fact]
    public async Task TestAsync_ShouldBecomeReadyThenEnable()
    {
        HistoryGate gate = new(new StubHistoryChannel(true));
        gate.MarkConfigured();
        HistoryChannelTestResult result = await gate.TestAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(HistoryGateState.Ready, gate.State);
        Assert.False(gate.CanWrite);
        gate.Enable();
        Assert.True(gate.CanWrite);
    }

    /// <summary>确认连接参数变化会自动关闭已启用通道。</summary>
    [Fact]
    public async Task ConfigurationChange_ShouldDisableEnabledChannel()
    {
        HistoryGate gate = new(new StubHistoryChannel(true));
        gate.MarkConfigured();
        await gate.TestAsync(CancellationToken.None);
        gate.Enable();
        gate.MarkConfigurationChanged();
        Assert.Equal(HistoryGateState.Disabled, gate.State);
        Assert.False(gate.CanWrite);
    }

    /// <summary>提供可控制测试结果的历史通道桩。</summary>
    private sealed class StubHistoryChannel(bool succeeds) : IHistoryChannel
    {
        /// <inheritdoc />
        public Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryChannelTestResult(succeeds, succeeds ? "成功" : "失败"));

        /// <inheritdoc />
        public Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryWriteResult(values.Count, 0, null));

        /// <inheritdoc />
        public Task<HistoryQueryResult> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new HistoryQueryResult([], null));
    }
}
