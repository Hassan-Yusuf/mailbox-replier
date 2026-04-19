namespace EmailCopilot.Worker;

public sealed record EmailAsk(
    string Text,
    string AskType,
    bool IsOptional);
