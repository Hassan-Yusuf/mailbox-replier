namespace EmailCopilot.Worker;

public sealed record RuleEvaluation(
    string RuleName,
    bool Matched,
    string ReasonCode,
    string DecisionSource,
    string? Detail = null);
