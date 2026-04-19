namespace EmailCopilot.Worker;

public sealed record ClassificationResult(
    bool RequiresReply,
    string ReasonCode,
    string DecisionSource = ClassificationDecisionSources.BuiltIn,
    DecisionTrace? Trace = null)
{
    public DecisionTrace Trace { get; init; } = Trace ?? DecisionTrace.Empty;
}
