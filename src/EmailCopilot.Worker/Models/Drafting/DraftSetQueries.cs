namespace EmailCopilot.Worker;

public sealed record DraftSetSummary(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    int VariantCount,
    double TopConfidence,
    string? Urgency);

public sealed record DraftVariantDetail(
    long Id,
    string Shape,
    string ShapeLabel,
    double ConfidenceScore,
    string Body,
    string? GroundingWarning);

public sealed record DraftSetDetail(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    string SourceMessageId,
    long? SelectedVariantId,
    EmailRequestAnalysis? Analysis,
    IReadOnlyList<DraftVariantDetail> Variants);
