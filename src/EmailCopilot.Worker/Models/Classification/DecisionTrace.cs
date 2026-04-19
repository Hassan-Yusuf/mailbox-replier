namespace EmailCopilot.Worker;

public sealed record DecisionTrace(IReadOnlyList<RuleEvaluation> Evaluations)
{
    public static DecisionTrace Empty { get; } = new([]);

    public static DecisionTrace Single(RuleEvaluation evaluation) => new([evaluation]);

    public DecisionTrace Append(RuleEvaluation evaluation) =>
        new(Evaluations.Concat([evaluation]).ToArray());

    public DecisionTrace Concat(DecisionTrace other) =>
        new(Evaluations.Concat(other.Evaluations).ToArray());
}
