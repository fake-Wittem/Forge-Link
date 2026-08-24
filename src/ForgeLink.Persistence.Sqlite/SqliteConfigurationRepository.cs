// 文件说明：实现 SQLite 配置库迁移及设备、点位配置访问。
// 责任边界：SQLite 仅保存管理配置，严禁写入点位采集历史。

using System.Globalization;
using ForgeLink.Application;
using ForgeLink.Domain;
using Microsoft.Data.Sqlite;

namespace ForgeLink.Persistence.Sqlite;

/// <summary>通过参数化 SQL 持久化 ForgeLink 管理配置。</summary>
public sealed class SqliteConfigurationRepository : IConfigurationRepository
{
    private readonly string _databasePath;
    private readonly ISecretProtector _secretProtector;
    private readonly string _connectionString;

    /// <summary>创建仅用于不访问敏感配置的仓储实例。</summary>
    public SqliteConfigurationRepository(string databasePath) : this(databasePath, new UnavailableSecretProtector()) { }

    /// <summary>创建使用指定凭据保护器的配置仓储。</summary>
    public SqliteConfigurationRepository(string databasePath, ISecretProtector secretProtector)
    {
        _databasePath = databasePath;
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
            INSERT INTO schema_version(version) SELECT 5 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
            CREATE TABLE IF NOT EXISTS devices (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, protocol TEXT NOT NULL, host TEXT NOT NULL,
                port INTEGER NOT NULL, is_enabled INTEGER NOT NULL, default_scan_interval_ms INTEGER NOT NULL,
                unit_id INTEGER NOT NULL DEFAULT 1, connection_timeout_ms INTEGER NOT NULL DEFAULT 3000,
                read_timeout_ms INTEGER NOT NULL DEFAULT 2000);
            CREATE TABLE IF NOT EXISTS points (
                id TEXT PRIMARY KEY, device_id TEXT NOT NULL, code TEXT NOT NULL, name TEXT NOT NULL,
                address TEXT NOT NULL, data_type INTEGER NOT NULL, scale REAL NOT NULL, offset REAL NOT NULL,
                unit TEXT NOT NULL, scan_interval_ms INTEGER NOT NULL, deadband REAL NOT NULL,
                history_mode INTEGER NOT NULL, is_enabled INTEGER NOT NULL, allow_write INTEGER NOT NULL,
                byte_order INTEGER NOT NULL DEFAULT 0, word_order INTEGER NOT NULL DEFAULT 0,
                string_length INTEGER NOT NULL DEFAULT 0, group_name TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(device_id) REFERENCES devices(id));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_points_device_code ON points(device_id, code);
            CREATE TABLE IF NOT EXISTS tdengine_connection (
                singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
                host TEXT NOT NULL, port INTEGER NOT NULL, username TEXT NOT NULL,
                password_protected BLOB NOT NULL, database_name TEXT NOT NULL,
                use_ssl INTEGER NOT NULL, enable_compression INTEGER NOT NULL,
                auto_reconnect INTEGER NOT NULL, request_timeout_ms INTEGER NOT NULL,
                test_passed INTEGER NOT NULL DEFAULT 0, is_enabled INTEGER NOT NULL DEFAULT 0);
            """;
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await ApplyVersion3MigrationAsync(connection, cancellationToken).ConfigureAwait(false);
        await ApplyVersion4MigrationAsync(connection, cancellationToken).ConfigureAwait(false);
        await ApplyVersion5MigrationAsync(connection, cancellationToken).ConfigureAwait(false);
        await SeedDemoConfigurationAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceDefinition>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        List<DeviceDefinition> devices = [];
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,protocol,host,port,is_enabled,default_scan_interval_ms,unit_id,connection_timeout_ms,read_timeout_ms FROM devices ORDER BY name";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            devices.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetBoolean(5), reader.GetInt32(6), checked((byte)reader.GetInt32(7)),
                reader.GetInt32(8), reader.GetInt32(9)));
        }
        return devices;
    }

    /// <inheritdoc />
    public async Task SaveDeviceAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> errors = device.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(' ', errors), nameof(device));
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices(id,name,protocol,host,port,is_enabled,default_scan_interval_ms,unit_id,connection_timeout_ms,read_timeout_ms)
            VALUES($id,$name,$protocol,$host,$port,$enabled,$interval,$unitId,$connectionTimeout,$readTimeout)
            ON CONFLICT(id) DO UPDATE SET name=$name,protocol=$protocol,host=$host,port=$port,is_enabled=$enabled,default_scan_interval_ms=$interval,unit_id=$unitId,connection_timeout_ms=$connectionTimeout,read_timeout_ms=$readTimeout
            """;
        command.Parameters.AddWithValue("$id", device.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$protocol", device.Protocol);
        command.Parameters.AddWithValue("$host", device.Host);
        command.Parameters.AddWithValue("$port", device.Port);
        command.Parameters.AddWithValue("$enabled", device.IsEnabled);
        command.Parameters.AddWithValue("$interval", device.DefaultScanIntervalMs);
        command.Parameters.AddWithValue("$unitId", device.UnitId);
        command.Parameters.AddWithValue("$connectionTimeout", device.ConnectionTimeoutMs);
        command.Parameters.AddWithValue("$readTimeout", device.ReadTimeoutMs);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM devices
            WHERE id = $id
              AND NOT EXISTS (SELECT 1 FROM points WHERE device_id = $id)
            """;
        command.Parameters.AddWithValue("$id", deviceId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PointDefinition>> GetPointsAsync(CancellationToken cancellationToken)
    {
        List<PointDefinition> points = [];
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,device_id,code,name,address,data_type,scale,offset,unit,scan_interval_ms,deadband,history_mode,is_enabled,allow_write,byte_order,word_order,string_length,group_name FROM points ORDER BY group_name,code";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            points.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), (PointDataType)reader.GetInt32(5), reader.GetDouble(6), reader.GetDouble(7), reader.GetString(8),
                reader.GetInt32(9), reader.GetDouble(10), (HistoryRecordMode)reader.GetInt32(11), reader.GetBoolean(12), reader.GetBoolean(13),
                (RegisterByteOrder)reader.GetInt32(14), (RegisterWordOrder)reader.GetInt32(15), reader.GetInt32(16), reader.GetString(17)));
        }
        return points;
    }

    /// <inheritdoc />
    public async Task SavePointAsync(PointDefinition point, CancellationToken cancellationToken)
        => await SavePointsAsync([point], cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SavePointsAsync(IReadOnlyList<PointDefinition> points, CancellationToken cancellationToken)
    {
        foreach (PointDefinition point in points)
        {
            IReadOnlyList<string> errors = point.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join(' ', errors), nameof(points));
        }
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (PointDefinition point in points)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO points(id,device_id,code,name,address,data_type,scale,offset,unit,scan_interval_ms,deadband,history_mode,is_enabled,allow_write,byte_order,word_order,string_length,group_name)
                VALUES($id,$device,$code,$name,$address,$type,$scale,$offset,$unit,$interval,$deadband,$history,$enabled,$write,$byteOrder,$wordOrder,$stringLength,$groupName)
                ON CONFLICT(id) DO UPDATE SET device_id=$device,code=$code,name=$name,address=$address,data_type=$type,scale=$scale,offset=$offset,unit=$unit,scan_interval_ms=$interval,deadband=$deadband,history_mode=$history,is_enabled=$enabled,allow_write=$write,byte_order=$byteOrder,word_order=$wordOrder,string_length=$stringLength,group_name=$groupName
                """;
            command.Parameters.AddWithValue("$id", point.Id.ToString("D"));
            command.Parameters.AddWithValue("$device", point.DeviceId.ToString("D"));
            command.Parameters.AddWithValue("$code", point.Code);
            command.Parameters.AddWithValue("$name", point.Name);
            command.Parameters.AddWithValue("$address", point.Address);
            command.Parameters.AddWithValue("$type", (int)point.DataType);
            command.Parameters.AddWithValue("$scale", point.Scale);
            command.Parameters.AddWithValue("$offset", point.Offset);
            command.Parameters.AddWithValue("$unit", point.Unit);
            command.Parameters.AddWithValue("$interval", point.ScanIntervalMs);
            command.Parameters.AddWithValue("$deadband", point.Deadband);
            command.Parameters.AddWithValue("$history", (int)point.HistoryMode);
            command.Parameters.AddWithValue("$enabled", point.IsEnabled);
            command.Parameters.AddWithValue("$write", point.AllowWrite);
            command.Parameters.AddWithValue("$byteOrder", (int)point.ByteOrder);
            command.Parameters.AddWithValue("$wordOrder", (int)point.WordOrder);
            command.Parameters.AddWithValue("$stringLength", point.StringLength);
            command.Parameters.AddWithValue("$groupName", point.GroupName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeletePointAsync(Guid pointId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM points WHERE id = $id";
        command.Parameters.AddWithValue("$id", pointId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<TDengineConnectionConfiguration?> GetTDengineConfigurationAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT host,port,username,password_protected,database_name,use_ssl,enable_compression,auto_reconnect,request_timeout_ms,test_passed,is_enabled FROM tdengine_connection WHERE singleton_id=1";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        byte[] protectedPassword = (byte[])reader.GetValue(3);
        return new(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2), _secretProtector.Unprotect(protectedPassword),
            reader.GetString(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetInt32(8),
            reader.GetBoolean(9), reader.GetBoolean(10));
    }

    /// <inheritdoc />
    public async Task SaveTDengineConfigurationAsync(TDengineConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        byte[] protectedPassword = _secretProtector.Protect(configuration.Password);
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tdengine_connection(singleton_id,host,port,username,password_protected,database_name,use_ssl,enable_compression,auto_reconnect,request_timeout_ms,test_passed,is_enabled)
            VALUES(1,$host,$port,$username,$password,$database,$ssl,$compression,$reconnect,$timeout,0,0)
            ON CONFLICT(singleton_id) DO UPDATE SET host=$host,port=$port,username=$username,password_protected=$password,database_name=$database,use_ssl=$ssl,enable_compression=$compression,auto_reconnect=$reconnect,request_timeout_ms=$timeout,test_passed=0,is_enabled=0
            """;
        command.Parameters.AddWithValue("$host", configuration.Host);
        command.Parameters.AddWithValue("$port", configuration.Port);
        command.Parameters.AddWithValue("$username", configuration.Username);
        command.Parameters.Add("$password", SqliteType.Blob).Value = protectedPassword;
        command.Parameters.AddWithValue("$database", configuration.Database);
        command.Parameters.AddWithValue("$ssl", configuration.UseSsl);
        command.Parameters.AddWithValue("$compression", configuration.EnableCompression);
        command.Parameters.AddWithValue("$reconnect", configuration.AutoReconnect);
        command.Parameters.AddWithValue("$timeout", configuration.RequestTimeoutMs);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetHistoryGateStateAsync(bool testPassed, bool isEnabled, CancellationToken cancellationToken)
    {
        if (isEnabled && !testPassed) throw new ArgumentException("未通过完整测试时不能持久化启用状态。", nameof(isEnabled));
        await using SqliteConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE tdengine_connection SET test_passed=$tested,is_enabled=$enabled WHERE singleton_id=1";
        command.Parameters.AddWithValue("$tested", testPassed);
        command.Parameters.AddWithValue("$enabled", isEnabled);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("TDengine 连接配置尚未保存。");
    }

    /// <summary>打开已启用外键约束的数据库连接。</summary>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>为空数据库写入脱敏的模拟设备和演示点位。</summary>
    private static async Task SeedDemoConfigurationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        Guid deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO devices(id,name,protocol,host,port,is_enabled,default_scan_interval_ms,unit_id,connection_timeout_ms,read_timeout_ms)
            VALUES($device,'模拟产线 PLC','Simulation','127.0.0.1',502,1,1000,1,3000,2000);
            INSERT OR IGNORE INTO points(id,device_id,code,name,address,data_type,scale,offset,unit,scan_interval_ms,deadband,history_mode,is_enabled,allow_write,byte_order,word_order,string_length,group_name)
            VALUES('21111111-1111-1111-1111-111111111111',$device,'TEMP_01','入口温度','D100',6,0.1,0,'°C',1000,0.2,5,1,0,0,0,0,'工艺参数');
            INSERT OR IGNORE INTO points(id,device_id,code,name,address,data_type,scale,offset,unit,scan_interval_ms,deadband,history_mode,is_enabled,allow_write,byte_order,word_order,string_length,group_name)
            VALUES('31111111-1111-1111-1111-111111111111',$device,'PRESSURE_01','管路压力','D102',6,0.01,0,'MPa',1000,0.01,5,1,0,0,0,0,'工艺参数');
            INSERT OR IGNORE INTO points(id,device_id,code,name,address,data_type,scale,offset,unit,scan_interval_ms,deadband,history_mode,is_enabled,allow_write,byte_order,word_order,string_length,group_name)
            VALUES('41111111-1111-1111-1111-111111111111',$device,'RUNNING_01','运行状态','M0',0,1,0,'',1000,0,2,1,0,0,0,0,'运行状态');
            """;
        command.Parameters.AddWithValue("$device", deviceId.ToString("D", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>为旧版配置库增补 Modbus 设备参数和寄存器顺序字段。</summary>
    private static async Task ApplyVersion3MigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, "devices", "unit_id", "INTEGER NOT NULL DEFAULT 1", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "devices", "connection_timeout_ms", "INTEGER NOT NULL DEFAULT 3000", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "devices", "read_timeout_ms", "INTEGER NOT NULL DEFAULT 2000", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "points", "byte_order", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "points", "word_order", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await AddColumnIfMissingAsync(connection, "points", "string_length", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE schema_version SET version = 3 WHERE version < 3";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把配置库版本提升到包含 TDengine 加密连接配置的版本 4。</summary>
    private static async Task ApplyVersion4MigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE schema_version SET version = 4 WHERE version < 4";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>为点位配置增加可选的自定义分组名称。</summary>
    private static async Task ApplyVersion5MigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, "points", "group_name", "TEXT NOT NULL DEFAULT ''", cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE schema_version SET version = 5 WHERE version < 5";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>通过表结构元数据检查，幂等地增加单个迁移字段。</summary>
    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string table,
        string column,
        string declaration,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        await using SqliteDataReader reader = await check.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        bool exists = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            exists |= string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase);
        await reader.DisposeAsync().ConfigureAwait(false);
        if (exists) return;

        await using SqliteCommand alter = connection.CreateCommand();
        // 表名、列名和声明均由本类中的固定迁移常量提供，不接收外部输入。
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration}";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>阻止没有显式安全实现的调用方意外读写密码。</summary>
    private sealed class UnavailableSecretProtector : ISecretProtector
    {
        public byte[] Protect(string plaintext) => throw new InvalidOperationException("必须配置凭据保护器后才能保存 TDengine 密码。");
        public string Unprotect(byte[] protectedPayload) => throw new InvalidOperationException("必须配置凭据保护器后才能读取 TDengine 密码。");
    }
}
