namespace EmailCopilot.Worker;

public sealed record StageOutcome(
    bool IsDefinitive,
    ClassificationResult? Result,
    DecisionTrace Trace)
{
    public static StageOutcome Continue(DecisionTrace trace) => new(false, null, trace);

    public static StageOutcome Definitive(ClassificationResult result) => new(true, result, result.Trace);
}
