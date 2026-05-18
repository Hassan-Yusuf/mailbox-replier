using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class SqliteDraftStore : IDraftStore
{
    private static readonly JsonSerializerOptions StoreJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqliteDraftStore(IOptions<DatabaseOptions> options)
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
            CREATE TABLE IF NOT EXISTS DraftSets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceImapUid INTEGER NOT NULL,
                SourceMessageId TEXT NOT NULL,
                FromAddress TEXT NOT NULL,
                Subject TEXT NOT NULL,
                OriginalBodyPreview TEXT NOT NULL,
                SourceReceivedAtUtc TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LlmMode TEXT NOT NULL,
                IsAmbiguous INTEGER NOT NULL,
                Status TEXT NOT NULL,
                AnalysisJson TEXT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var variantsCommand = connection.CreateCommand();
        variantsCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS DraftVariants (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DraftSetId INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                Intent TEXT NOT NULL,
                IntentLabel TEXT NOT NULL,
                Body TEXT NOT NULL,
                ConfidenceScore REAL NOT NULL,
                StyleSegmentUsed TEXT NOT NULL,
                GroundingWarning TEXT NULL,
                CoverageWarning TEXT NULL,
                WasSelected INTEGER NOT NULL DEFAULT 0,
                WasEdited INTEGER NOT NULL DEFAULT 0,
                EditedBody TEXT NULL,
                FOREIGN KEY (DraftSetId) REFERENCES DraftSets (Id) ON DELETE CASCADE
            );
            """;

        await variantsCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var legacyCommand = connection.CreateCommand();
        legacyCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS DraftReplies (
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

        await legacyCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var skippedCommand = connection.CreateCommand();
        skippedCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SkippedEmails (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceImapUid INTEGER NOT NULL,
                SourceMessageId TEXT NOT NULL,
                FromAddress TEXT NOT NULL,
                Subject TEXT NOT NULL,
                ReasonCode TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """;

        await skippedCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var strategyCommand = connection.CreateCommand();
        strategyCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS DraftStrategies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceImapUid INTEGER NOT NULL,
                SourceMessageId TEXT NOT NULL,
                FromAddress TEXT NOT NULL,
                Subject TEXT NOT NULL,
                EligibilityDecision TEXT NOT NULL,
                EligibilityReason TEXT NOT NULL,
                AmbiguityScore REAL NOT NULL,
                ResolvedIntentsJson TEXT NOT NULL,
                VariantCount INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """;

        await strategyCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DraftSets_SourceImapUid
            ON DraftSets (SourceImapUid);
            """;

        await indexCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var variantsIndexCommand = connection.CreateCommand();
        variantsIndexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_DraftVariants_DraftSetId_SortOrder
            ON DraftVariants (DraftSetId, SortOrder);
            """;

        await variantsIndexCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var legacyIndexCommand = connection.CreateCommand();
        legacyIndexCommand.CommandText =
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_DraftReplies_SourceImapUid
            ON DraftReplies (SourceImapUid);
            """;

        await legacyIndexCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var skippedIndexCommand = connection.CreateCommand();
        skippedIndexCommand.CommandText =
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_SkippedEmails_SourceImapUid
            ON SkippedEmails (SourceImapUid);
            """;

        await skippedIndexCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var strategyIndexCommand = connection.CreateCommand();
        strategyIndexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_DraftStrategies_SourceImapUid_CreatedAtUtc
            ON DraftStrategies (SourceImapUid, CreatedAtUtc);
            """;

        await strategyIndexCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var auditCommand = connection.CreateCommand();
        auditCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS DraftAuditEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DraftSetId INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                EventAtUtc TEXT NOT NULL,
                ActorUserId TEXT NULL,
                PayloadJson TEXT NULL,
                FOREIGN KEY (DraftSetId) REFERENCES DraftSets (Id) ON DELETE CASCADE
            );
            """;

        await auditCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var auditIndexCommand = connection.CreateCommand();
        auditIndexCommand.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_DraftAuditEvents_DraftSetId_EventAtUtc
            ON DraftAuditEvents (DraftSetId, EventAtUtc);
            """;

        await auditIndexCommand.ExecuteNonQueryAsync(cancellationToken);

        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "SelectedVariantId", "INTEGER NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "ReviewedAtUtc", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "PushedToOutlookAtUtc", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "SourceReceivedAtUtc", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "AnalysisJson", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "OriginalEmailBody", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "AggregateConfidenceScore", "REAL NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "ConfidenceTier", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "DismissReason", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "DismissedAtUtc", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "AssignedToUserId", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftSets", "OwnerUserId", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftVariants", "GroundingWarning", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftVariants", "CoverageWarning", "TEXT NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftVariants", "EditDistance", "INTEGER NULL", cancellationToken);
        await SqliteMigrationHelpers.EnsureColumnAsync(connection, "DraftVariants", "EditedAtUtc", "TEXT NULL", cancellationToken);

        await using (var renameStatusCommand = connection.CreateCommand())
        {
            renameStatusCommand.CommandText =
                """
                UPDATE DraftSets
                SET Status = 'APPROVED'
                WHERE Status = 'ACCEPTED';
                """;

            await renameStatusCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var backfillSourceReceivedCommand = connection.CreateCommand())
        {
            backfillSourceReceivedCommand.CommandText =
                """
                UPDATE DraftSets
                SET SourceReceivedAtUtc = CreatedAtUtc
                WHERE SourceReceivedAtUtc IS NULL OR TRIM(SourceReceivedAtUtc) = '';
                """;

            await backfillSourceReceivedCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var backfillAggregateConfidenceCommand = connection.CreateCommand())
        {
            backfillAggregateConfidenceCommand.CommandText =
                """
                UPDATE DraftSets
                SET AggregateConfidenceScore = (
                        SELECT AVG(ConfidenceScore)
                        FROM DraftVariants
                        WHERE DraftVariants.DraftSetId = DraftSets.Id
                    ),
                    ConfidenceTier = (
                        SELECT CASE
                            WHEN AVG(ConfidenceScore) >= 0.75 THEN 'High'
                            WHEN AVG(ConfidenceScore) >= 0.55 THEN 'Medium'
                            ELSE 'Low'
                        END
                        FROM DraftVariants
                        WHERE DraftVariants.DraftSetId = DraftSets.Id
                    )
                WHERE AggregateConfidenceScore IS NULL
                  AND EXISTS (
                        SELECT 1 FROM DraftVariants
                        WHERE DraftVariants.DraftSetId = DraftSets.Id
                  );
                """;

            await backfillAggregateConfidenceCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await MigrateLegacyDraftRepliesAsync(connection, cancellationToken);
    }

    public async Task<HashSet<uint>> GetProcessedSourceImapUidsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourceImapUid FROM DraftSets
            UNION
            SELECT SourceImapUid FROM DraftReplies
            UNION
            SELECT SourceImapUid FROM SkippedEmails;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var processedUids = new HashSet<uint>();

        while (await reader.ReadAsync(cancellationToken))
        {
            processedUids.Add(Convert.ToUInt32(reader.GetInt64(0)));
        }

        return processedUids;
    }

    public async Task<long> InsertAsync(DraftSetRecord draftSet, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.CommandText =
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
                Status,
                AnalysisJson,
                OriginalEmailBody,
                AggregateConfidenceScore,
                ConfidenceTier,
                AssignedToUserId,
                OwnerUserId
            )
            VALUES (
                $sourceImapUid,
                $sourceMessageId,
                $fromAddress,
                $subject,
                $originalBodyPreview,
                $sourceReceivedAtUtc,
                $createdAtUtc,
                $llmMode,
                $isAmbiguous,
                $status,
                $analysisJson,
                $originalEmailBody,
                $aggregateConfidenceScore,
                $confidenceTier,
                $assignedToUserId,
                $ownerUserId
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$sourceImapUid", draftSet.SourceImapUid);
        command.Parameters.AddWithValue("$sourceMessageId", draftSet.SourceMessageId);
        command.Parameters.AddWithValue("$fromAddress", draftSet.FromAddress);
        command.Parameters.AddWithValue("$subject", draftSet.Subject);
        command.Parameters.AddWithValue("$originalBodyPreview", draftSet.OriginalBodyPreview);
        command.Parameters.AddWithValue("$sourceReceivedAtUtc", draftSet.SourceReceivedAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$createdAtUtc", draftSet.CreatedAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$llmMode", draftSet.LlmMode);
        command.Parameters.AddWithValue("$isAmbiguous", draftSet.IsAmbiguous ? 1 : 0);
        command.Parameters.AddWithValue("$status", draftSet.Status);
        command.Parameters.AddWithValue("$analysisJson", (object?)draftSet.AnalysisJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$originalEmailBody", (object?)draftSet.OriginalEmailBody ?? DBNull.Value);
        command.Parameters.AddWithValue("$aggregateConfidenceScore", (object?)draftSet.AggregateConfidenceScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$confidenceTier", draftSet.ConfidenceTier.HasValue ? draftSet.ConfidenceTier.Value.ToString() : (object)DBNull.Value);
        command.Parameters.AddWithValue("$assignedToUserId", (object?)draftSet.AssignedToUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$ownerUserId", (object?)draftSet.OwnerUserId ?? DBNull.Value);

        var insertedId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        foreach (var variant in draftSet.Variants)
        {
            await using var variantCommand = connection.CreateCommand();
            variantCommand.Transaction = sqliteTransaction;
            variantCommand.CommandText =
                """
                INSERT INTO DraftVariants (
                    DraftSetId,
                    SortOrder,
                    Intent,
                    IntentLabel,
                    Body,
                    ConfidenceScore,
                    StyleSegmentUsed,
                    GroundingWarning,
                    CoverageWarning,
                    WasSelected,
                    WasEdited,
                    EditedBody
                )
                VALUES (
                    $draftSetId,
                    $sortOrder,
                    $intent,
                    $intentLabel,
                    $body,
                    $confidenceScore,
                    $styleSegmentUsed,
                    $groundingWarning,
                    $coverageWarning,
                    $wasSelected,
                    $wasEdited,
                    $editedBody
                );
                """;

            variantCommand.Parameters.AddWithValue("$draftSetId", insertedId);
            variantCommand.Parameters.AddWithValue("$sortOrder", variant.SortOrder);
            variantCommand.Parameters.AddWithValue("$intent", variant.ReplyShape);
            variantCommand.Parameters.AddWithValue("$intentLabel", variant.ReplyShapeLabel);
            variantCommand.Parameters.AddWithValue("$body", variant.Body);
            variantCommand.Parameters.AddWithValue("$confidenceScore", variant.ConfidenceScore);
            variantCommand.Parameters.AddWithValue("$styleSegmentUsed", variant.StyleSegmentUsed);
            variantCommand.Parameters.AddWithValue("$groundingWarning", (object?)variant.GroundingWarning ?? DBNull.Value);
            variantCommand.Parameters.AddWithValue("$coverageWarning", (object?)variant.CoverageWarning ?? DBNull.Value);
            variantCommand.Parameters.AddWithValue("$wasSelected", variant.WasSelected ? 1 : 0);
            variantCommand.Parameters.AddWithValue("$wasEdited", variant.WasEdited ? 1 : 0);
            variantCommand.Parameters.AddWithValue("$editedBody", (object?)variant.EditedBody ?? DBNull.Value);

            await variantCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return insertedId;
    }

    public async Task<long> InsertSkippedAsync(SkippedEmailRecord skippedEmail, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SkippedEmails (
                SourceImapUid,
                SourceMessageId,
                FromAddress,
                Subject,
                ReasonCode,
                CreatedAtUtc
            )
            VALUES (
                $sourceImapUid,
                $sourceMessageId,
                $fromAddress,
                $subject,
                $reasonCode,
                $createdAtUtc
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$sourceImapUid", skippedEmail.SourceImapUid);
        command.Parameters.AddWithValue("$sourceMessageId", skippedEmail.SourceMessageId);
        command.Parameters.AddWithValue("$fromAddress", skippedEmail.FromAddress);
        command.Parameters.AddWithValue("$subject", skippedEmail.Subject);
        command.Parameters.AddWithValue("$reasonCode", skippedEmail.ReasonCode);
        command.Parameters.AddWithValue("$createdAtUtc", skippedEmail.CreatedAtUtc.UtcDateTime.ToString("O"));

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(insertedId);
    }

    public async Task<long> InsertStrategyAsync(DraftStrategyRecord draftStrategy, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO DraftStrategies (
                SourceImapUid,
                SourceMessageId,
                FromAddress,
                Subject,
                EligibilityDecision,
                EligibilityReason,
                AmbiguityScore,
                ResolvedIntentsJson,
                VariantCount,
                CreatedAtUtc
            )
            VALUES (
                $sourceImapUid,
                $sourceMessageId,
                $fromAddress,
                $subject,
                $eligibilityDecision,
                $eligibilityReason,
                $ambiguityScore,
                $resolvedIntentsJson,
                $variantCount,
                $createdAtUtc
            );

            SELECT last_insert_rowid();
            """;

        command.Parameters.AddWithValue("$sourceImapUid", draftStrategy.SourceImapUid);
        command.Parameters.AddWithValue("$sourceMessageId", draftStrategy.SourceMessageId);
        command.Parameters.AddWithValue("$fromAddress", draftStrategy.FromAddress);
        command.Parameters.AddWithValue("$subject", draftStrategy.Subject);
        command.Parameters.AddWithValue("$eligibilityDecision", draftStrategy.EligibilityDecision);
        command.Parameters.AddWithValue("$eligibilityReason", draftStrategy.EligibilityReason);
        command.Parameters.AddWithValue("$ambiguityScore", draftStrategy.AmbiguityScore);
        command.Parameters.AddWithValue("$resolvedIntentsJson", JsonSerializer.Serialize(draftStrategy.PlannedReplyShapes));
        command.Parameters.AddWithValue("$variantCount", draftStrategy.VariantCount);
        command.Parameters.AddWithValue("$createdAtUtc", draftStrategy.CreatedAtUtc.UtcDateTime.ToString("O"));

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(insertedId);
    }

    public async Task<IReadOnlyList<DraftSetSummary>> GetDraftSetsAsync(string? status, int skip, int take, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                ds.Id,
                ds.FromAddress,
                ds.Subject,
                ds.SourceReceivedAtUtc,
                ds.CreatedAtUtc,
                ds.Status,
                COUNT(dv.Id) AS VariantCount,
                COALESCE(MAX(dv.ConfidenceScore), 0),
                json_extract(ds.AnalysisJson, '$.urgency') AS Urgency,
                ds.AggregateConfidenceScore,
                ds.ConfidenceTier
            FROM DraftSets ds
            LEFT JOIN DraftVariants dv ON dv.DraftSetId = ds.Id
            WHERE ($status IS NULL OR ds.Status = $status)
            GROUP BY ds.Id, ds.FromAddress, ds.Subject, ds.SourceReceivedAtUtc, ds.CreatedAtUtc, ds.Status, ds.AnalysisJson, ds.AggregateConfidenceScore, ds.ConfidenceTier
            ORDER BY ds.SourceReceivedAtUtc DESC, ds.CreatedAtUtc DESC
            LIMIT $take OFFSET $skip;
            """;

        command.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status);
        command.Parameters.AddWithValue("$skip", skip);
        command.Parameters.AddWithValue("$take", take);

        var results = new List<DraftSetSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double? aggregate = reader.IsDBNull(9) ? null : reader.GetDouble(9);
            ConfidenceTier? tier = reader.IsDBNull(10)
                ? null
                : Enum.TryParse<ConfidenceTier>(reader.GetString(10), ignoreCase: true, out var parsed)
                    ? parsed
                    : null;

            results.Add(new DraftSetSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                aggregate,
                tier));
        }

        return results;
    }

    public async Task<DraftSetDetail?> GetDraftSetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var setCommand = connection.CreateCommand();
        setCommand.CommandText =
            """
            SELECT Id, FromAddress, Subject, SourceReceivedAtUtc, CreatedAtUtc, Status, SourceMessageId, SelectedVariantId, AnalysisJson, AggregateConfidenceScore, ConfidenceTier
            FROM DraftSets
            WHERE Id = $id;
            """;
        setCommand.Parameters.AddWithValue("$id", id);

        await using var setReader = await setCommand.ExecuteReaderAsync(cancellationToken);
        if (!await setReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var draftSetId = setReader.GetInt64(0);
        var fromAddress = setReader.GetString(1);
        var subject = setReader.GetString(2);
        var sourceReceivedAt = DateTimeOffset.Parse(setReader.GetString(3));
        var draftCreatedAt = DateTimeOffset.Parse(setReader.GetString(4));
        var draftSetStatus = setReader.GetString(5);
        var sourceMessageId = setReader.GetString(6);
        long? selectedVariantId = setReader.IsDBNull(7) ? null : setReader.GetInt64(7);
        var analysis = setReader.IsDBNull(8)
            ? null
            : JsonSerializer.Deserialize<EmailRequestAnalysis>(setReader.GetString(8), StoreJsonOptions);
        double? aggregateConfidence = setReader.IsDBNull(9) ? null : setReader.GetDouble(9);
        ConfidenceTier? confidenceTier = setReader.IsDBNull(10)
            ? null
            : Enum.TryParse<ConfidenceTier>(setReader.GetString(10), ignoreCase: true, out var parsedTier)
                ? parsedTier
                : null;

        await using var variantsCommand = connection.CreateCommand();
        variantsCommand.CommandText =
            """
            SELECT Id, Intent, IntentLabel, ConfidenceScore, Body, GroundingWarning, CoverageWarning,
                   WasSelected, WasEdited, EditedBody, EditDistance, EditedAtUtc
            FROM DraftVariants
            WHERE DraftSetId = $draftSetId
            ORDER BY SortOrder ASC;
            """;
        variantsCommand.Parameters.AddWithValue("$draftSetId", draftSetId);

        var variants = new List<DraftVariantDetail>();
        await using var variantsReader = await variantsCommand.ExecuteReaderAsync(cancellationToken);
        while (await variantsReader.ReadAsync(cancellationToken))
        {
            variants.Add(new DraftVariantDetail(
                variantsReader.GetInt64(0),
                variantsReader.GetString(1),
                variantsReader.GetString(2),
                variantsReader.GetDouble(3),
                variantsReader.GetString(4),
                variantsReader.IsDBNull(5) ? null : variantsReader.GetString(5),
                CoverageWarning: variantsReader.IsDBNull(6) ? null : variantsReader.GetString(6),
                WasSelected: variantsReader.GetInt64(7) != 0,
                WasEdited: variantsReader.GetInt64(8) != 0,
                EditedBody: variantsReader.IsDBNull(9) ? null : variantsReader.GetString(9),
                EditDistance: variantsReader.IsDBNull(10) ? null : (int)variantsReader.GetInt64(10),
                EditedAtUtc: variantsReader.IsDBNull(11) ? null : DateTimeOffset.Parse(variantsReader.GetString(11))));
        }

        return new DraftSetDetail(
            draftSetId,
            fromAddress,
            subject,
            sourceReceivedAt,
            draftCreatedAt,
            draftSetStatus,
            sourceMessageId,
            selectedVariantId,
            analysis,
            variants,
            aggregateConfidence,
            confidenceTier);
    }

    public async Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OriginalEmailBody FROM DraftSets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : (string)result;
    }

    public async Task<IReadOnlyList<SkippedEmailRecord>> GetSkippedEmailsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SourceImapUid, SourceMessageId, FromAddress, Subject, ReasonCode, CreatedAtUtc
            FROM SkippedEmails
            ORDER BY CreatedAtUtc DESC
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$skip", skip);
        command.Parameters.AddWithValue("$take", take);

        var results = new List<SkippedEmailRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SkippedEmailRecord
            {
                Id = reader.GetInt64(0),
                SourceImapUid = Convert.ToUInt32(reader.GetInt64(1)),
                SourceMessageId = reader.GetString(2),
                FromAddress = reader.GetString(3),
                Subject = reader.GetString(4),
                ReasonCode = reader.GetString(5),
                CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(6))
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<RunRecordListItem>> GetRunRecordsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                StartedAtUtc,
                FinishedAtUtc,
                ExitCode,
                CandidateWindowsScanned,
                CandidatesEvaluated,
                SkippedCount,
                SkipsByReasonCodeJson,
                DraftCreated,
                DraftId,
                DraftSourceImapUid,
                ErrorMessage
            FROM RunRecords
            ORDER BY StartedAtUtc DESC
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$skip", skip);
        command.Parameters.AddWithValue("$take", take);

        var results = new List<RunRecordListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var skipBucketsJson = reader.GetString(7);
            var skipBuckets = JsonSerializer.Deserialize<Dictionary<string, int>>(skipBucketsJson) ??
                              new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            results.Add(new RunRecordListItem(
                reader.GetInt64(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                skipBuckets,
                reader.GetInt32(8) == 1,
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.IsDBNull(10) ? null : Convert.ToUInt32(reader.GetInt64(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return results;
    }

    public async Task UpdateDraftSetStatusAsync(
        long id,
        string status,
        long? selectedVariantId,
        DateTimeOffset? reviewedAt,
        DateTimeOffset? pushedAt,
        bool clearSelectedVariantId,
        bool clearReviewedAt,
        bool clearPushedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using var currentStatusCommand = connection.CreateCommand();
        currentStatusCommand.Transaction = sqliteTransaction;
        currentStatusCommand.CommandText = "SELECT Status FROM DraftSets WHERE Id = $id;";
        currentStatusCommand.Parameters.AddWithValue("$id", id);
        var currentStatusObj = await currentStatusCommand.ExecuteScalarAsync(cancellationToken);

        if (currentStatusObj is null || currentStatusObj is DBNull)
        {
            throw new InvalidOperationException($"Draft set {id} does not exist.");
        }

        var currentStatus = (string)currentStatusObj;
        if (!DraftSetStatusTransitions.IsAllowed(currentStatus, status))
        {
            throw new InvalidOperationException(
                $"Status transition from '{currentStatus}' to '{status}' is not allowed for draft set {id}.");
        }

        var setClauses = new List<string> { "Status = $status" };

        await using var command = connection.CreateCommand();
        command.Transaction = sqliteTransaction;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);

        if (selectedVariantId is not null)
        {
            setClauses.Add("SelectedVariantId = $selectedVariantId");
            command.Parameters.AddWithValue("$selectedVariantId", selectedVariantId.Value);
        }
        else if (clearSelectedVariantId)
        {
            setClauses.Add("SelectedVariantId = NULL");
        }

        if (reviewedAt is not null)
        {
            setClauses.Add("ReviewedAtUtc = $reviewedAtUtc");
            command.Parameters.AddWithValue("$reviewedAtUtc", reviewedAt.Value.UtcDateTime.ToString("O"));
        }
        else if (clearReviewedAt)
        {
            setClauses.Add("ReviewedAtUtc = NULL");
        }

        if (pushedAt is not null)
        {
            setClauses.Add("PushedToOutlookAtUtc = $pushedAtUtc");
            command.Parameters.AddWithValue("$pushedAtUtc", pushedAt.Value.UtcDateTime.ToString("O"));
        }
        else if (clearPushedAt)
        {
            setClauses.Add("PushedToOutlookAtUtc = NULL");
        }

        command.CommandText =
            $"""
            UPDATE DraftSets
            SET {string.Join(", ", setClauses)}
            WHERE Id = $id;
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordVariantEditAsync(
        long variantId,
        string editedBody,
        int editDistance,
        DateTimeOffset editedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE DraftVariants
            SET WasEdited = 1,
                EditedBody = $editedBody,
                EditDistance = $editDistance,
                EditedAtUtc = $editedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", variantId);
        command.Parameters.AddWithValue("$editedBody", editedBody);
        command.Parameters.AddWithValue("$editDistance", editDistance);
        command.Parameters.AddWithValue("$editedAtUtc", editedAtUtc.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordVariantSelectionAsync(
        long variantId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE DraftVariants
            SET WasSelected = 1
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", variantId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordDismissalAsync(
        long id,
        string? reason,
        DateTimeOffset dismissedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE DraftSets
            SET DismissReason = $reason,
                DismissedAtUtc = $dismissedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : (object)reason!.Trim());
        command.Parameters.AddWithValue("$dismissedAtUtc", dismissedAtUtc.UtcDateTime.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAuditEventAsync(
        long draftSetId,
        string eventType,
        DateTimeOffset eventAtUtc,
        string? actorUserId,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO DraftAuditEvents (
                DraftSetId,
                EventType,
                EventAtUtc,
                ActorUserId,
                PayloadJson
            )
            VALUES (
                $draftSetId,
                $eventType,
                $eventAtUtc,
                $actorUserId,
                $payloadJson
            );
            """;
        command.Parameters.AddWithValue("$draftSetId", draftSetId);
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$eventAtUtc", eventAtUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$actorUserId", string.IsNullOrWhiteSpace(actorUserId) ? DBNull.Value : (object)actorUserId!.Trim());
        command.Parameters.AddWithValue("$payloadJson", string.IsNullOrWhiteSpace(payloadJson) ? DBNull.Value : (object)payloadJson!);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DraftAuditEventRecord>> GetAuditEventsAsync(
        long draftSetId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, DraftSetId, EventType, EventAtUtc, ActorUserId, PayloadJson
            FROM DraftAuditEvents
            WHERE DraftSetId = $draftSetId
            ORDER BY EventAtUtc ASC, Id ASC;
            """;
        command.Parameters.AddWithValue("$draftSetId", draftSetId);

        var results = new List<DraftAuditEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DraftAuditEventRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
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

    private static async Task MigrateLegacyDraftRepliesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sqliteTransaction = (SqliteTransaction)transaction;

        await using (var migrateSetsCommand = connection.CreateCommand())
        {
            migrateSetsCommand.Transaction = sqliteTransaction;
            migrateSetsCommand.CommandText =
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
                    Status,
                    AnalysisJson
                )
                SELECT
                    legacy.SourceImapUid,
                    legacy.SourceMessageId,
                    legacy.FromAddress,
                    legacy.Subject,
                    legacy.OriginalBodyPreview,
                    legacy.CreatedAtUtc,
                    legacy.CreatedAtUtc,
                    legacy.LlmMode,
                    0,
                    'PENDING',
                    NULL
                FROM DraftReplies AS legacy
                LEFT JOIN DraftSets AS current
                    ON current.SourceImapUid = legacy.SourceImapUid
                WHERE current.Id IS NULL;
                """;

            await migrateSetsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var migrateVariantsCommand = connection.CreateCommand())
        {
            migrateVariantsCommand.Transaction = sqliteTransaction;
            migrateVariantsCommand.CommandText =
                """
                INSERT INTO DraftVariants (
                    DraftSetId,
                    SortOrder,
                    Intent,
                    IntentLabel,
                    Body,
                    ConfidenceScore,
                    StyleSegmentUsed,
                    GroundingWarning,
                    CoverageWarning,
                    WasSelected,
                    WasEdited,
                    EditedBody
                )
                SELECT
                    current.Id,
                    0,
                    'GENERAL_REPLY',
                    'General reply',
                    legacy.DraftText,
                    0.50,
                    'legacy-history',
                    NULL,
                    NULL,
                    0,
                    0,
                    NULL
                FROM DraftReplies AS legacy
                INNER JOIN DraftSets AS current
                    ON current.SourceImapUid = legacy.SourceImapUid
                LEFT JOIN DraftVariants AS variants
                    ON variants.DraftSetId = current.Id
                    AND variants.SortOrder = 0
                WHERE variants.Id IS NULL;
                """;

            await migrateVariantsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
