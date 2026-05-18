using Microsoft.Data.Sqlite;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteMigrationHelpersTests
{
    [Test]
    public async Task EnsureColumnAsync_adds_missing_column()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateTableAsync(connection, "Widgets", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL");

        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "Widgets", "Note", "TEXT NULL", CancellationToken.None);

        Assert.That(await ColumnExistsAsync(connection, "Widgets", "Note"), Is.True);
    }

    [Test]
    public async Task EnsureColumnAsync_is_idempotent_when_column_already_present()
    {
        await using var connection = await OpenConnectionAsync();
        await CreateTableAsync(connection, "Widgets", "Id INTEGER PRIMARY KEY, Name TEXT NOT NULL");
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "Widgets", "Note", "TEXT NULL", CancellationToken.None);

        Assert.DoesNotThrowAsync(async () =>
            await SqliteMigrationHelpers.EnsureColumnAsync(connection, "Widgets", "Note", "TEXT NULL", CancellationToken.None));

        var columnCount = await CountColumnsAsync(connection, "Widgets", "Note");
        Assert.That(columnCount, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureSchemaVersionTableAsync_creates_table_when_missing()
    {
        await using var connection = await OpenConnectionAsync();

        await SqliteMigrationHelpers.EnsureSchemaVersionTableAsync(connection, CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SchemaVersions';";
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task GetSchemaVersionAsync_returns_zero_when_store_unknown()
    {
        await using var connection = await OpenConnectionAsync();
        await SqliteMigrationHelpers.EnsureSchemaVersionTableAsync(connection, CancellationToken.None);

        var version = await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "NonExistentStore", CancellationToken.None);

        Assert.That(version, Is.EqualTo(0));
    }

    [Test]
    public async Task SetSchemaVersionAsync_inserts_then_updates_version()
    {
        await using var connection = await OpenConnectionAsync();
        await SqliteMigrationHelpers.EnsureSchemaVersionTableAsync(connection, CancellationToken.None);

        await SqliteMigrationHelpers.SetSchemaVersionAsync(connection, "DraftStore", 1, CancellationToken.None);
        Assert.That(await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "DraftStore", CancellationToken.None), Is.EqualTo(1));

        await SqliteMigrationHelpers.SetSchemaVersionAsync(connection, "DraftStore", 5, CancellationToken.None);
        Assert.That(await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "DraftStore", CancellationToken.None), Is.EqualTo(5));
    }

    [Test]
    public async Task SetSchemaVersionAsync_tracks_multiple_stores_independently()
    {
        await using var connection = await OpenConnectionAsync();
        await SqliteMigrationHelpers.EnsureSchemaVersionTableAsync(connection, CancellationToken.None);

        await SqliteMigrationHelpers.SetSchemaVersionAsync(connection, "DraftStore", 3, CancellationToken.None);
        await SqliteMigrationHelpers.SetSchemaVersionAsync(connection, "StyleProfileStore", 7, CancellationToken.None);

        Assert.That(await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "DraftStore", CancellationToken.None), Is.EqualTo(3));
        Assert.That(await SqliteMigrationHelpers.GetSchemaVersionAsync(connection, "StyleProfileStore", CancellationToken.None), Is.EqualTo(7));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task CreateTableAsync(SqliteConnection connection, string tableName, string columns)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE {tableName} ({columns});";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        return await CountColumnsAsync(connection, tableName, columnName) > 0;
    }

    private static async Task<long> CountColumnsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column;";
        command.Parameters.AddWithValue("$table", tableName);
        command.Parameters.AddWithValue("$column", columnName);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
