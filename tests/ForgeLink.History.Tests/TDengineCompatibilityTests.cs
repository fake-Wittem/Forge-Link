// 文件说明：验证 TDengine 3.x 服务端版本的兼容分级规则。
// 责任边界：只检查版本判断，不连接本机或远程 TDengine 服务。

using ForgeLink.History.TDengine;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖不支持、尽力兼容和官方保证三类版本边界。</summary>
public sealed class TDengineCompatibilityTests
{
    /// <summary>确认本机 3.4.2.6 Enterprise 版本位于官方 WebSocket 兼容保证范围。</summary>
    [Fact]
    public void Evaluate_ShouldGuaranteeLocalServerVersion()
    {
        TDengineCompatibilityResult result = TDengineCompatibility.Evaluate("3.4.2.6.enterprise");

        Assert.True(result.IsSupported);
        Assert.True(result.IsOfficiallyGuaranteed);
        Assert.Equal(new Version(3, 4, 2, 6), result.ServerVersion);
    }

    /// <summary>确认较早的 3.x 被保留为需真实验收的尽力兼容范围。</summary>
    [Theory]
    [InlineData("3.0.0.0")]
    [InlineData("3.3.5.9")]
    public void Evaluate_ShouldMarkEarly3xAsBestEffort(string version)
    {
        TDengineCompatibilityResult result = TDengineCompatibility.Evaluate(version);

        Assert.True(result.IsSupported);
        Assert.False(result.IsOfficiallyGuaranteed);
        Assert.Contains("尽力兼容", result.Message, StringComparison.Ordinal);
    }

    /// <summary>确认 2.x 和无法解析的版本不会通过历史门禁。</summary>
    [Theory]
    [InlineData("2.6.0.0")]
    [InlineData("unknown")]
    public void Evaluate_ShouldRejectUnsupportedVersion(string version)
    {
        TDengineCompatibilityResult result = TDengineCompatibility.Evaluate(version);

        Assert.False(result.IsSupported);
        Assert.False(result.IsOfficiallyGuaranteed);
    }
}
