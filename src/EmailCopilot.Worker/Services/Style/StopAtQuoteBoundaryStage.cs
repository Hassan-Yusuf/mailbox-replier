namespace EmailCopilot.Worker;

public sealed partial class StopAtQuoteBoundaryStage : IStyleLineStage
{
    public StyleLineStageResult Evaluate(string trimmedLine, bool hasKeptContent)
    {
        if (hasKeptContent && QuoteBoundaryRegex().IsMatch(trimmedLine))
        {
            return StyleLineStageResult.Stop(nameof(StopAtQuoteBoundaryStage), trimmedLine);
        }

        return StyleLineStageResult.Keep(nameof(StopAtQuoteBoundaryStage));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(on .+ wrote:|-{2,}\s*original message\s*-{2,}|-{2,}\s*forwarded message\s*-{2,}|begin forwarded message:)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex QuoteBoundaryRegex();
}
