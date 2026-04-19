using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteStyleExampleStoreTests
{
    [Test]
    public async Task Should_initialize_replace_and_read_style_examples_in_descending_recency_order()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-style-examples-{Guid.NewGuid():N}.db");

        try
        {
            var store = CreateStore(databasePath);

            await store.InitializeAsync(CancellationToken.None);
            await store.ReplaceBySegmentAsync(
                "domain:example.com",
                [
                    new StyleExample("domain:example.com", "Older example body", "Older subject", new DateTimeOffset(2026, 4, 15, 9, 0, 0, TimeSpan.Zero)),
                    new StyleExample("domain:example.com", "Newer example body", "Newer subject", new DateTimeOffset(2026, 4, 16, 10, 0, 0, TimeSpan.Zero))
                ],
                CancellationToken.None);

            var examples = await store.GetBySegmentAsync("domain:example.com", CancellationToken.None);

            Assert.That(examples, Has.Count.EqualTo(2));
            Assert.That(examples[0].SubjectHint, Is.EqualTo("Newer subject"));
            Assert.That(examples[1].SubjectHint, Is.EqualTo("Older subject"));

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'StyleExamples';";
            var tableCount = (long)(await command.ExecuteScalarAsync() ?? 0L);

            Assert.That(tableCount, Is.EqualTo(1));
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

    [Test]
    public async Task ReplaceBySegment_should_replace_only_the_requested_segment()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-style-examples-replace-{Guid.NewGuid():N}.db");

        try
        {
            var store = CreateStore(databasePath);

            await store.InitializeAsync(CancellationToken.None);
            await store.ReplaceBySegmentAsync(
                "domain:one.com",
                [new StyleExample("domain:one.com", "Segment one body", "Segment one", DateTimeOffset.UtcNow.AddDays(-1))],
                CancellationToken.None);
            await store.ReplaceBySegmentAsync(
                "domain:two.com",
                [new StyleExample("domain:two.com", "Segment two body", "Segment two", DateTimeOffset.UtcNow)],
                CancellationToken.None);

            await store.ReplaceBySegmentAsync(
                "domain:one.com",
                [new StyleExample("domain:one.com", "Replacement body", "Replacement", DateTimeOffset.UtcNow.AddHours(-2))],
                CancellationToken.None);

            var segmentOne = await store.GetBySegmentAsync("domain:one.com", CancellationToken.None);
            var segmentTwo = await store.GetBySegmentAsync("domain:two.com", CancellationToken.None);

            Assert.That(segmentOne.Select(static example => example.SubjectHint), Is.EqualTo(new[] { "Replacement" }));
            Assert.That(segmentTwo.Select(static example => example.SubjectHint), Is.EqualTo(new[] { "Segment two" }));
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

    private static SqliteStyleExampleStore CreateStore(string databasePath) =>
        new(Options.Create(new DatabaseOptions
        {
            ConnectionString = $"Data Source={databasePath};Pooling=False"
        }));
}
