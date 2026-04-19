namespace EmailCopilot.Worker;

public sealed class DraftStrategyRecord
{
    public uint SourceImapUid { get; init; }

    public string SourceMessageId { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string EligibilityDecision { get; init; } = DraftEligibilityDecisions.SingleDraft;

    public string EligibilityReason { get; init; } = string.Empty;

    public double AmbiguityScore { get; init; }

    public IReadOnlyList<string> PlannedReplyShapes { get; init; } = [];

    public int VariantCount { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
