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
                ExclamationUsageRate REAL NOT NULL DEFAULT 0,
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

        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "GreetingUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "SignoffUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "QuestionEndingRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "ExclamationUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "GratitudeUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "ContractionUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "FragmentUsageRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "ExplicitNextStepRate", "REAL NOT NULL DEFAULT 0", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "TypicalSentenceCountMin", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "TypicalSentenceCountMax", "INTEGER NOT NULL DEFAULT 2", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "FormalityScore", "REAL NOT NULL DEFAULT 0.5", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "AvoidPhrasesJson", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "FavoredPhrasesJson", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "StyleProfileSegments", "ObservedDiscourseMarkersJson", "TEXT NULL", cancellationToken);
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
                   BuiltAtUtc,
                   AvoidPhrasesJson,
                   FavoredPhrasesJson,
                   ExclamationUsageRate,
                   ObservedDiscourseMarkersJson
            FROM StyleProfileSegments;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new Dictionary<string, StyleProfile>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken))
        {
            var commonPhrasesJson = reader.GetString(5);
            var commonPhrases = JsonSerializer.Deserialize<List<string>>(commonPhrasesJson, JsonOptions) ?? [];

            IReadOnlyDictionary<string, IReadOnlyList<string>>? avoidPhrases = null;
            DateTimeOffset? avoidPhrasesUpdatedAtUtc = null;

            if (!reader.IsDBNull(19))
            {
                var avoidJson = reader.GetString(19);
                if (!string.IsNullOrWhiteSpace(avoidJson))
                {
                    var blob = JsonSerializer.Deserialize<PhrasesBlob>(avoidJson, JsonOptions);
                    if (blob is not null)
                    {
                        if (blob.Phrases is not null && blob.Phrases.Count > 0)
                        {
                            avoidPhrases = blob.Phrases.ToDictionary(
                                static kv => kv.Key,
                                static kv => (IReadOnlyList<string>)kv.Value.ToArray(),
                                StringComparer.OrdinalIgnoreCase);
                        }
                        avoidPhrasesUpdatedAtUtc = blob.UpdatedAtUtc;
                    }
                }
            }

            IReadOnlyDictionary<string, IReadOnlyList<string>>? favoredPhrases = null;
            DateTimeOffset? favoredPhrasesUpdatedAtUtc = null;

            if (!reader.IsDBNull(20))
            {
                var favoredJson = reader.GetString(20);
                if (!string.IsNullOrWhiteSpace(favoredJson))
                {
                    var blob = JsonSerializer.Deserialize<PhrasesBlob>(favoredJson, JsonOptions);
                    if (blob is not null)
                    {
                        if (blob.Phrases is not null && blob.Phrases.Count > 0)
                        {
                            favoredPhrases = blob.Phrases.ToDictionary(
                                static kv => kv.Key,
                                static kv => (IReadOnlyList<string>)kv.Value.ToArray(),
                                StringComparer.OrdinalIgnoreCase);
                        }
                        favoredPhrasesUpdatedAtUtc = blob.UpdatedAtUtc;
                    }
                }
            }

            var exclamationUsageRate = reader.IsDBNull(21) ? 0.0 : reader.GetDouble(21);

            IReadOnlyList<DiscourseMarkerObservation>? observedMarkers = null;
            if (reader.FieldCount > 22 && !reader.IsDBNull(22))
            {
                var markersJson = reader.GetString(22);
                if (!string.IsNullOrWhiteSpace(markersJson))
                {
                    observedMarkers = JsonSerializer
                        .Deserialize<List<DiscourseMarkerObservation>>(markersJson, JsonOptions)
                        ?? new List<DiscourseMarkerObservation>();
                }
            }

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
                ExclamationUsageRate: exclamationUsageRate,
                GratitudeUsageRate: reader.GetDouble(10),
                ContractionUsageRate: reader.GetDouble(11),
                FragmentUsageRate: reader.GetDouble(12),
                ExplicitNextStepRate: reader.GetDouble(13),
                TypicalSentenceCountMin: reader.GetInt32(14),
                TypicalSentenceCountMax: reader.GetInt32(15),
                FormalityScore: reader.GetDouble(16),
                SampleSize: reader.GetInt32(17),
                BuiltAtUtc: DateTimeOffset.Parse(reader.GetString(18)),
                AvoidPhrases: avoidPhrases,
                AvoidPhrasesUpdatedAtUtc: avoidPhrasesUpdatedAtUtc,
                FavoredPhrases: favoredPhrases,
                FavoredPhrasesUpdatedAtUtc: favoredPhrasesUpdatedAtUtc,
                ObservedDiscourseMarkers: observedMarkers);

            profiles[profile.SegmentKey] = profile;
        }

        return profiles;
    }

    private sealed class PhrasesBlob
    {
        public Dictionary<string, List<string>>? Phrases { get; set; }
        public DateTimeOffset? UpdatedAtUtc { get; set; }
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
                    ExclamationUsageRate,
                    GratitudeUsageRate,
                    ContractionUsageRate,
                    FragmentUsageRate,
                    ExplicitNextStepRate,
                    TypicalSentenceCountMin,
                    TypicalSentenceCountMax,
                    FormalityScore,
                    SampleSize,
                    BuiltAtUtc,
                    AvoidPhrasesJson,
                    FavoredPhrasesJson,
                    ObservedDiscourseMarkersJson
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
                    $exclamationUsageRate,
                    $gratitudeUsageRate,
                    $contractionUsageRate,
                    $fragmentUsageRate,
                    $explicitNextStepRate,
                    $typicalSentenceCountMin,
                    $typicalSentenceCountMax,
                    $formalityScore,
                    $sampleSize,
                    $builtAtUtc,
                    $avoidPhrasesJson,
                    $favoredPhrasesJson,
                    $observedDiscourseMarkersJson
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
            insertCommand.Parameters.AddWithValue("$exclamationUsageRate", profile.ExclamationUsageRate);
            insertCommand.Parameters.AddWithValue("$gratitudeUsageRate", profile.GratitudeUsageRate);
            insertCommand.Parameters.AddWithValue("$contractionUsageRate", profile.ContractionUsageRate);
            insertCommand.Parameters.AddWithValue("$fragmentUsageRate", profile.FragmentUsageRate);
            insertCommand.Parameters.AddWithValue("$explicitNextStepRate", profile.ExplicitNextStepRate);
            insertCommand.Parameters.AddWithValue("$typicalSentenceCountMin", profile.TypicalSentenceCountMin);
            insertCommand.Parameters.AddWithValue("$typicalSentenceCountMax", profile.TypicalSentenceCountMax);
            insertCommand.Parameters.AddWithValue("$formalityScore", profile.FormalityScore);
            insertCommand.Parameters.AddWithValue("$sampleSize", profile.SampleSize);
            insertCommand.Parameters.AddWithValue("$builtAtUtc", profile.BuiltAtUtc.UtcDateTime.ToString("O"));

            object avoidPhrasesValue = DBNull.Value;
            if (profile.AvoidPhrases is not null && profile.AvoidPhrases.Count > 0)
            {
                var blob = new PhrasesBlob
                {
                    Phrases = profile.AvoidPhrases.ToDictionary(
                        static kv => kv.Key,
                        static kv => kv.Value.ToList(),
                        StringComparer.OrdinalIgnoreCase),
                    UpdatedAtUtc = profile.AvoidPhrasesUpdatedAtUtc
                };
                avoidPhrasesValue = JsonSerializer.Serialize(blob, JsonOptions);
            }
            insertCommand.Parameters.AddWithValue("$avoidPhrasesJson", avoidPhrasesValue);

            object favoredPhrasesValue = DBNull.Value;
            if (profile.FavoredPhrases is not null && profile.FavoredPhrases.Count > 0)
            {
                var blob = new PhrasesBlob
                {
                    Phrases = profile.FavoredPhrases.ToDictionary(
                        static kv => kv.Key,
                        static kv => kv.Value.ToList(),
                        StringComparer.OrdinalIgnoreCase),
                    UpdatedAtUtc = profile.FavoredPhrasesUpdatedAtUtc
                };
                favoredPhrasesValue = JsonSerializer.Serialize(blob, JsonOptions);
            }
            insertCommand.Parameters.AddWithValue("$favoredPhrasesJson", favoredPhrasesValue);

            object observedDiscourseMarkersValue = DBNull.Value;
            if (profile.ObservedDiscourseMarkers is not null && profile.ObservedDiscourseMarkers.Count > 0)
            {
                observedDiscourseMarkersValue = JsonSerializer.Serialize(profile.ObservedDiscourseMarkers, JsonOptions);
            }
            insertCommand.Parameters.AddWithValue("$observedDiscourseMarkersJson", observedDiscourseMarkersValue);

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

}
