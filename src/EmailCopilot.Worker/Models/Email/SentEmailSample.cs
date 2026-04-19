namespace EmailCopilot.Worker;

public sealed record SentEmailSample(
    string MessageId,
    string Subject,
    string RecipientAddress,
    string RecipientDomain,
    string BodyText,
    DateTimeOffset SentAtUtc);
