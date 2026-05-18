using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteStyleProfileStoreExclamationRateTests
{
    [Test]
    public async Task Profile_with_exclamation_rate_is_persisted_and_round_tripped()
    {
        var databasePath = NewDatabasePath();

        try
        {
            var store = CreateStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);

            var profile = CreateProfile("domain:example.com", exclamationUsageRate: 0.37);

            await store.ReplaceAllAsync([profile], CancellationToken.None);

            var loaded = await store.GetAllAsync(CancellationToken.None);

            Assert.That(loaded, Contains.Key("domain:example.com"));
            Assert.That(loaded["domain:example.com"].ExclamationUsageRate, Is.EqualTo(0.37).Within(0.001));
        }
        finally
        {
            CleanupDatabase(databasePath);
        }
    }

    [Test]
    public async Task Legacy_row_without_exclamation_column_reads_as_zero_default()
    {
        var databasePath = NewDatabasePath();

        try
        {
            var store = CreateStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);

            await using (var connection = new SqliteConnection(BuildConnectionString(databasePath)))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO StyleProfileSegments
                        (SegmentKey, Greeting, Closing, Tone, Signature, CommonPhrasesJson,
                         AverageSentenceLength, GreetingUsageRate, SignoffUsageRate, QuestionEndingRate,
                         GratitudeUsageRate, ContractionUsageRate, FragmentUsageRate, ExplicitNextStepRate,
                         TypicalSentenceCountMin, TypicalSentenceCountMax, FormalityScore, SampleSize, BuiltAtUtc)
                    VALUES
                        ('domain:legacy.com', 'Hi', 'Thanks', 'neutral', '', '[]',
                         9.0, 0.5, 0.5, 0.1,
                         0.1, 0.4, 0.1, 0.2,
                         1, 2, 0.5, 5, '2026-04-30T10:00:00+00:00');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var loaded = await store.GetAllAsync(CancellationToken.None);

            Assert.That(loaded, Contains.Key("domain:legacy.com"));
            Assert.That(loaded["domain:legacy.com"].ExclamationUsageRate, Is.EqualTo(0.0));
        }
        finally
        {
            CleanupDatabase(databasePath);
        }
    }

    private static StyleProfile CreateProfile(string segmentKey, double exclamationUsageRate) =>
        new(
            segmentKey,
            "Hi",
            "Thanks",
            "neutral",
            string.Empty,
            [],
            9.0,
            0.5,
            0.5,
            0.1,
            exclamationUsageRate,
            0.1,
            0.4,
            0.1,
            0.2,
            1,
            2,
            0.5,
            5,
            new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero));

    private static SqliteStyleProfileStore CreateStore(string databasePath) =>
        new(Options.Create(new DatabaseOptions
        {
            ConnectionString = BuildConnectionString(databasePath)
        }));

    private static string BuildConnectionString(string databasePath) =>
        $"Data Source={databasePath};Pooling=False";

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"email-copilot-style-profile-excl-{Guid.NewGuid():N}.db");

    private static void CleanupDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
