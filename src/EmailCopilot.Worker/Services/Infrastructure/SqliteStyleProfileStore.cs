using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class SqliteStyleProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqliteStyleProfileStore(IOptions<DatabaseOptions> options)
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
            CREATE TABLE IF NOT EXISTS StyleProfileSegments (
                SegmentKey TEXT PRIMARY KEY,
                Greeting TEXT NOT NULL,
                Closing TEXT NOT NULL,
                Tone TEXT NOT NULL,
                Signature TEXT NOT NULL,
                CommonPhrasesJson TEXT NOT NULL,
                AverageSentenceLength REAL NOT NULL,
                GreetingUsageRate REAL NOT NULL DEFAULT 0,
                SignoffUsageRate REAL NOT NULL DEFAULT 0,
                QuestionEndingRate REAL NOT NULL DEFAULT 0,
                GratitudeUsageRate REAL NOT NULL DEFAULT 0,
                ContractionUsageRate REAL NOT NULL DEFAULT 0,
                FragmentUsageRate REAL NOT NULL DEFAULT 0,
                ExplicitNextStepRate REAL NOT NULL DEFAULT 0,
                TypicalSentenceCountMin INTEGER NOT NULL DEFAULT 1,
                TypicalSentenceCountMax INTEGER NOT NULL DEFAULT 2,
                FormalityScore REAL NOT NULL DEFAULT 0.5,
                SampleSize INTEGER NOT NULL,
                BuiltAtUtc TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "StyleProfileSegments", "GreetingUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "SignoffUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "QuestionEndingRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "GratitudeUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "ContractionUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "FragmentUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "ExplicitNextStepRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "TypicalSentenceCountMin", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "TypicalSentenceCountMax", "INTEGER NOT NULL DEFAULT 2", cancellationToken);
        await EnsureColumnAsync(connection, "StyleProfileSegments", "FormalityScore", "REAL NOT NULL DEFAULT 0.5", cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, StyleProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SegmentKey,
                   Greeting,
                   Closing,
                   Tone,
                   Signature,
                   CommonPhrasesJson,
                   AverageSentenceLength,
                   GreetingUsageRate,
                   SignoffUsageRate,
                   QuestionEndingRate,
                   GratitudeUsageRate,
                   ContractionUsageRate,
                   FragmentUsageRate,
                   ExplicitNextStepRate,
                   TypicalSentenceCountMin,
                   TypicalSentenceCountMax,
                   FormalityScore,
                   SampleSize,
                   BuiltAtUtc
            FROM StyleProfileSegments;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new Dictionary<string, StyleProfile>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            var commonPhrasesJson = reader.GetString(5);
            var commonPhrases = JsonSerializer.Deserialize<List<string>>(commonPhrasesJson, JsonOptions) ?? [];

            var profile = new StyleProfile(
                SegmentKey: reader.GetString(0),
                Greeting: reader.GetString(1),
                Closing: reader.GetString(2),
                Tone: reader.GetString(3),
                Signature: reader.GetString(4),
                CommonPhrases: commonPhrases,
                AverageSentenceLength: reader.GetDouble(6),
                GreetingUsageRate: reader.GetDouble(7),
                SignoffUsageRate: reader.GetDouble(8),
                QuestionEndingRate: reader.GetDouble(9),
                GratitudeUsageRate: reader.GetDouble(10),
                ContractionUsageRate: reader.GetDouble(11),
                FragmentUsageRate: reader.GetDouble(12),
                ExplicitNextStepRate: reader.GetDouble(13),
                TypicalSentenceCountMin: reader.GetInt32(14),
                TypicalSentenceCountMax: reader.GetInt32(15),
                FormalityScore: reader.GetDouble(16),
                SampleSize: reader.GetInt32(17),
                BuiltAtUtc: DateTimeOffset.Parse(reader.GetString(18)));

            profiles[profile.SegmentKey] = profile;
        }

        return profiles;
    }

    public async Task ReplaceAllAsync(
        IEnumerable<StyleProfile> profiles,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = sqliteTransaction;
            deleteCommand.CommandText = "DELETE FROM StyleProfileSegments;";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var profile in profiles)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = sqliteTransaction;
            insertCommand.CommandText =
                """
                INSERT INTO StyleProfileSegments (
                    SegmentKey,
                    Greeting,
                    Closing,
                    Tone,
                    Signature,
                    CommonPhrasesJson,
                    AverageSentenceLength,
                    GreetingUsageRate,
                    SignoffUsageRate,
                    QuestionEndingRate,
                    GratitudeUsageRate,
                    ContractionUsageRate,
                    FragmentUsageRate,
                    ExplicitNextStepRate,
                    TypicalSentenceCountMin,
                    TypicalSentenceCountMax,
                    FormalityScore,
                    SampleSize,
                    BuiltAtUtc
                )
                VALUES (
                    $segmentKey,
                    $greeting,
                    $closing,
                    $tone,
                    $signature,
                    $commonPhrasesJson,
                    $averageSentenceLength,
                    $greetingUsageRate,
                    $signoffUsageRate,
                    $questionEndingRate,
                    $gratitudeUsageRate,
                    $contractionUsageRate,
                    $fragmentUsageRate,
                    $explicitNextStepRate,
                    $typicalSentenceCountMin,
                    $typicalSentenceCountMax,
                    $formalityScore,
                    $sampleSize,
                    $builtAtUtc
                );
                """;

            insertCommand.Parameters.AddWithValue("$segmentKey", profile.SegmentKey);
            insertCommand.Parameters.AddWithValue("$greeting", profile.Greeting);
            insertCommand.Parameters.AddWithValue("$closing", profile.Closing);
            insertCommand.Parameters.AddWithValue("$tone", profile.Tone);
            insertCommand.Parameters.AddWithValue("$signature", profile.Signature);
            insertCommand.Parameters.AddWithValue("$commonPhrasesJson", JsonSerializer.Serialize(profile.CommonPhrases, JsonOptions));
            insertCommand.Parameters.AddWithValue("$averageSentenceLength", profile.AverageSentenceLength);
            insertCommand.Parameters.AddWithValue("$greetingUsageRate", profile.GreetingUsageRate);
            insertCommand.Parameters.AddWithValue("$signoffUsageRate", profile.SignoffUsageRate);
            insertCommand.Parameters.AddWithValue("$questionEndingRate", profile.QuestionEndingRate);
            insertCommand.Parameters.AddWithValue("$gratitudeUsageRate", profile.GratitudeUsageRate);
            insertCommand.Parameters.AddWithValue("$contractionUsageRate", profile.ContractionUsageRate);
            insertCommand.Parameters.AddWithValue("$fragmentUsageRate", profile.FragmentUsageRate);
            insertCommand.Parameters.AddWithValue("$explicitNextStepRate", profile.ExplicitNextStepRate);
            insertCommand.Parameters.AddWithValue("$typicalSentenceCountMin", profile.TypicalSentenceCountMin);
            insertCommand.Parameters.AddWithValue("$typicalSentenceCountMax", profile.TypicalSentenceCountMax);
            insertCommand.Parameters.AddWithValue("$formalityScore", profile.FormalityScore);
            insertCommand.Parameters.AddWithValue("$sampleSize", profile.SampleSize);
            insertCommand.Parameters.AddWithValue("$builtAtUtc", profile.BuiltAtUtc.UtcDateTime.ToString("O"));

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
