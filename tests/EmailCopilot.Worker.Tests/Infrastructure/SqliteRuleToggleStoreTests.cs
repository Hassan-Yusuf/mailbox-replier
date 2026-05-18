using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteRuleToggleStoreTests
{
    [Test]
    public async Task Initialize_creates_table_and_stamps_schema_version()
    {
        await UseTempDatabaseAsync(async (databasePath, store) =>
        {
            await store.InitializeAsync(CancellationToken.None);

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();

            await using var tableCommand = connection.CreateCommand();
            tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RuleToggles';";
            var tableCount = (long)(await tableCommand.ExecuteScalarAsync() ?? 0L);
            Assert.That(tableCount, Is.EqualTo(1));

            var schemaVersion = await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "RuleToggle", CancellationToken.None);
            Assert.That(schemaVersion, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LoadSnapshot_is_empty_when_no_toggles_have_been_set()
    {
        await UseTempDatabaseAsync(async (_databasePath, store) =>
        {
            await store.InitializeAsync(CancellationToken.None);

            var snapshot = await store.LoadSnapshotAsync(CancellationToken.None);

            Assert.That(snapshot, Is.Empty);
        });
    }

    [Test]
    public async Task SetAsync_persists_disabled_toggle_and_invalidates_cache()
    {
        await UseTempDatabaseAsync(async (_databasePath, store) =>
        {
            await store.InitializeAsync(CancellationToken.None);
            await store.LoadSnapshotAsync(CancellationToken.None);

            await store.SetAsync("BUILT_IN:NO_REPLY_PLATFORM_SENDER", false, CancellationToken.None);

            var snapshot = await store.LoadSnapshotAsync(CancellationToken.None);
            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(snapshot["BUILT_IN:NO_REPLY_PLATFORM_SENDER"], Is.False);
        });
    }

    [Test]
    public async Task SetAsync_upserts_existing_toggle()
    {
        await UseTempDatabaseAsync(async (_databasePath, store) =>
        {
            await store.InitializeAsync(CancellationToken.None);

            await store.SetAsync("BUILT_IN:NO_REPLY_PLATFORM_SENDER", false, CancellationToken.None);
            await store.SetAsync("BUILT_IN:NO_REPLY_PLATFORM_SENDER", true, CancellationToken.None);

            var snapshot = await store.LoadSnapshotAsync(CancellationToken.None);
            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(snapshot["BUILT_IN:NO_REPLY_PLATFORM_SENDER"], Is.True);
        });
    }

    [Test]
    public async Task SetAsync_rejects_empty_rule_name()
    {
        await UseTempDatabaseAsync(async (_databasePath, store) =>
        {
            await store.InitializeAsync(CancellationToken.None);

            Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.SetAsync(" ", false, CancellationToken.None));
        });
    }

    private static async Task UseTempDatabaseAsync(Func<string, SqliteRuleToggleStore, Task> body)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-rule-toggle-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteRuleToggleStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await body(databasePath, store);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
