namespace EmailCopilot.Worker;

public sealed class SkippedEmailRecord
{
    public long Id { get; init; }

    public uint SourceImapUid { get; init; }

    public string SourceMessageId { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string ReasonCode { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}
