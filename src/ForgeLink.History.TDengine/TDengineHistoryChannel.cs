// 文件说明：使用官方 TDengine.Connector 实现 TDengine 3.x 历史写入、查询和门禁测试。
// 责任边界：固定使用 WebSocket，不包含即将淘汰的 Native 连接或 SQLite 历史回退。

using System.Data.Common;
using System.Globalization;
using ForgeLink.Domain;
using ForgeLink.History.Abstractions;
using TDengine.Data.Client;

namespace ForgeLink.History.TDengine;

/// <summary>提供 TDengine 3.x WebSocket 历史通道实现。</summary>
public sealed class TDengineHistoryChannel : IHistoryChannel
{
    private static readonly string[] StableNames = ["plc_numeric", "plc_integer", "plc_boolean", "plc_text", "plc_event"];
    private const int WriteBatchSize = 1_000;
    private readonly TDengineOptions _options;

    /// <summary>创建并校验 TDengine 3.x WebSocket 客户端配置。</summary>
    public TDengineHistoryChannel(TDengineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        IReadOnlyList<string> errors = options.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(' ', errors), nameof(options));
    }

    /// <inheritdoc />
    public async Task<HistoryChannelTestResult> TestAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => TestCore(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, $"TDengine 3.x 完整连接测试失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<HistoryWriteResult> WriteAsync(IReadOnlyList<PointValue> values, CancellationToken cancellationToken)
    {
        if (values.Count == 0) return new(0, 0, null);
        try
        {
            await Task.Run(() => WriteCore(values, cancellationToken), cancellationToken).ConfigureAwait(false);
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
            IReadOnlyList<PointValue> values = await Task.Run(() => QueryCore(query, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return new(values, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new([], exception.Message);
        }
    }

    private HistoryChannelTestResult TestCore(CancellationToken cancellationToken)
    {
        using TDengineConnection connection = OpenConnection();
        TDengineCompatibilityResult compatibility = TDengineCompatibility.Evaluate(connection.ServerVersion);
        if (!compatibility.IsSupported)
            return new(false, $"TDengine WebSocket 已连接，但服务端版本不在支持范围内。{compatibility.Message}");
        EnsureSchema(connection, cancellationToken);
        string testSuffix = Guid.NewGuid().ToString("N");
        string tableName = $"plc_numeric_test_{testSuffix}";
        string pointId = Guid.NewGuid().ToString("D");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            Execute(connection, TDengineSqlBuilder.BuildTestInsert(_options.Database, tableName, pointId, timestamp), cancellationToken);
            string sql = $"SELECT point_id FROM {TDengineSqlBuilder.Qualified(_options.Database, tableName)} WHERE ts = {timestamp.ToString(CultureInfo.InvariantCulture)} LIMIT 1";
            using DbCommand command = CreateCommand(connection, sql);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state => TryCancel((DbCommand)state!), command);
            using DbDataReader reader = command.ExecuteReader();
            cancellationToken.ThrowIfCancellationRequested();
            bool found = reader.Read() && string.Equals(TDengineValueConverter.ToText(reader.GetValue(0)), pointId, StringComparison.OrdinalIgnoreCase);
            if (!found) return new(false, "TDengine 测试点写入后未能查询到，历史通道不可启用。");

            return new(true, $"TDengine 3.x WebSocket 连接、认证、Database、建表、写入和查询验证通过。{compatibility.Message}");
        }
        finally
        {
            Execute(connection, $"DROP TABLE IF EXISTS {TDengineSqlBuilder.Qualified(_options.Database, tableName)}", CancellationToken.None);
        }
    }

    private void WriteCore(IReadOnlyList<PointValue> values, CancellationToken cancellationToken)
    {
        using TDengineConnection connection = OpenConnection();
        EnsureSchema(connection, cancellationToken);
        foreach (PointValue[] batch in values.Chunk(WriteBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TDenginePointRow[] rows = batch.Select(TDenginePointMapper.Map).ToArray();
            Execute(connection, TDengineSqlBuilder.BuildInsert(_options.Database, rows), cancellationToken);
        }
    }

    private IReadOnlyList<PointValue> QueryCore(HistoryQuery query, CancellationToken cancellationToken)
    {
        using TDengineConnection connection = OpenConnection();
        EnsureSchema(connection, cancellationToken);
        List<PointValue> values = [];
        foreach (string stableName in StableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (values.Count >= query.MaxPoints) break;
            string sql = TDengineSqlBuilder.BuildQuery(_options.Database, stableName, query.PointIds,
                query.FromUtc, query.ToUtc, query.MaxPoints - values.Count);
            using DbCommand command = CreateCommand(connection, sql);
            using CancellationTokenRegistration registration = cancellationToken.Register(static state => TryCancel((DbCommand)state!), command);
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                values.Add(ToPointValue(reader, stableName));
            }
        }
        return values.OrderBy(static value => value.CollectTimestampUtc).ToArray();
    }

    private TDengineConnection OpenConnection()
    {
        TDengineConnection connection = new(_options.BuildConnectionString());
        try
        {
            connection.Open();
            TDengineCompatibilityResult compatibility = TDengineCompatibility.Evaluate(connection.ServerVersion);
            if (!compatibility.IsSupported) throw new NotSupportedException(compatibility.Message);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void EnsureSchema(TDengineConnection connection, CancellationToken cancellationToken)
    {
        foreach (string sql in TDengineSqlBuilder.BuildStableStatements(_options.Database)) Execute(connection, sql, cancellationToken);
    }

    private void Execute(TDengineConnection connection, string sql, CancellationToken cancellationToken)
    {
        using DbCommand command = CreateCommand(connection, sql);
        using CancellationTokenRegistration registration = cancellationToken.Register(static state => TryCancel((DbCommand)state!), command);
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private DbCommand CreateCommand(TDengineConnection connection, string sql)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = Math.Max(1, (_options.RequestTimeoutMs + 999) / 1_000);
        return command;
    }

    private static void TryCancel(DbCommand command)
    {
        try
        {
            command.Cancel();
        }
        catch (Exception)
        {
            // 连接器不支持取消时仍由命令超时保证调用最终返回。
        }
    }

    private static PointValue ToPointValue(DbDataReader row, string stableName)
    {
        PointDataType dataType = Enum.Parse<PointDataType>(TDengineValueConverter.ToText(row.GetValue(6)), true);
        bool isEvent = string.Equals(stableName, "plc_event", StringComparison.Ordinal);
        return new(
            TDengineValueConverter.ToText(row.GetValue(1)),
            Guid.Parse(TDengineValueConverter.ToText(row.GetValue(2))),
            Guid.Parse(TDengineValueConverter.ToText(row.GetValue(3))),
            isEvent || row.IsDBNull(5) ? null : row.GetValue(5),
            isEvent || row.IsDBNull(4) ? null : row.GetValue(4),
            dataType,
            TDengineValueConverter.ToText(row.GetValue(7)),
            Enum.Parse<DataQuality>(TDengineValueConverter.ToText(row.GetValue(8)), true),
            row.IsDBNull(9) ? null : ParseTimestamp(row.GetValue(9)),
            ParseTimestamp(row.GetValue(10)),
            Convert.ToInt64(row.GetValue(11), CultureInfo.InvariantCulture));
    }

    private static DateTimeOffset ParseTimestamp(object value) => value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        DateTime dateTime => new(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        long milliseconds => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds),
        _ => DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
    };
}
