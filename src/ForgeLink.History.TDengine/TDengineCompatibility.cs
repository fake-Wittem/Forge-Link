// 文件说明：集中判断 TDengine 3.x 服务端版本的支持级别。
// 责任边界：只依据官方 WebSocket 兼容基线分类，不代替目标服务器读写验收。

using System.Text.RegularExpressions;

namespace ForgeLink.History.TDengine;

/// <summary>表示 TDengine 服务端版本兼容性判断结果。</summary>
public sealed record TDengineCompatibilityResult(
    bool IsSupported,
    bool IsOfficiallyGuaranteed,
    Version? ServerVersion,
    string Message);

/// <summary>定义 ForgeLink 对 TDengine 3.x 的版本兼容策略。</summary>
public static partial class TDengineCompatibility
{
    /// <summary>ForgeLink 尽力兼容的最低 TDengine 3.x 版本。</summary>
    public static Version MinimumBestEffortVersion { get; } = new(3, 0, 0, 0);

    /// <summary>官方对 C# WebSocket 连接提供兼容保证的最低服务端版本。</summary>
    public static Version MinimumGuaranteedVersion { get; } = new(3, 3, 6, 0);

    /// <summary>解析服务端版本并给出拒绝、尽力兼容或官方保证级别。</summary>
    public static TDengineCompatibilityResult Evaluate(string? serverVersion)
    {
        Match match = VersionPattern().Match(serverVersion ?? string.Empty);
        if (!match.Success || !Version.TryParse(match.Value, out Version? version))
            return new(false, false, null, $"无法识别 TDengine 服务端版本：{serverVersion ?? "<empty>"}。");
        if (version < MinimumBestEffortVersion)
            return new(false, false, version, $"TDengine {version} 低于 ForgeLink 支持的 3.0.0.0 最低边界。");
        if (version < MinimumGuaranteedVersion)
            return new(true, false, version,
                $"TDengine {version} 属于 3.0.0.0–3.3.5.x 尽力兼容范围，启用前必须完成目标版本建表、批量写入和查询验收。");
        return new(true, true, version,
            $"TDengine {version} 位于官方 C# WebSocket 兼容保证范围内。");
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
