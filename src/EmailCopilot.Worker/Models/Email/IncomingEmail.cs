namespace EmailCopilot.Worker;

public sealed record IncomingEmail(
    uint ImapUid,
    string MessageId,
    EmailAddress From,
    string Subject,
    string BodyText,
    DateTimeOffset ReceivedAtUtc,
    bool HasListUnsubscribeHeader,
    bool HasListIdHeader,
    string Precedence,
    bool IsAutoSubmitted)
{
    public string FromAddress => From.Address;
    public string FromDisplayName => From.DisplayName;
}
