// 文件说明：实现点位配置的可重复 CSV 导入导出格式。
// 责任边界：只处理文本编解码和字段类型，不访问数据库或覆盖凭据。

using System.Globalization;
using System.Text;
using ForgeLink.Domain;

namespace ForgeLink.Application;

/// <summary>表示点位 CSV 解析结果及逐行错误。</summary>
public sealed record PointCsvParseResult(IReadOnlyList<PointDefinition> Points, IReadOnlyList<string> Errors);

/// <summary>提供支持引号、逗号和换行的点位 CSV 编解码。</summary>
public sealed class PointCsvCodec
{
    private static readonly string[] Headers =
    [
        "Id", "DeviceId", "Code", "Name", "Address", "DataType", "Scale", "Offset", "Unit",
        "ScanIntervalMs", "Deadband", "HistoryMode", "IsEnabled", "AllowWrite", "ByteOrder", "WordOrder",
        "StringLength"
    ];

    /// <summary>把点位配置导出为采用固定英文列名和不变区域格式的 CSV。</summary>
    public string Export(IReadOnlyList<PointDefinition> points)
    {
        StringBuilder builder = new();
        builder.AppendLine(string.Join(',', Headers));
        foreach (PointDefinition point in points)
        {
            string[] values =
            [
                point.Id.ToString("D"),
                point.DeviceId.ToString("D"),
                point.Code,
                point.Name,
                point.Address,
                point.DataType.ToString(),
                point.Scale.ToString("R", CultureInfo.InvariantCulture),
                point.Offset.ToString("R", CultureInfo.InvariantCulture),
                point.Unit,
                point.ScanIntervalMs.ToString(CultureInfo.InvariantCulture),
                point.Deadband.ToString("R", CultureInfo.InvariantCulture),
                point.HistoryMode.ToString(),
                point.IsEnabled.ToString(CultureInfo.InvariantCulture),
                point.AllowWrite.ToString(CultureInfo.InvariantCulture),
                point.ByteOrder.ToString(),
                point.WordOrder.ToString(),
                point.StringLength.ToString(CultureInfo.InvariantCulture)
            ];
            builder.AppendLine(string.Join(',', values.Select(Escape)));
        }
        return builder.ToString();
    }

    /// <summary>解析点位 CSV，并收集所有可定位到行号的格式错误。</summary>
    public PointCsvParseResult Parse(string csv)
    {
        IReadOnlyList<IReadOnlyList<string>> rows = ParseRows(csv);
        if (rows.Count == 0) return new([], ["CSV 文件为空。"]);
        if (!Headers.SequenceEqual(rows[0], StringComparer.OrdinalIgnoreCase))
            return new([], [$"CSV 表头必须是：{string.Join(',', Headers)}"]);

        List<PointDefinition> points = [];
        List<string> errors = [];
        for (int index = 1; index < rows.Count; index++)
        {
            IReadOnlyList<string> row = rows[index];
            if (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])) continue;
            if (row.Count != Headers.Length)
            {
                errors.Add($"第 {index + 1} 行应包含 {Headers.Length} 列，实际为 {row.Count} 列。");
                continue;
            }

            if (TryCreatePoint(row, out PointDefinition? point, out string? error)) points.Add(point);
            else errors.Add($"第 {index + 1} 行：{error}");
        }
        return new(points, errors);
    }

    /// <summary>把单行字段转换为强类型点位配置。</summary>
    private static bool TryCreatePoint(IReadOnlyList<string> row, out PointDefinition point, out string? error)
    {
        point = default!;
        error = null;
        if (!Guid.TryParse(row[0], out Guid id) || id == Guid.Empty) error = "Id 必须是非空 GUID。";
        else if (!Guid.TryParse(row[1], out Guid deviceId) || deviceId == Guid.Empty) error = "DeviceId 必须是非空 GUID。";
        else if (!Enum.TryParse(row[5], true, out PointDataType dataType)) error = "DataType 无效。";
        else if (!double.TryParse(row[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double scale)) error = "Scale 必须是数值。";
        else if (!double.TryParse(row[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double offset)) error = "Offset 必须是数值。";
        else if (!int.TryParse(row[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval)) error = "ScanIntervalMs 必须是整数。";
        else if (!double.TryParse(row[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double deadband)) error = "Deadband 必须是数值。";
        else if (!Enum.TryParse(row[11], true, out HistoryRecordMode historyMode)) error = "HistoryMode 无效。";
        else if (!bool.TryParse(row[12], out bool enabled)) error = "IsEnabled 必须是 True 或 False。";
        else if (!bool.TryParse(row[13], out bool allowWrite)) error = "AllowWrite 必须是 True 或 False。";
        else if (!Enum.TryParse(row[14], true, out RegisterByteOrder byteOrder)) error = "ByteOrder 无效。";
        else if (!Enum.TryParse(row[15], true, out RegisterWordOrder wordOrder)) error = "WordOrder 无效。";
        else if (!int.TryParse(row[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stringLength)) error = "StringLength 必须是整数。";
        else
        {
            point = new(id, deviceId, row[2], row[3], row[4], dataType, scale, offset, row[8], interval,
                deadband, historyMode, enabled, allowWrite, byteOrder, wordOrder, stringLength);
            IReadOnlyList<string> validationErrors = point.Validate();
            if (validationErrors.Count > 0) error = string.Join(' ', validationErrors);
        }
        return error is null;
    }

    /// <summary>按 RFC 4180 的引号规则解析 CSV 字段和换行。</summary>
    private static IReadOnlyList<IReadOnlyList<string>> ParseRows(string csv)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        StringBuilder field = new();
        bool quoted = false;
        for (int index = 0; index < csv.Length; index++)
        {
            char current = csv[index];
            if (current == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (current == ',' && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((current == '\r' || current == '\n') && !quoted)
            {
                if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else field.Append(current);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>仅在字段需要时添加引号并转义内部引号。</summary>
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
        : value;
}
