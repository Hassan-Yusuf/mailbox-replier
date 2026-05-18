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
    EmailRequestAnalysis? Analysis,
    double? AggregateConfidence,
    string? Tier);

public sealed record DraftVariantDto(
    long Id,
    string Shape,
    string ShapeLabel,
    double ConfidenceScore,
    string Body,
    string? GroundingWarning,
    string? CoverageWarning = null,
    bool WasSelected = false,
    bool WasEdited = false,
    string? EditedBody = null,
    int? EditDistance = null,
    DateTimeOffset? EditedAtUtc = null);

public sealed record DraftSetSummaryDto(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    int VariantCount,
    double TopConfidence,
    string? Urgency,
    double? AggregateConfidence,
    string? Tier);

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

public sealed record ApproveDraftRequest(long VariantId, string? EditedBody = null);

public sealed record DismissDraftRequest(string? Reason = null);

public sealed record PolicyRuleDto(
    string RuleId,
    string DisplayName,
    string Description,
    string Category,
    bool DefaultEnabled,
    bool IsUserConfigurable,
    bool CurrentlyEnabled);

public sealed record SetPolicyRuleRequest(bool Enabled);

public sealed record WorkflowConfigDto(string Mode);

public sealed record DraftAuditEventDto(
    long Id,
    string EventType,
    DateTimeOffset EventAtUtc,
    string? ActorUserId,
    string? PayloadJson);
