using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteRunRecordStoreTests
{
    [Test]
    public async Task Should_initialize_and_persist_run_record()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-run-record-{Guid.NewGuid():N}.db");

        try
        {
            await AssertRunRecordRoundTripAsync(databasePath);
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

    private static async Task AssertRunRecordRoundTripAsync(string databasePath)
    {
        var store = new SqliteRunRecordStore(
            Options.Create(new DatabaseOptions
            {
                ConnectionString = $"Data Source={databasePath};Pooling=False"
            }));

        await store.InitializeAsync(CancellationToken.None);

        var runRecord = new RunRecord(
            DateTimeOffset.UtcNow.AddSeconds(-2),
            DateTimeOffset.UtcNow,
            0,
            3,
            25,
            24,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [ClassificationReasonCodes.ListOrBroadcastMail] = 20,
                [ClassificationReasonCodes.NoReplySender] = 4
            },
            true,
            123,
            456,
            null);

        var insertedId = await store.InsertAsync(runRecord, CancellationToken.None);

        Assert.That(insertedId, Is.GreaterThan(0));

        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ExitCode, CandidateWindowsScanned, CandidatesEvaluated, SkippedCount, DraftCreated, DraftId, DraftSourceImapUid, ErrorMessage, SkipsByReasonCodeJson
            FROM RunRecords
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", insertedId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(0));
        Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        Assert.That(reader.GetInt32(2), Is.EqualTo(25));
        Assert.That(reader.GetInt32(3), Is.EqualTo(24));
        Assert.That(reader.GetInt32(4), Is.EqualTo(1));
        Assert.That(reader.GetInt64(5), Is.EqualTo(123));
        Assert.That(reader.GetInt64(6), Is.EqualTo(456));
        Assert.That(reader.IsDBNull(7), Is.True);
        Assert.That(reader.GetString(8), Does.Contain(ClassificationReasonCodes.ListOrBroadcastMail));
    }
}
