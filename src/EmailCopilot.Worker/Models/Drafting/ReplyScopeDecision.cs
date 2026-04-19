namespace EmailCopilot.Worker;

public sealed record ReplyScopeDecision(
    bool IsAllowed,
    string Reason);
