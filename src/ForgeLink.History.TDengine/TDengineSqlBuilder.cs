// 文件说明：集中生成 TDengine 超级表、批量写入和历史查询 SQL。
// 责任边界：只接受已校验数据库名和内部生成表名，并统一转义所有字符串字面量。

using System.Globalization;
using System.Text;

namespace ForgeLink.History.TDengine;

/// <summary>为 TDengine 3.x 构建受控 SQL。</summary>
public static class TDengineSqlBuilder
{
    /// <summary>返回五类点位值超级表的幂等建表语句。</summary>
    public static IReadOnlyList<string> BuildStableStatements(string database) =>
    [
        BuildStable(database, "plc_numeric", "DOUBLE"),
        BuildStable(database, "plc_integer", "BIGINT"),
        BuildStable(database, "plc_boolean", "BOOL"),
        BuildStable(database, "plc_text", "NCHAR(1024)"),
        BuildStable(database, "plc_event", "NCHAR(1)")
    ];

    /// <summary>构建可同时创建子表并写入多点数据的批量 INSERT。</summary>
    public static string BuildInsert(string database, IReadOnlyList<TDenginePointRow> rows)
    {
        if (rows.Count == 0) throw new ArgumentException("TDengine 批量写入不能为空。", nameof(rows));
        StringBuilder sql = new("INSERT INTO ");
        foreach (IGrouping<string, TDenginePointRow> tableRows in rows.GroupBy(static row => row.ChildTableName, StringComparer.Ordinal))
        {
            TDenginePointRow first = tableRows.First();
            sql.Append(Qualified(database, first.ChildTableName))
                .Append(" USING ").Append(Qualified(database, first.StableName))
                .Append(" TAGS(").Append(Literal(first.InstanceId)).Append(',')
                .Append(Literal(first.DeviceId)).Append(',').Append(Literal(first.PointId))
                .Append(") VALUES");
            foreach (TDenginePointRow row in tableRows)
            {
                sql.Append('(').Append(row.TimestampMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(ValueLiteral(row.Value)).Append(',').Append(ValueLiteral(row.RawValue)).Append(',')
                    .Append(Literal(row.DataType)).Append(',').Append(Literal(row.Unit)).Append(',')
                    .Append(Literal(row.Quality)).Append(',').Append(NullableNumber(row.SourceTimestampMilliseconds)).Append(',')
                    .Append(row.CollectTimestampMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.SequenceNumber.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
            sql.Append(' ');
        }
        return sql.ToString().TrimEnd();
    }

    /// <summary>按超级表、时间范围和点位集合构建有上限的查询。</summary>
    public static string BuildQuery(string database, string stableName, IReadOnlyList<Guid> pointIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit)
    {
        string ids = string.Join(',', pointIds.Select(static id => Literal(id.ToString("D"))));
        return $"SELECT ts,instance_id,device_id,point_id,value,raw_value,data_type,unit,quality,source_timestamp,collect_timestamp,sequence_number FROM {Qualified(database, stableName)} WHERE ts >= {fromUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)} AND ts <= {toUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)} AND point_id IN ({ids}) ORDER BY ts ASC LIMIT {limit.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>构建连接验证所需的唯一测试子表。</summary>
    public static string BuildTestInsert(string database, string tableName, string pointId, long timestamp) =>
        $"INSERT INTO {Qualified(database, tableName)} USING {Qualified(database, "plc_numeric")} TAGS('_forgelink_test','00000000-0000-0000-0000-000000000000',{Literal(pointId)}) VALUES({timestamp.ToString(CultureInfo.InvariantCulture)},1,1,'Double','', 'Good',NULL,{timestamp.ToString(CultureInfo.InvariantCulture)},0)";

    /// <summary>引用数据库和表标识符。</summary>
    public static string Qualified(string database, string table) => $"`{database}`.`{table}`";

    private static string BuildStable(string database, string name, string valueType) =>
        $"CREATE STABLE IF NOT EXISTS {Qualified(database, name)} (ts TIMESTAMP, value {valueType}, raw_value {valueType}, data_type BINARY(16), unit NCHAR(64), quality BINARY(16), source_timestamp TIMESTAMP, collect_timestamp TIMESTAMP, sequence_number BIGINT) TAGS(instance_id NCHAR(128), device_id BINARY(36), point_id BINARY(36))";

    private static string NullableNumber(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "NULL";

    private static string ValueLiteral(object? value) => value switch
    {
        null => "NULL",
        bool boolean => boolean ? "true" : "false",
        string text => Literal(text),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Literal(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
    };

    /// <summary>按 TDengine SQL 的反斜杠规则转义字符串字面量。</summary>
    private static string Literal(string value)
    {
        StringBuilder escaped = new(value.Length + 8);
        foreach (char character in value)
        {
            escaped.Append(character switch
            {
                '\\' => "\\\\",
                '\'' => "\\'",
                '\"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => "\uFFFD",
                _ => character.ToString()
            });
        }
        return $"'{escaped}'";
    }
}
