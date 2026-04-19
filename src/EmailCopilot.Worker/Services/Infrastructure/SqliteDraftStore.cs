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

        await EnsureDraftSetColumnExistsAsync(connection, "SelectedVariantId", "INTEGER NULL", cancellationToken);
        await EnsureDraftSetColumnExistsAsync(connection, "ReviewedAtUtc", "TEXT NULL", cancellationToken);
        await EnsureDraftSetColumnExistsAsync(connection, "PushedToOutlookAtUtc", "TEXT NULL", cancellationToken);
        await EnsureDraftSetColumnExistsAsync(connection, "SourceReceivedAtUtc", "TEXT NULL", cancellationToken);
        await EnsureDraftSetColumnExistsAsync(connection, "AnalysisJson", "TEXT NULL", cancellationToken);
        await EnsureDraftSetColumnExistsAsync(connection, "OriginalEmailBody", "TEXT NULL", cancellationToken);
        await EnsureDraftVariantColumnExistsAsync(connection, "GroundingWarning", "TEXT NULL", cancellationToken);

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
                OriginalEmailBody
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
                $originalEmailBody
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
                json_extract(ds.AnalysisJson, '$.urgency') AS Urgency
            FROM DraftSets ds
            LEFT JOIN DraftVariants dv ON dv.DraftSetId = ds.Id
            WHERE ($status IS NULL OR ds.Status = $status)
            GROUP BY ds.Id, ds.FromAddress, ds.Subject, ds.SourceReceivedAtUtc, ds.CreatedAtUtc, ds.Status, ds.AnalysisJson
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
            results.Add(new DraftSetSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                DateTimeOffset.Parse(reader.GetString(4)),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
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
            SELECT Id, FromAddress, Subject, SourceReceivedAtUtc, CreatedAtUtc, Status, SourceMessageId, SelectedVariantId, AnalysisJson
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

        await using var variantsCommand = connection.CreateCommand();
        variantsCommand.CommandText =
            """
            SELECT Id, Intent, IntentLabel, ConfidenceScore, Body, GroundingWarning
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
                variantsReader.IsDBNull(5) ? null : variantsReader.GetString(5)));
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
            variants);
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
        var setClauses = new List<string> { "Status = $status" };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
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

    private static async Task EnsureDraftSetColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM pragma_table_info('DraftSets')
            WHERE name = $columnName;
            """;
        existsCommand.Parameters.AddWithValue("$columnName", columnName);

        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE DraftSets ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDraftVariantColumnExistsAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM pragma_table_info('DraftVariants')
            WHERE name = $columnName;
            """;
        existsCommand.Parameters.AddWithValue("$columnName", columnName);

        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE DraftVariants ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
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
