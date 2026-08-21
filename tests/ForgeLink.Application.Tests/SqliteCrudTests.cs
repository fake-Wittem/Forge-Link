// 文件说明：验证 SQLite 配置删除保护和批量点位事务。
// 责任边界：只操作每个测试独立创建的临时数据库。

using ForgeLink.Domain;
using ForgeLink.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ForgeLink.Application.Tests;

/// <summary>覆盖设备点位 CRUD 的数据完整性边界。</summary>
public sealed class SqliteCrudTests
{
    /// <summary>确认有关联点位时拒绝删除设备，删除点位后允许删除。</summary>
    [Fact]
    public async Task DeleteDevice_ShouldRequirePointsToBeRemovedFirst()
    {
        string path = CreateTemporaryDatabasePath();
        try
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            SqliteConfigurationRepository repository = new(path);
            await repository.InitializeAsync(token);
            DeviceDefinition device = new(Guid.NewGuid(), "测试设备", "Simulation", "127.0.0.1", 502, true, 1000);
            PointDefinition point = CreatePoint(Guid.NewGuid(), device.Id, "TEST_DELETE");
            await repository.SaveDeviceAsync(device, token);
            await repository.SavePointAsync(point, token);
            Assert.False(await repository.DeleteDeviceAsync(device.Id, token));
            Assert.True(await repository.DeletePointAsync(point.Id, token));
            Assert.True(await repository.DeleteDeviceAsync(device.Id, token));
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>确认批量导入中任一点位冲突时整个事务回滚。</summary>
    [Fact]
    public async Task SavePointsAsync_ShouldRollbackWholeBatchOnUniqueConflict()
    {
        string path = CreateTemporaryDatabasePath();
        try
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            SqliteConfigurationRepository repository = new(path);
            await repository.InitializeAsync(token);
            Guid deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            PointDefinition first = CreatePoint(Guid.NewGuid(), deviceId, "ATOMIC_TEST");
            PointDefinition duplicate = CreatePoint(Guid.NewGuid(), deviceId, "ATOMIC_TEST");
            await Assert.ThrowsAsync<SqliteException>(() => repository.SavePointsAsync([first, duplicate], token));
            IReadOnlyList<PointDefinition> points = await repository.GetPointsAsync(token);
            Assert.DoesNotContain(points, point => point.Code == "ATOMIC_TEST");
        }
        finally
        {
            Cleanup(path);
        }
    }

    /// <summary>创建测试点位。</summary>
    private static PointDefinition CreatePoint(Guid id, Guid deviceId, string code) => new(
        id, deviceId, code, code, "D200", PointDataType.Double, 1, 0, "", 1000, 0,
        HistoryRecordMode.None, true, false);

    /// <summary>生成位于系统临时目录的唯一数据库路径。</summary>
    private static string CreateTemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"forgelink-crud-{Guid.NewGuid():N}.db");

    /// <summary>清理当前测试创建的数据库及 WAL 辅助文件。</summary>
    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        DeleteIfExists(path);
        DeleteIfExists(path + "-shm");
        DeleteIfExists(path + "-wal");
    }

    /// <summary>删除明确命名的单个临时文件。</summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
