namespace EmailCopilot.Worker;

public sealed record EmailRequestAnalysis(
    IReadOnlyList<EmailAsk> Asks,
    IReadOnlyList<DecisionBranch> DecisionBranches,
    IReadOnlyList<string> StatedDeadlines,
    string Urgency,
    bool RequiresPersonalConfirmation)
{
    public static EmailRequestAnalysis Empty { get; } = new([], [], [], UrgencyLevels.Unknown, false);
}
