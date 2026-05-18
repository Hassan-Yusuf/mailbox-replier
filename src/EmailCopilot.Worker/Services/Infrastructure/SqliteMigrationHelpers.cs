using Microsoft.Data.Sqlite;

namespace EmailCopilot.Worker;

public static class SqliteMigrationHelpers
{
    public static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM pragma_table_info($table)
            WHERE name = $column;
            """;
        existsCommand.Parameters.AddWithValue("$table", tableName);
        existsCommand.Parameters.AddWithValue("$column", columnName);

        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task EnsureSchemaVersionTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaVersions (
                StoreName TEXT PRIMARY KEY,
                Version INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        string storeName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaVersions WHERE StoreName = $store;";
        command.Parameters.AddWithValue("$store", storeName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public static async Task SetSchemaVersionAsync(
        SqliteConnection connection,
        string storeName,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SchemaVersions (StoreName, Version)
            VALUES ($store, $version)
            ON CONFLICT(StoreName) DO UPDATE SET Version = excluded.Version;
            """;
        command.Parameters.AddWithValue("$store", storeName);
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
