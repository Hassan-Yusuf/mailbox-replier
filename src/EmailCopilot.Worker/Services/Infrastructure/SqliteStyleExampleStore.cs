using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class SqliteStyleExampleStore : IStyleExampleStore
{
    private readonly string _connectionString;

    public SqliteStyleExampleStore(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureDatabaseDirectoryExists();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS StyleExamples (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SegmentKey TEXT NOT NULL,
                ExampleBody TEXT NOT NULL,
                SubjectHint TEXT NOT NULL,
                SentAtUtc TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceBySegmentAsync(
        string segmentKey,
        IReadOnlyList<StyleExample> examples,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = sqliteTransaction;
            deleteCommand.CommandText = "DELETE FROM StyleExamples WHERE SegmentKey = $segmentKey;";
            deleteCommand.Parameters.AddWithValue("$segmentKey", segmentKey);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var example in examples)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = sqliteTransaction;
            insertCommand.CommandText =
                """
                INSERT INTO StyleExamples (
                    SegmentKey,
                    ExampleBody,
                    SubjectHint,
                    SentAtUtc
                )
                VALUES (
                    $segmentKey,
                    $exampleBody,
                    $subjectHint,
                    $sentAtUtc
                );
                """;

            insertCommand.Parameters.AddWithValue("$segmentKey", segmentKey);
            insertCommand.Parameters.AddWithValue("$exampleBody", example.ExampleBody);
            insertCommand.Parameters.AddWithValue("$subjectHint", example.SubjectHint);
            insertCommand.Parameters.AddWithValue("$sentAtUtc", example.SentAtUtc.UtcDateTime.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StyleExample>> GetBySegmentAsync(
        string segmentKey,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SegmentKey, ExampleBody, SubjectHint, SentAtUtc
            FROM StyleExamples
            WHERE SegmentKey = $segmentKey
            ORDER BY SentAtUtc DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$segmentKey", segmentKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var examples = new List<StyleExample>();

        while (await reader.ReadAsync(cancellationToken))
        {
            examples.Add(new StyleExample(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }

        return examples;
    }

    private void EnsureDatabaseDirectoryExists()
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            return;
        }

        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
