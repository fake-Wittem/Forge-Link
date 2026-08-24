// 文件说明：验证 TDengine 建表、批量写入和查询 SQL 的安全生成规则。
// 责任边界：只检查 SQL 文本，不连接 TDengine 服务。

using ForgeLink.History.TDengine;
using Xunit;

namespace ForgeLink.History.Tests;

/// <summary>覆盖 TDengine SQL 中的 schema、转义和查询上限。</summary>
public sealed class TDengineSqlBuilderTests
{
    /// <summary>确认字符串字段和标签按 TDengine 规则转义引号、反斜杠和控制字符。</summary>
    [Fact]
    public void BuildInsert_ShouldEscapeStringLiterals()
    {
        TDenginePointRow row = new("plc_text", "plc_text_0123456789abcdef", 1, "O'Hare", "A\\B\nC",
            "String", "m's", "Good", null, 1, 2, "plant'01", Guid.Empty.ToString("D"), Guid.Empty.ToString("D"));

        string sql = TDengineSqlBuilder.BuildInsert("forgelink", [row]);

        Assert.Contains("'O\\'Hare'", sql, StringComparison.Ordinal);
        Assert.Contains("'A\\\\B\\nC'", sql, StringComparison.Ordinal);
        Assert.Contains("'m\\'s'", sql, StringComparison.Ordinal);
        Assert.Contains("'plant\\'01'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("O''Hare", sql, StringComparison.Ordinal);
        Assert.Contains("USING `forgelink`.`plc_text`", sql, StringComparison.Ordinal);
    }

    /// <summary>确认漏入历史边界的非法控制字符会被替换，不再破坏整条 TDengine SQL。</summary>
    [Fact]
    public void BuildInsert_ShouldReplaceUnsupportedControlCharacters()
    {
        TDenginePointRow row = new("plc_text", "plc_text_control", 1, "A\0B", "\u0001C",
            "String", "", "Good", null, 1, 1, "plant", Guid.Empty.ToString("D"), Guid.Empty.ToString("D"));

        string sql = TDengineSqlBuilder.BuildInsert("forgelink", [row]);

        Assert.DoesNotContain('\0', sql);
        Assert.DoesNotContain('\u0001', sql);
        Assert.Contains("'A�B'", sql, StringComparison.Ordinal);
        Assert.Contains("'�C'", sql, StringComparison.Ordinal);
    }

    /// <summary>确认同一点位的多行被合并为一个子表写入段。</summary>
    [Fact]
    public void BuildInsert_ShouldGroupRowsByChildTable()
    {
        TDenginePointRow first = new("plc_numeric", "plc_numeric_point", 1, 1D, 1D,
            "Double", "", "Good", null, 1, 1, "plant", Guid.Empty.ToString("D"), Guid.Empty.ToString("D"));
        TDenginePointRow second = first with { TimestampMilliseconds = 2, Value = 2D, RawValue = 2D };

        string sql = TDengineSqlBuilder.BuildInsert("forgelink", [first, second]);

        Assert.Equal(1, CountOccurrences(sql, "USING `forgelink`.`plc_numeric`"));
        Assert.Contains("VALUES(1,1,1", sql, StringComparison.Ordinal);
        Assert.Contains(")(2,2,2", sql, StringComparison.Ordinal);
    }

    /// <summary>确认五类超级表均采用幂等建表并保留点位标签。</summary>
    [Fact]
    public void BuildStableStatements_ShouldCreateFiveTypedStables()
    {
        IReadOnlyList<string> statements = TDengineSqlBuilder.BuildStableStatements("forgelink");

        Assert.Equal(5, statements.Count);
        Assert.All(statements, static sql => Assert.Contains("CREATE STABLE IF NOT EXISTS", sql, StringComparison.Ordinal));
        Assert.All(statements, static sql => Assert.Contains("point_id BINARY(36)", sql, StringComparison.Ordinal));
    }

    /// <summary>确认历史查询限定时间、点位集合和最大返回数。</summary>
    [Fact]
    public void BuildQuery_ShouldApplyTimePointAndLimitFilters()
    {
        Guid pointId = Guid.NewGuid();
        string sql = TDengineSqlBuilder.BuildQuery("forgelink", "plc_numeric", [pointId],
            DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(2000), 25);

        Assert.Contains("ts >= 1000 AND ts <= 2000", sql, StringComparison.Ordinal);
        Assert.Contains(pointId.ToString("D"), sql, StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 25", sql, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
