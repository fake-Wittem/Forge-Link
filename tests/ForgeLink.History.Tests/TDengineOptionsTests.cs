// 文件说明：验证 TDengine 3.x WebSocket 连接参数的本地校验规则。
// 责任边界：不发送网络请求、不验证真实账号或密码。

using ForgeLink.History.TDengine;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖 TDengine 3.x 必填参数、标识符和连接字符串边界。</summary>
public sealed class TDengineOptionsTests
{
    /// <summary>确认完整 WebSocket 配置通过校验并固定使用官方 WebSocket 协议。</summary>
    [Fact]
    public void Validate_ShouldAcceptCompleteConfiguration()
    {
        TDengineOptions options = new("tdengine.example.local", "forgelink", "secret", "forgelink_history");

        Assert.Empty(options.Validate());
        string connectionString = options.BuildConnectionString();
        Assert.Contains("protocol=WebSocket", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("port=6041", connectionString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Native", connectionString, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>确认非法主机、空凭据、非法数据库名、端口和超时均被拒绝。</summary>
    [Fact]
    public void Validate_ShouldRejectInvalidConfiguration()
    {
        TDengineOptions options = new("bad;host", "", "", "bad-name", Port: 0, RequestTimeoutMs: 100);
        Assert.Equal(6, options.Validate().Count);
    }
}
