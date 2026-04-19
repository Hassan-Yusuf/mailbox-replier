using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class SqliteRunRecordStore : IRunRecordStore
{
    private readonly string _connectionString;

    public SqliteRunRecordStore(IOptions<DatabaseOptions> options)
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
            CREATE TABLE IF NOT EXISTS RunRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAtUtc TEXT NOT NULL,
                FinishedAtUtc TEXT NOT NULL,
                DurationMs REAL NOT NULL,
                ExitCode INTEGER NOT NULL,
                CandidateWindowsScanned INTEGER NOT NULL,
                CandidatesEvaluated INTEGER NOT NULL,
                SkippedCount INTEGER NOT NULL,
                SkipsByReasonCodeJson TEXT NOT NULL,
                DraftCreated INTEGER NOT NULL,
                DraftId INTEGER NULL,
                DraftSourceImapUid INTEGER NULL,
                ErrorMessage TEXT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> InsertAsync(RunRecord runRecord, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO RunRecords (
                StartedAtUtc,
                FinishedAtUtc,
                DurationMs,
                ExitCode,
                CandidateWindowsScanned,
                CandidatesEvaluated,
                SkippedCount,
                SkipsByReasonCodeJson,
                DraftCreated,
                DraftId,
                DraftSourceImapUid,
                ErrorMessage
            )
            VALUES (
                $startedAtUtc,
                $finishedAtUtc,
                $durationMs,
                $exitCode,
                $candidateWindowsScanned,
                $candidatesEvaluated,
                $skippedCount,
                $skipsByReasonCodeJson,
                $draftCreated,
                $draftId,
                $draftSourceImapUid,
                $errorMessage
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$startedAtUtc", runRecord.StartedAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$finishedAtUtc", runRecord.FinishedAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$durationMs", runRecord.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$exitCode", runRecord.ExitCode);
        command.Parameters.AddWithValue("$candidateWindowsScanned", runRecord.CandidateWindowsScanned);
        command.Parameters.AddWithValue("$candidatesEvaluated", runRecord.CandidatesEvaluated);
        command.Parameters.AddWithValue("$skippedCount", runRecord.SkippedCount);
        command.Parameters.AddWithValue(
            "$skipsByReasonCodeJson",
            JsonSerializer.Serialize(runRecord.SkipsByReasonCode));
        command.Parameters.AddWithValue("$draftCreated", runRecord.DraftCreated ? 1 : 0);
        command.Parameters.AddWithValue("$draftId", (object?)runRecord.DraftId ?? DBNull.Value);
        command.Parameters.AddWithValue("$draftSourceImapUid", (object?)runRecord.DraftSourceImapUid ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)runRecord.ErrorMessage ?? DBNull.Value);

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(insertedId);
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
