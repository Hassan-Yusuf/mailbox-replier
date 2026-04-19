using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class SqliteDraftStoreTests
{
    [Test]
    public async Task Should_initialize_and_persist_draft_set_with_variant()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-draft-store-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteDraftStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await store.InitializeAsync(CancellationToken.None);

            var insertedId = await store.InsertAsync(
                new DraftSetRecord
                {
                    SourceImapUid = 123,
                    SourceMessageId = "message-123",
                    FromAddress = "sender@example.com",
                    Subject = "Need a reply",
                    OriginalBodyPreview = "Preview",
                    SourceReceivedAtUtc = new DateTimeOffset(2026, 4, 18, 8, 45, 0, TimeSpan.Zero),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    LlmMode = "mock",
                    IsAmbiguous = false,
                    Status = DraftSetStatuses.Pending,
                    AnalysisJson = "{\"asks\":[{\"text\":\"Can you send the reply?\",\"askType\":\"QUESTION\",\"isOptional\":false}],\"decisionBranches\":[],\"statedDeadlines\":[],\"urgency\":\"LOW\"}",
                    Variants =
                    [
                        new DraftVariantRecord
                        {
                            SortOrder = 0,
                            ReplyShape = ReplyShapes.GeneralReply,
                            ReplyShapeLabel = "General reply",
                            Body = "Hi,\n\nTest draft.",
                            ConfidenceScore = 0.75,
                            StyleSegmentUsed = "relationship-professional",
                            GroundingWarning = "unverified_date"
                        }
                    ]
                },
                CancellationToken.None);

            Assert.That(insertedId, Is.GreaterThan(0));

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();

            await using (var setCommand = connection.CreateCommand())
            {
                setCommand.CommandText =
                    """
                    SELECT SourceImapUid, Subject, IsAmbiguous, Status
                    FROM DraftSets
                    WHERE Id = $id;
                    """;
                setCommand.Parameters.AddWithValue("$id", insertedId);

                await using var reader = await setCommand.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(123));
                Assert.That(reader.GetString(1), Is.EqualTo("Need a reply"));
                Assert.That(reader.GetInt32(2), Is.EqualTo(0));
                Assert.That(reader.GetString(3), Is.EqualTo(DraftSetStatuses.Pending));
            }

            await using (var variantCommand = connection.CreateCommand())
            {
                variantCommand.CommandText =
                    """
                    SELECT DraftSetId, SortOrder, Intent, IntentLabel, Body, ConfidenceScore, StyleSegmentUsed, GroundingWarning
                    FROM DraftVariants
                    WHERE DraftSetId = $draftSetId;
                    """;
                variantCommand.Parameters.AddWithValue("$draftSetId", insertedId);

                await using var reader = await variantCommand.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(insertedId));
                Assert.That(reader.GetInt32(1), Is.EqualTo(0));
                Assert.That(reader.GetString(2), Is.EqualTo(ReplyShapes.GeneralReply));
                Assert.That(reader.GetString(3), Is.EqualTo("General reply"));
                Assert.That(reader.GetString(4), Does.Contain("Test draft"));
                Assert.That(reader.GetDouble(5), Is.EqualTo(0.75).Within(0.001));
                Assert.That(reader.GetString(6), Is.EqualTo("relationship-professional"));
                Assert.That(reader.GetString(7), Is.EqualTo("unverified_date"));
            }

            var processedUids = await store.GetProcessedSourceImapUidsAsync(CancellationToken.None);
            Assert.That(processedUids, Does.Contain(123u));
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
    public async Task Should_migrate_legacy_draft_replies_into_draft_sets_and_variants()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-draft-store-migrate-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();

                await using (var createLegacyCommand = connection.CreateCommand())
                {
                    createLegacyCommand.CommandText =
                        """
                        CREATE TABLE DraftReplies (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SourceImapUid INTEGER NOT NULL,
                            SourceMessageId TEXT NOT NULL,
                            FromAddress TEXT NOT NULL,
                            Subject TEXT NOT NULL,
                            OriginalBodyPreview TEXT NOT NULL,
                            DraftText TEXT NOT NULL,
                            CreatedAtUtc TEXT NOT NULL,
                            LlmMode TEXT NOT NULL
                        );
                        """;

                    await createLegacyCommand.ExecuteNonQueryAsync();
                }

                await using (var insertLegacyCommand = connection.CreateCommand())
                {
                    insertLegacyCommand.CommandText =
                        """
                        INSERT INTO DraftReplies (
                            SourceImapUid,
                            SourceMessageId,
                            FromAddress,
                            Subject,
                            OriginalBodyPreview,
                            DraftText,
                            CreatedAtUtc,
                            LlmMode
                        )
                        VALUES (
                            $sourceImapUid,
                            $sourceMessageId,
                            $fromAddress,
                            $subject,
                            $originalBodyPreview,
                            $draftText,
                            $createdAtUtc,
                            $llmMode
                        );
                        """;
                    insertLegacyCommand.Parameters.AddWithValue("$sourceImapUid", 456);
                    insertLegacyCommand.Parameters.AddWithValue("$sourceMessageId", "legacy-message-456");
                    insertLegacyCommand.Parameters.AddWithValue("$fromAddress", "legacy@example.com");
                    insertLegacyCommand.Parameters.AddWithValue("$subject", "Legacy subject");
                    insertLegacyCommand.Parameters.AddWithValue("$originalBodyPreview", "Legacy preview");
                    insertLegacyCommand.Parameters.AddWithValue("$draftText", "Hi,\n\nLegacy draft.");
                    insertLegacyCommand.Parameters.AddWithValue("$createdAtUtc", "2026-04-16T21:00:00.0000000Z");
                    insertLegacyCommand.Parameters.AddWithValue("$llmMode", "remote");

                    await insertLegacyCommand.ExecuteNonQueryAsync();
                }
            }

            var store = new SqliteDraftStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await store.InitializeAsync(CancellationToken.None);

            await using var verifyConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await verifyConnection.OpenAsync();

            await using (var setCommand = verifyConnection.CreateCommand())
            {
                setCommand.CommandText =
                    """
                    SELECT SourceImapUid, Subject, IsAmbiguous, Status
                    FROM DraftSets
                    WHERE SourceImapUid = 456;
                    """;

                await using var reader = await setCommand.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt64(0), Is.EqualTo(456));
                Assert.That(reader.GetString(1), Is.EqualTo("Legacy subject"));
                Assert.That(reader.GetInt32(2), Is.EqualTo(0));
                Assert.That(reader.GetString(3), Is.EqualTo(DraftSetStatuses.Pending));
            }

            await using (var variantCommand = verifyConnection.CreateCommand())
            {
                variantCommand.CommandText =
                    """
                    SELECT Intent, IntentLabel, Body, ConfidenceScore, StyleSegmentUsed
                    FROM DraftVariants
                    WHERE DraftSetId = (SELECT Id FROM DraftSets WHERE SourceImapUid = 456);
                    """;

                await using var reader = await variantCommand.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetString(0), Is.EqualTo(ReplyShapes.GeneralReply));
                Assert.That(reader.GetString(1), Is.EqualTo("General reply"));
                Assert.That(reader.GetString(2), Is.EqualTo("Hi,\n\nLegacy draft."));
                Assert.That(reader.GetDouble(3), Is.EqualTo(0.5).Within(0.001));
                Assert.That(reader.GetString(4), Is.EqualTo("legacy-history"));
            }

            await store.InitializeAsync(CancellationToken.None);

            await using (var countCommand = verifyConnection.CreateCommand())
            {
                countCommand.CommandText =
                    """
                    SELECT
                        (SELECT COUNT(*) FROM DraftSets WHERE SourceImapUid = 456),
                        (SELECT COUNT(*) FROM DraftVariants WHERE DraftSetId = (SELECT Id FROM DraftSets WHERE SourceImapUid = 456));
                    """;

                await using var reader = await countCommand.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));
                Assert.That(reader.GetInt32(1), Is.EqualTo(1));
            }
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
    public async Task Should_persist_draft_strategy_records()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-draft-strategy-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteDraftStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await store.InitializeAsync(CancellationToken.None);

            var insertedId = await store.InsertStrategyAsync(
                new DraftStrategyRecord
                {
                    SourceImapUid = 789,
                    SourceMessageId = "strategy-message-789",
                    FromAddress = "sender@example.com",
                    Subject = "Need a decision",
                    EligibilityDecision = DraftEligibilityDecisions.VariantCandidate,
                    EligibilityReason = "intent is genuinely ambiguous",
                    AmbiguityScore = 0.72,
                    PlannedReplyShapes = [ReplyShapes.Acknowledge, ReplyShapes.ConfirmAndRequest],
                    VariantCount = 2,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                CancellationToken.None);

            Assert.That(insertedId, Is.GreaterThan(0));

            await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EligibilityDecision, EligibilityReason, AmbiguityScore, ResolvedIntentsJson, VariantCount
                FROM DraftStrategies
                WHERE Id = $id;
                """;
            command.Parameters.AddWithValue("$id", insertedId);

            await using var reader = await command.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(0), Is.EqualTo(DraftEligibilityDecisions.VariantCandidate));
            Assert.That(reader.GetString(1), Is.EqualTo("intent is genuinely ambiguous"));
            Assert.That(reader.GetDouble(2), Is.EqualTo(0.72).Within(0.001));
            Assert.That(reader.GetString(3), Does.Contain(ReplyShapes.Acknowledge));
            Assert.That(reader.GetInt32(4), Is.EqualTo(2));
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
    public async Task Should_initialize_phase3_columns_and_rename_accepted_status_idempotently()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-draft-status-migrate-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();

                await using (var createDraftSetsCommand = connection.CreateCommand())
                {
                    createDraftSetsCommand.CommandText =
                        """
                        CREATE TABLE DraftSets (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SourceImapUid INTEGER NOT NULL,
                            SourceMessageId TEXT NOT NULL,
                            FromAddress TEXT NOT NULL,
                            Subject TEXT NOT NULL,
                            OriginalBodyPreview TEXT NOT NULL,
                            SourceReceivedAtUtc TEXT NULL,
                            CreatedAtUtc TEXT NOT NULL,
                            LlmMode TEXT NOT NULL,
                            IsAmbiguous INTEGER NOT NULL,
                            Status TEXT NOT NULL
                        );
                        """;
                    await createDraftSetsCommand.ExecuteNonQueryAsync();
                }

                await using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        """
                        INSERT INTO DraftSets (
                            SourceImapUid,
                            SourceMessageId,
                            FromAddress,
                            Subject,
                            OriginalBodyPreview,
                            SourceReceivedAtUtc,
                            CreatedAtUtc,
                            LlmMode,
                            IsAmbiguous,
                            Status
                        )
                        VALUES (1, 'message-1', 'sender@example.com', 'Subject', 'Preview', NULL, '2026-04-17T10:00:00.0000000Z', 'mock', 0, 'ACCEPTED');
                        """;
                    await insertCommand.ExecuteNonQueryAsync();
                }
            }

            var store = new SqliteDraftStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await store.InitializeAsync(CancellationToken.None);
            await store.InitializeAsync(CancellationToken.None);

            await using var verifyConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await verifyConnection.OpenAsync();

            await using (var pragmaCommand = verifyConnection.CreateCommand())
            {
                pragmaCommand.CommandText =
                    """
                    SELECT name FROM pragma_table_info('DraftSets')
                    WHERE name IN ('SelectedVariantId', 'ReviewedAtUtc', 'PushedToOutlookAtUtc', 'SourceReceivedAtUtc', 'AnalysisJson')
                    ORDER BY name;
                    """;

                var columnNames = new List<string>();
                await using var reader = await pragmaCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columnNames.Add(reader.GetString(0));
                }

                Assert.That(columnNames, Is.EquivalentTo(new[]
                {
                    "AnalysisJson",
                    "PushedToOutlookAtUtc",
                    "ReviewedAtUtc",
                    "SourceReceivedAtUtc",
                    "SelectedVariantId"
                }));
            }

            await using (var statusCommand = verifyConnection.CreateCommand())
            {
                statusCommand.CommandText = "SELECT Status FROM DraftSets WHERE SourceImapUid = 1;";
                var status = (string?)await statusCommand.ExecuteScalarAsync();
                Assert.That(status, Is.EqualTo(DraftSetStatuses.Approved));
            }
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
    public async Task Should_read_draft_sets_and_update_status_fields()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"email-copilot-draft-reads-{Guid.NewGuid():N}.db");

        try
        {
            var store = new SqliteDraftStore(
                Options.Create(new DatabaseOptions
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }));

            await store.InitializeAsync(CancellationToken.None);

            var olderId = await store.InsertAsync(
                new DraftSetRecord
                {
                    SourceImapUid = 901,
                    SourceMessageId = "message-901",
                    FromAddress = "older@example.com",
                    Subject = "Older draft",
                    OriginalBodyPreview = "Older preview",
                    SourceReceivedAtUtc = new DateTimeOffset(2026, 4, 15, 15, 0, 0, TimeSpan.Zero),
                    CreatedAtUtc = new DateTimeOffset(2026, 4, 16, 9, 0, 0, TimeSpan.Zero),
                    LlmMode = "mock",
                    IsAmbiguous = false,
                    Status = DraftSetStatuses.Pending,
                    AnalysisJson = "{\"asks\":[{\"text\":\"Can you send photos?\",\"askType\":\"QUESTION\",\"isOptional\":false}],\"decisionBranches\":[],\"statedDeadlines\":[],\"urgency\":\"LOW\"}",
                    Variants =
                    [
                        new DraftVariantRecord
                        {
                            SortOrder = 0,
                            ReplyShape = ReplyShapes.DirectAnswer,
                            ReplyShapeLabel = "Direct answer",
                            Body = "Older draft body",
                            ConfidenceScore = 0.61,
                            StyleSegmentUsed = "test",
                            GroundingWarning = "missed_ask:Can you send photos?"
                        }
                    ]
                },
                CancellationToken.None);

            var newerId = await store.InsertAsync(
                new DraftSetRecord
                {
                    SourceImapUid = 902,
                    SourceMessageId = "message-902",
                    FromAddress = "newer@example.com",
                    Subject = "Newer draft",
                    OriginalBodyPreview = "Newer preview",
                    SourceReceivedAtUtc = new DateTimeOffset(2026, 4, 17, 8, 15, 0, TimeSpan.Zero),
                    CreatedAtUtc = new DateTimeOffset(2026, 4, 17, 9, 0, 0, TimeSpan.Zero),
                    LlmMode = "mock",
                    IsAmbiguous = true,
                    Status = DraftSetStatuses.Pending,
                    AnalysisJson = "{\"asks\":[],\"decisionBranches\":[{\"summary\":\"Need confirmation\",\"viableReplyShapes\":[\"ACKNOWLEDGE\",\"CONFIRM_AND_REQUEST\"]}],\"statedDeadlines\":[],\"urgency\":\"LOW\"}",
                    Variants =
                    [
                        new DraftVariantRecord
                        {
                            SortOrder = 0,
                            ReplyShape = ReplyShapes.Acknowledge,
                            ReplyShapeLabel = "Acknowledge only",
                            Body = "Variant A",
                            ConfidenceScore = 0.55,
                            StyleSegmentUsed = "test"
                        },
                        new DraftVariantRecord
                        {
                            SortOrder = 1,
                            ReplyShape = ReplyShapes.ConfirmAndRequest,
                            ReplyShapeLabel = "Confirm and request",
                            Body = "Variant B",
                            ConfidenceScore = 0.82,
                            StyleSegmentUsed = "test",
                            GroundingWarning = "unverified_number"
                        }
                    ]
                },
                CancellationToken.None);

            var summaries = await store.GetDraftSetsAsync(DraftSetStatuses.Pending, 0, 10, CancellationToken.None);
            Assert.That(summaries, Has.Count.EqualTo(2));
            Assert.That(summaries[0].Id, Is.EqualTo(newerId));
            Assert.That(summaries[0].SourceReceivedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 17, 8, 15, 0, TimeSpan.Zero)));
            Assert.That(summaries[0].DraftCreatedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 17, 9, 0, 0, TimeSpan.Zero)));
            Assert.That(summaries[0].VariantCount, Is.EqualTo(2));
            Assert.That(summaries[0].TopConfidence, Is.EqualTo(0.82).Within(0.001));
            Assert.That(summaries[1].Id, Is.EqualTo(olderId));
            Assert.That(summaries[1].SourceReceivedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 15, 15, 0, 0, TimeSpan.Zero)));

            var detail = await store.GetDraftSetByIdAsync(newerId, CancellationToken.None);
            Assert.That(detail, Is.Not.Null);
            Assert.That(detail!.SourceMessageId, Is.EqualTo("message-902"));
            Assert.That(detail.SourceReceivedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 17, 8, 15, 0, TimeSpan.Zero)));
            Assert.That(detail.DraftCreatedAt, Is.EqualTo(new DateTimeOffset(2026, 4, 17, 9, 0, 0, TimeSpan.Zero)));
            Assert.That(detail.Analysis, Is.Not.Null);
            Assert.That(detail.Variants, Has.Count.EqualTo(2));
            Assert.That(detail.Variants[0].Shape, Is.EqualTo(ReplyShapes.Acknowledge));
            Assert.That(detail.Variants[1].Shape, Is.EqualTo(ReplyShapes.ConfirmAndRequest));
            Assert.That(detail.Variants[1].GroundingWarning, Is.EqualTo("unverified_number"));

            var selectedVariantId = detail.Variants[1].Id;
            var reviewedAt = new DateTimeOffset(2026, 4, 17, 10, 30, 0, TimeSpan.Zero);
            await store.UpdateDraftSetStatusAsync(
                newerId,
                DraftSetStatuses.Approved,
                selectedVariantId,
                reviewedAt,
                null,
                false,
                false,
                false,
                CancellationToken.None);

            var updatedDetail = await store.GetDraftSetByIdAsync(newerId, CancellationToken.None);
            Assert.That(updatedDetail, Is.Not.Null);
            Assert.That(updatedDetail!.Status, Is.EqualTo(DraftSetStatuses.Approved));
            Assert.That(updatedDetail.SelectedVariantId, Is.EqualTo(selectedVariantId));

            await using var verifyConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await verifyConnection.OpenAsync();
            await using var verifyCommand = verifyConnection.CreateCommand();
            verifyCommand.CommandText =
                """
                SELECT SelectedVariantId, ReviewedAtUtc, PushedToOutlookAtUtc
                FROM DraftSets
                WHERE Id = $id;
                """;
            verifyCommand.Parameters.AddWithValue("$id", newerId);

            await using var reader = await verifyCommand.ExecuteReaderAsync();
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetInt64(0), Is.EqualTo(selectedVariantId));
            Assert.That(reader.GetString(1), Is.EqualTo(reviewedAt.UtcDateTime.ToString("O")));
            Assert.That(reader.IsDBNull(2), Is.True);
            await reader.DisposeAsync();
            await verifyCommand.DisposeAsync();
            await verifyConnection.DisposeAsync();

            await store.UpdateDraftSetStatusAsync(
                newerId,
                DraftSetStatuses.PushedToOutlook,
                null,
                null,
                new DateTimeOffset(2026, 4, 17, 11, 0, 0, TimeSpan.Zero),
                true,
                true,
                false,
                CancellationToken.None);

            await using var clearedConnection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            await clearedConnection.OpenAsync();
            await using var clearedCommand = clearedConnection.CreateCommand();
            clearedCommand.CommandText =
                """
                SELECT SelectedVariantId, ReviewedAtUtc, PushedToOutlookAtUtc, Status
                FROM DraftSets
                WHERE Id = $id;
                """;
            clearedCommand.Parameters.AddWithValue("$id", newerId);

            await using var clearedReader = await clearedCommand.ExecuteReaderAsync();
            Assert.That(await clearedReader.ReadAsync(), Is.True);
            Assert.That(clearedReader.IsDBNull(0), Is.True);
            Assert.That(clearedReader.IsDBNull(1), Is.True);
            Assert.That(clearedReader.GetString(2), Is.EqualTo("2026-04-17T11:00:00.0000000Z"));
            Assert.That(clearedReader.GetString(3), Is.EqualTo(DraftSetStatuses.PushedToOutlook));
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
}
