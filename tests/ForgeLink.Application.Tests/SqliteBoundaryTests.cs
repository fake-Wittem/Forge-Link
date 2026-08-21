// 文件说明：验证 SQLite 配置库的实际表结构不包含点位历史。
// 责任边界：仅使用临时数据库执行本地迁移，不访问生产数据目录。

using ForgeLink.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ForgeLink.Application.Tests;

/// <summary>保护 SQLite 仅承担管理配置的架构边界。</summary>
public sealed class SqliteBoundaryTests
{
    /// <summary>确认初始化后的数据库只包含预期配置表。</summary>
    [Fact]
    public async Task InitializeAsync_ShouldNotCreateHistoryTables()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"forgelink-test-{Guid.NewGuid():N}.db");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            SqliteConfigurationRepository repository = new(databasePath);
            await repository.InitializeAsync(cancellationToken);

            List<string> tables = [];
            await using SqliteConnection connection = new($"Data Source={databasePath}");
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));

            Assert.Equal(["devices", "points", "schema_version"], tables);
            Assert.DoesNotContain(tables, static name => name.Contains("history", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            // SQLite 默认连接池会继续持有临时文件，清池后才能可靠清理测试产物。
            SqliteConnection.ClearAllPools();
            DeleteSqliteFile(databasePath);
            DeleteSqliteFile(databasePath + "-shm");
            DeleteSqliteFile(databasePath + "-wal");
        }
    }

    /// <summary>确认旧版配置库会保留数据并补齐 Modbus 与寄存器顺序字段。</summary>
    [Fact]
    public async Task InitializeAsync_ShouldMigrateVersion2Configuration()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"forgelink-v2-{Guid.NewGuid():N}.db");
        CancellationToken token = TestContext.Current.CancellationToken;
        try
        {
            await using (SqliteConnection connection = new($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync(token);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE schema_version(version INTEGER NOT NULL);
                    INSERT INTO schema_version VALUES(2);
                    CREATE TABLE devices(id TEXT PRIMARY KEY,name TEXT NOT NULL,protocol TEXT NOT NULL,host TEXT NOT NULL,port INTEGER NOT NULL,is_enabled INTEGER NOT NULL,default_scan_interval_ms INTEGER NOT NULL);
                    CREATE TABLE points(id TEXT PRIMARY KEY,device_id TEXT NOT NULL,code TEXT NOT NULL,name TEXT NOT NULL,address TEXT NOT NULL,data_type INTEGER NOT NULL,scale REAL NOT NULL,offset REAL NOT NULL,unit TEXT NOT NULL,scan_interval_ms INTEGER NOT NULL,deadband REAL NOT NULL,history_mode INTEGER NOT NULL,is_enabled INTEGER NOT NULL,allow_write INTEGER NOT NULL,FOREIGN KEY(device_id) REFERENCES devices(id));
                    INSERT INTO devices VALUES('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','旧设备','Modbus TCP','127.0.0.1',1502,1,1000);
                    INSERT INTO points VALUES('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb','aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','OLD_POINT','旧点位','HR:0',2,1,0,'',1000,0,0,1,0);
                    """;
                await command.ExecuteNonQueryAsync(token);
            }

            SqliteConfigurationRepository repository = new(databasePath);
            await repository.InitializeAsync(token);
            ForgeLink.Domain.DeviceDefinition device = (await repository.GetDevicesAsync(token)).Single(item => item.Name == "旧设备");
            ForgeLink.Domain.PointDefinition point = (await repository.GetPointsAsync(token)).Single(item => item.Code == "OLD_POINT");
            Assert.Equal((byte)1, device.UnitId);
            Assert.Equal(3000, device.ConnectionTimeoutMs);
            Assert.Equal(ForgeLink.Domain.RegisterByteOrder.BigEndian, point.ByteOrder);
            Assert.Equal(ForgeLink.Domain.RegisterWordOrder.HighWordFirst, point.WordOrder);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteSqliteFile(databasePath);
            DeleteSqliteFile(databasePath + "-shm");
            DeleteSqliteFile(databasePath + "-wal");
        }
    }

    /// <summary>删除本测试创建的单个临时数据库文件。</summary>
    private static void DeleteSqliteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
