namespace EmailCopilot.Worker;

public interface IDraftStore
{
    Task<long> InsertAsync(DraftSetRecord draftSet, CancellationToken cancellationToken);

    Task<long> InsertStrategyAsync(DraftStrategyRecord draftStrategy, CancellationToken cancellationToken);

    Task<long> InsertSkippedAsync(SkippedEmailRecord skippedEmail, CancellationToken cancellationToken);

    Task<IReadOnlyList<DraftSetSummary>> GetDraftSetsAsync(string? status, int skip, int take, CancellationToken cancellationToken);

    Task<DraftSetDetail?> GetDraftSetByIdAsync(long id, CancellationToken cancellationToken);

    Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SkippedEmailRecord>> GetSkippedEmailsAsync(int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<RunRecordListItem>> GetRunRecordsAsync(int skip, int take, CancellationToken cancellationToken);

    Task UpdateDraftSetStatusAsync(
        long id,
        string status,
        long? selectedVariantId,
        DateTimeOffset? reviewedAt,
        DateTimeOffset? pushedAt,
        bool clearSelectedVariantId,
        bool clearReviewedAt,
        bool clearPushedAt,
        CancellationToken cancellationToken);
}
