// 文件说明：定义 TDengine 3.x 历史通道的 WebSocket 连接参数。
// 责任边界：仅包含运行时连接信息，不负责凭据持久化或界面展示。

using System.Data.Common;
using System.Text.RegularExpressions;

namespace ForgeLink.History.TDengine;

/// <summary>表示 TDengine 3.x WebSocket 连接和超时参数。</summary>
public sealed partial record TDengineOptions(
    string Host,
    string Username,
    string Password,
    string Database,
    int Port = 6041,
    bool UseSsl = false,
    bool EnableCompression = true,
    bool AutoReconnect = true,
    int RequestTimeoutMs = 10_000)
{
    /// <summary>校验必要参数和标识符安全边界。</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(Host) || Host.Contains(';', StringComparison.Ordinal))
            errors.Add("TDengine WebSocket 主机不能为空且不得包含分号。");
        if (string.IsNullOrWhiteSpace(Username)) errors.Add("TDengine 用户名不能为空。");
        if (string.IsNullOrWhiteSpace(Password)) errors.Add("TDengine 密码不能为空。");
        if (string.IsNullOrWhiteSpace(Database) || !IdentifierPattern().IsMatch(Database))
            errors.Add("TDengine Database 必须以字母或下划线开头，且只能包含字母、数字和下划线。");
        if (Port is < 1 or > 65535) errors.Add("TDengine WebSocket 端口必须在 1 到 65535 之间。");
        if (RequestTimeoutMs is < 1_000 or > 120_000)
            errors.Add("TDengine 请求超时必须在 1 到 120 秒之间。");
        return errors;
    }

    /// <summary>使用标准连接字符串构造器生成官方连接器所需参数。</summary>
    public string BuildConnectionString()
    {
        DbConnectionStringBuilder builder = new()
        {
            ["protocol"] = "WebSocket",
            ["host"] = Host,
            ["port"] = Port,
            ["username"] = Username,
            ["password"] = Password,
            ["db"] = Database,
            ["useSSL"] = UseSsl,
            ["enableCompression"] = EnableCompression,
            ["autoReconnect"] = AutoReconnect,
            ["timezone"] = "UTC"
        };
        return builder.ConnectionString;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
