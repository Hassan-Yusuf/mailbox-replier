namespace EmailCopilot.Worker;

public sealed partial class StopAtHeaderLineStage : IStyleLineStage
{
    public StyleLineStageResult Evaluate(string trimmedLine, bool hasKeptContent)
    {
        if (hasKeptContent && HeaderLineRegex().IsMatch(trimmedLine))
        {
            return StyleLineStageResult.Stop(nameof(StopAtHeaderLineStage), trimmedLine);
        }

        return StyleLineStageResult.Keep(nameof(StopAtHeaderLineStage));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(from|to|cc|bcc|sent|subject|date)\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex HeaderLineRegex();
}
