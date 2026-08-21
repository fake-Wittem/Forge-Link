// 文件说明：验证 InfluxDB 3 连接参数的本地校验规则。
// 责任边界：不发送网络请求、不验证真实 Token。

using ForgeLink.History.InfluxDb3;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖 InfluxDB 3 必填参数与 URL 边界。</summary>
public sealed class InfluxDb3OptionsTests
{
    /// <summary>确认完整 HTTPS 配置通过本地校验。</summary>
    [Fact]
    public void Validate_ShouldAcceptCompleteConfiguration()
    {
        InfluxDb3Options options = new("https://influx.example.local", "secret", "forgelink");
        Assert.Empty(options.Validate());
    }

    /// <summary>确认不支持的协议和空凭据会被拒绝。</summary>
    [Fact]
    public void Validate_ShouldRejectInvalidConfiguration()
    {
        InfluxDb3Options options = new("ftp://influx.example.local", "", "", RequestTimeoutMs: 100);
        Assert.Equal(4, options.Validate().Count);
    }
}
