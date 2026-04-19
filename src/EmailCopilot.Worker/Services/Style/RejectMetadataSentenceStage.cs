namespace EmailCopilot.Worker;

public sealed class RejectMetadataSentenceStage : IStyleSentenceStage
{
    public RuleEvaluation Evaluate(string sentence)
    {
        var matched =
            sentence.Contains('@') ||
            sentence.Contains('<') ||
            sentence.Contains('>') ||
            sentence.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            sentence.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        return new RuleEvaluation(
            nameof(RejectMetadataSentenceStage),
            matched,
            "STYLE_REJECT_METADATA",
            ClassificationDecisionSources.BuiltIn,
            matched ? sentence : null);
    }
}
