namespace EmailCopilot.Worker;

public sealed class SkipQuotedLineStage : IStyleLineStage
{
    public StyleLineStageResult Evaluate(string trimmedLine, bool hasKeptContent)
    {
        return trimmedLine.StartsWith(">", StringComparison.Ordinal)
            ? StyleLineStageResult.Skip(nameof(SkipQuotedLineStage), trimmedLine)
            : StyleLineStageResult.Keep(nameof(SkipQuotedLineStage));
    }
}
