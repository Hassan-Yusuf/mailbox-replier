namespace EmailCopilot.Worker;

public sealed partial class SkipBoilerplateLineStage : IStyleLineStage
{
    public StyleLineStageResult Evaluate(string trimmedLine, bool hasKeptContent)
    {
        if (SeparatorLineRegex().IsMatch(trimmedLine) ||
            trimmedLine.StartsWith("Microsoft Outlook Web Access:", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("From:", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("To:", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("External email", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("CAUTION:", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("Get Outlook for", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("Sent from my", StringComparison.OrdinalIgnoreCase))
        {
            return StyleLineStageResult.Skip(nameof(SkipBoilerplateLineStage), trimmedLine);
        }

        return StyleLineStageResult.Keep(nameof(SkipBoilerplateLineStage));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[_=\-]{4,}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SeparatorLineRegex();
}
