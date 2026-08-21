// 文件说明：验证 TDengine 配置加密持久化和门禁元数据重置。
// 责任边界：使用测试保护器和临时 SQLite，不调用 Windows DPAPI 或真实 TDengine。

using System.Text;
using ForgeLink.Application;
using ForgeLink.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ForgeLink.Application.Tests;

public sealed class SqliteTDengineConfigurationTests
{
    [Fact]
    public async Task SaveConfiguration_ShouldProtectPasswordAndResetGateState()
    {
        string path = Path.Combine(Path.GetTempPath(), $"forgelink-td-config-{Guid.NewGuid():N}.db");
        try
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            SqliteConfigurationRepository repository = new(path, new TestSecretProtector());
            await repository.InitializeAsync(token);
            TDengineConnectionConfiguration original = new(
                "127.0.0.1", 6041, "forgelink", "Secret-123!", "forgelink_history",
                false, true, true, 10_000);

            await repository.SaveTDengineConfigurationAsync(original, token);
            await repository.SetHistoryGateStateAsync(true, true, token);
            TDengineConnectionConfiguration restored = Assert.IsType<TDengineConnectionConfiguration>(
                await repository.GetTDengineConfigurationAsync(token));
            Assert.Equal("Secret-123!", restored.Password);
            Assert.True(restored.TestPassed);
            Assert.True(restored.IsEnabled);

            await repository.SaveTDengineConfigurationAsync(original with { Port = 6042 }, token);
            restored = Assert.IsType<TDengineConnectionConfiguration>(await repository.GetTDengineConfigurationAsync(token));
            Assert.False(restored.TestPassed);
            Assert.False(restored.IsEnabled);

            await using SqliteConnection connection = new($"Data Source={path}");
            await connection.OpenAsync(token);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT password_protected FROM tdengine_connection WHERE singleton_id=1";
            byte[] stored = (byte[])(await command.ExecuteScalarAsync(token))!;
            Assert.NotEqual("Secret-123!", Encoding.UTF8.GetString(stored));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Delete(path);
            Delete(path + "-shm");
            Delete(path + "-wal");
        }
    }

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public byte[] Protect(string plaintext) => Encoding.UTF8.GetBytes(plaintext).Select(static value => (byte)(value ^ 0xA5)).ToArray();
        public string Unprotect(byte[] protectedPayload) => Encoding.UTF8.GetString(protectedPayload.Select(static value => (byte)(value ^ 0xA5)).ToArray());
    }
}
