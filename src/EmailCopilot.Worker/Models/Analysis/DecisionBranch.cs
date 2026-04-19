namespace EmailCopilot.Worker;

public sealed record DecisionBranch(
    string Summary,
    IReadOnlyList<string> ViableReplyShapes);
