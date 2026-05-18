namespace EmailCopilot.Worker;

public static class DraftAuditEventTypes
{
    public const string Approved = "APPROVED";
    public const string Dismissed = "DISMISSED";
    public const string Pushed = "PUSHED";
}

public sealed record DraftAuditEventRecord(
    long Id,
    long DraftSetId,
    string EventType,
    DateTimeOffset EventAtUtc,
    string? ActorUserId,
    string? PayloadJson);
