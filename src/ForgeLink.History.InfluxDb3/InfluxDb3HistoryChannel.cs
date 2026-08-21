// 文件说明：使用官方 InfluxDB3.Client 实现 3.x 历史写入、查询和门禁测试。
// 责任边界：仅支持 InfluxDB 3.x，不包含 2.x Flux、Organization 或 Bucket 逻辑。

using System.Globalization;
using ForgeLink.Domain;
using ForgeLink.History.Abstractions;
using InfluxDB3.Client;
using InfluxDB3.Client.Config;
using InfluxDB3.Client.Write;

namespace ForgeLink.History.InfluxDb3;

/// <summary>提供 InfluxDB 3.x 历史通道实现。</summary>
public sealed class InfluxDb3HistoryChannel : IHistoryChannel, IDisposable
{
    private static readonly string[] Measurements = ["plc_numeric", "plc_integer", "plc_boolean", "plc_text", "plc_event"];
    private readonly InfluxDb3Options _options;
    private readonly InfluxDBClient _client;

    /// <summary>创建并校验 InfluxDB 3.x 客户端。</summary>
    public InfluxDb3HistoryChannel(InfluxDb3Options options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        IReadOnlyList<string> errors = options.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(' ', errors), nameof(options));
        TimeSpan timeout = TimeSpan.FromMilliseconds(options.RequestTimeoutMs);
        _client = new InfluxDBClient(new ClientConfig
        {
            Host = options.Url,
            Token = options.Token,
            Database = options.Database,
            QueryTimeout = timeout,
            WriteTimeout = timeout,
            DisableServerCertificateValidation = !options.ValidateTlsCertificate,
            WriteOptions = new WriteOptions { AcceptPartial = false }
        });
    }

    /// <inheritdoc />
    public async Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken)
    {
        try
        {
            string version = await _client.GetServerVersion().WaitAsync(cancellationToken).ConfigureAwait(false)
                ?? "unknown";
            string testId = Guid.NewGuid().ToString("D");
            PointData point = PointData.Measurement("plc_numeric")
                .SetTag("instance_id", "_forgelink_test")
                .SetTag("device_id", Guid.Empty.ToString("D"))
                .SetTag("point_id", testId)
                .SetField("value", 1D)
                .SetField("raw_value", 1D)
                .SetField("quality", DataQuality.Good.ToString())
                .SetField("data_type", PointDataType.Double.ToString())
                .SetField("unit", string.Empty)
                .SetField("collect_timestamp", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"))
                .SetField("sequence_number", 0L)
                .SetTimestamp(DateTimeOffset.UtcNow);
            await _client.WritePointAsync(point: point, cancellationToken: cancellationToken).ConfigureAwait(false);

            Dictionary<string, object> parameters = new() { ["pointId"] = testId };
            bool found = false;
            await foreach (object?[] _ in _client.Query(
                "SELECT point_id FROM plc_numeric WHERE point_id = $pointId ORDER BY time DESC LIMIT 1",
                namedParameters: parameters,
                timeout: TimeSpan.FromMilliseconds(_options.RequestTimeoutMs)).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                found = true;
                break;
            }
            return found
                ? new(true, $"InfluxDB 3 连接、认证、Database、写入及 SQL 查询验证通过；服务版本：{version}。")
                : new(false, "测试点写入后未能查询到，历史通道不可启用。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, $"InfluxDB 3 完整连接测试失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0) return new(0, 0, null);
        try
        {
            PointData[] points = values.Select(InfluxDb3PointMapper.Map).ToArray();
            await _client.WritePointsAsync(points: points, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new(values.Count, 0, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(0, values.Count, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<HistoryQueryResult> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        if (query.PointIds.Count == 0) return new([], null);
        if (query.FromUtc > query.ToUtc) return new([], "历史查询开始时间不得晚于结束时间。");
        if (query.MaxPoints is < 1 or > 100_000) return new([], "历史查询最大点数必须在 1 到 100000 之间。");

        try
        {
            List<PointValue> values = [];
            bool anyMeasurementQueried = false;
            string? lastError = null;
            foreach (string measurement in Measurements)
            {
                if (values.Count >= query.MaxPoints) break;
                Dictionary<string, object> parameters = new()
                {
                    ["fromUtc"] = query.FromUtc.UtcDateTime,
                    ["toUtc"] = query.ToUtc.UtcDateTime
                };
                string[] pointParameters = query.PointIds.Select((id, index) =>
                {
                    string name = $"point{index}";
                    parameters[name] = id.ToString("D");
                    return $"${name}";
                }).ToArray();
                int remaining = query.MaxPoints - values.Count;
                string sql = $"SELECT time,instance_id,device_id,point_id,value,raw_value,data_type,unit,quality,source_timestamp,collect_timestamp,sequence_number,'{measurement}' AS measurement FROM {measurement} WHERE time >= $fromUtc AND time <= $toUtc AND point_id IN ({string.Join(',', pointParameters)}) ORDER BY time ASC LIMIT {remaining.ToString(CultureInfo.InvariantCulture)}";
                try
                {
                    await foreach (object?[] row in _client.Query(sql, namedParameters: parameters,
                        timeout: TimeSpan.FromMilliseconds(_options.RequestTimeoutMs)).WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        values.Add(ToPointValue(row));
                    }
                    anyMeasurementQueried = true;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // 新部署中尚未写入过的 Measurement 可能不存在；继续查询其余类型。
                    lastError = exception.Message;
                }
            }
            return anyMeasurementQueried
                ? new(values.OrderBy(static value => value.CollectTimestampUtc).ToArray(), null)
                : new([], lastError ?? "没有可查询的 InfluxDB 3 Measurement。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], exception.Message);
        }
    }

    private static PointValue ToPointValue(object?[] row)
    {
        PointDataType dataType = Enum.Parse<PointDataType>(Convert.ToString(row[6], CultureInfo.InvariantCulture)!, true);
        DateTimeOffset collectTimestamp = ParseTimestamp(row[10] ?? row[0]);
        string? sourceText = Convert.ToString(row[9], CultureInfo.InvariantCulture);
        DateTimeOffset? sourceTimestamp = string.IsNullOrWhiteSpace(sourceText) ? null : ParseTimestamp(sourceText);
        bool isEvent = string.Equals(Convert.ToString(row[12], CultureInfo.InvariantCulture), "plc_event", StringComparison.Ordinal);
        return new(
            Convert.ToString(row[1], CultureInfo.InvariantCulture) ?? string.Empty,
            Guid.Parse(Convert.ToString(row[2], CultureInfo.InvariantCulture)!),
            Guid.Parse(Convert.ToString(row[3], CultureInfo.InvariantCulture)!),
            isEvent ? null : row[5], isEvent ? null : row[4], dataType,
            Convert.ToString(row[7], CultureInfo.InvariantCulture) ?? string.Empty,
            Enum.Parse<DataQuality>(Convert.ToString(row[8], CultureInfo.InvariantCulture)!, true),
            sourceTimestamp, collectTimestamp,
            Convert.ToInt64(row[11], CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset ParseTimestamp(object? value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
    };

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
