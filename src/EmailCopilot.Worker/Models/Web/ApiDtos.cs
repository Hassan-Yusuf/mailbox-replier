namespace EmailCopilot.Worker;

public sealed record DraftSetDto(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    IReadOnlyList<DraftVariantDto> Variants,
    string SourceMessageId,
    long? SelectedVariantId,
    EmailRequestAnalysis? Analysis);

public sealed record DraftVariantDto(
    long Id,
    string Shape,
    string ShapeLabel,
    double ConfidenceScore,
    string Body,
    string? GroundingWarning);

public sealed record DraftSetSummaryDto(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    int VariantCount,
    double TopConfidence,
    string? Urgency);

public sealed record SkippedEmailDto(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset ReceivedAt,
    string ReasonCode);

public sealed record RunRecordDto(
    long Id,
    DateTimeOffset StartedAt,
    int CandidatesEvaluated,
    int SkippedCount,
    bool DraftCreated,
    long? DraftId,
    IDictionary<string, int> SkipBuckets);

public sealed record ApproveDraftRequest(long VariantId);
