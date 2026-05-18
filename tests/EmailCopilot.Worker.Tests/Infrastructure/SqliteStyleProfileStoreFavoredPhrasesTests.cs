using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteStyleProfileStoreFavoredPhrasesTests
{
    [Test]
    public async Task Profile_with_FavoredPhrases_is_persisted_and_round_tripped()
    {
        var databasePath = NewDatabasePath();

        try
        {
            var store = CreateStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);

            var favoredPhrases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [ReplyShapes.Acknowledge] = new[] { "Got it, that works for me.", "Got it, all sorted." },
                [ReplyShapes.DirectAnswer] = new[] { "Can do Sunday afternoon." }
            };
            var updatedAt = new DateTimeOffset(2026, 5, 5, 9, 30, 0, TimeSpan.Zero);

            var profile = CreateProfile("domain:example.com", favoredPhrases, updatedAt);

            await store.ReplaceAllAsync([profile], CancellationToken.None);

            var loaded = await store.GetAllAsync(CancellationToken.None);

            Assert.That(loaded, Contains.Key("domain:example.com"));
            var loadedProfile = loaded["domain:example.com"];
            Assert.That(loadedProfile.FavoredPhrases, Is.Not.Null);
            Assert.That(loadedProfile.FavoredPhrases!.Keys, Is.EquivalentTo(new[] { ReplyShapes.Acknowledge, ReplyShapes.DirectAnswer }));
            Assert.That(loadedProfile.FavoredPhrases[ReplyShapes.Acknowledge],
                Is.EqualTo(new[] { "Got it, that works for me.", "Got it, all sorted." }));
            Assert.That(loadedProfile.FavoredPhrases[ReplyShapes.DirectAnswer],
                Is.EqualTo(new[] { "Can do Sunday afternoon." }));
            Assert.That(loadedProfile.FavoredPhrasesUpdatedAtUtc, Is.EqualTo(updatedAt));
        }
        finally
        {
            CleanupDatabase(databasePath);
        }
    }

    [Test]
    public async Task Profile_without_FavoredPhrases_round_trips_as_null()
    {
        var databasePath = NewDatabasePath();

        try
        {
            var store = CreateStore(databasePath);
            await store.InitializeAsync(CancellationToken.None);

            var profile = CreateProfile("domain:plain.com", favoredPhrases: null, favoredPhrasesUpdatedAtUtc: null);
            await store.ReplaceAllAsync([profile], CancellationToken.None);

            var loaded = await store.GetAllAsync(CancellationToken.None);

            Assert.That(loaded, Contains.Key("domain:plain.com"));
            var loadedProfile = loaded["domain:plain.com"];
            Assert.That(loadedProfile.FavoredPhrases, Is.Null);
            Assert.That(loadedProfile.FavoredPhrasesUpdatedAtUtc, Is.Null);
        }
        finally
        {
            CleanupDatabase(databasePath);
        }
    }

    [Test]
    public async Task Legacy_row_with_null_FavoredPhrasesJson_reads_as_null()
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
            var legacy = loaded["domain:legacy.com"];
            Assert.That(legacy.FavoredPhrases, Is.Null);
            Assert.That(legacy.FavoredPhrasesUpdatedAtUtc, Is.Null);
        }
        finally
        {
            CleanupDatabase(databasePath);
        }
    }

    private static StyleProfile CreateProfile(
        string segmentKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? favoredPhrases,
        DateTimeOffset? favoredPhrasesUpdatedAtUtc) =>
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
            0.02,
            0.1,
            0.4,
            0.1,
            0.2,
            1,
            2,
            0.5,
            5,
            new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero),
            FavoredPhrases: favoredPhrases,
            FavoredPhrasesUpdatedAtUtc: favoredPhrasesUpdatedAtUtc);

    private static SqliteStyleProfileStore CreateStore(string databasePath) =>
        new(Options.Create(new DatabaseOptions
        {
            ConnectionString = BuildConnectionString(databasePath)
        }));

    private static string BuildConnectionString(string databasePath) =>
        $"Data Source={databasePath};Pooling=False";

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"email-copilot-style-profile-favored-{Guid.NewGuid():N}.db");

    private static void CleanupDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}
