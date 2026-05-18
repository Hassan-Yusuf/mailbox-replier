namespace EmailCopilot.Worker;

public sealed class DraftSetRecord
{
    public long Id { get; init; }

    public uint SourceImapUid { get; init; }

    public string SourceMessageId { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string OriginalBodyPreview { get; init; } = string.Empty;

    public DateTimeOffset SourceReceivedAtUtc { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public string LlmMode { get; init; } = string.Empty;

    public bool IsAmbiguous { get; init; }

    public string Status { get; init; } = DraftSetStatuses.Pending;

    public string? AnalysisJson { get; init; }

    public string? OriginalEmailBody { get; init; }

    public double? AggregateConfidenceScore { get; init; }

    public ConfidenceTier? ConfidenceTier { get; init; }

    public string? AssignedToUserId { get; init; }

    public string? OwnerUserId { get; init; }

    public IReadOnlyList<DraftVariantRecord> Variants { get; init; } = [];
}
