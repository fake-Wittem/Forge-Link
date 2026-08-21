// 文件说明：定义 InfluxDB 3 历史通道的版本专用连接参数。
// 责任边界：仅包含运行时连接信息，不负责凭据持久化或界面展示。

namespace ForgeLink.History.InfluxDb3;

/// <summary>表示 InfluxDB 3.x 的连接和超时参数。</summary>
public sealed record InfluxDb3Options(
    string Url,
    string Token,
    string Database,
    bool ValidateTlsCertificate = true,
    int RequestTimeoutMs = 10_000)
{
    /// <summary>校验必要参数和安全边界。</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("InfluxDB 3 URL 必须是有效的 HTTP 或 HTTPS 绝对地址。");
        if (string.IsNullOrWhiteSpace(Token)) errors.Add("InfluxDB 3 Token 不能为空。");
        if (string.IsNullOrWhiteSpace(Database)) errors.Add("InfluxDB 3 Database 不能为空。");
        if (RequestTimeoutMs is < 1_000 or > 120_000) errors.Add("InfluxDB 3 请求超时必须在 1 到 120 秒之间。");
        return errors;
    }
}
