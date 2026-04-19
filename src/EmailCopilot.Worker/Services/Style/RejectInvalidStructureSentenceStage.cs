namespace EmailCopilot.Worker;

public sealed partial class RejectInvalidStructureSentenceStage : IStyleSentenceStage
{
    public RuleEvaluation Evaluate(string sentence)
    {
        var matched =
            string.IsNullOrWhiteSpace(sentence) ||
            !char.IsUpper(sentence[0]) ||
            CountWords(sentence) < 4 ||
            CountWords(sentence) > 35 ||
            DatePatternRegex().IsMatch(sentence) ||
            FullNamePatternRegex().IsMatch(sentence) ||
            SurveyMarkerRegex().IsMatch(sentence) ||
            LabelValuePatternRegex().IsMatch(sentence) ||
            ContainsAnyIgnoreCase(
                sentence,
                [
                    "shared a file",
                    "shared a folder",
                    "shared the folder",
                    "shared a video",
                    "has invited you",
                    "clicked a link",
                    "view in browser",
                    "name is",
                    "date of birth",
                    "highlight tape",
                    "game recordings",
                    "i am a 6 ft"
                ]);

        return new RuleEvaluation(
            nameof(RejectInvalidStructureSentenceStage),
            matched,
            "STYLE_REJECT_INVALID_STRUCTURE",
            ClassificationDecisionSources.BuiltIn,
            matched ? sentence : null);
    }

    private static int CountWords(string value) => WordRegex().Matches(value).Count;

    private static bool ContainsAnyIgnoreCase(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    [System.Text.RegularExpressions.GeneratedRegex(@"\b[\p{L}\p{N}']+\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex WordRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b\d{2}/\d{2}/\d{4}\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex DatePatternRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b[A-Z][a-z]+ [A-Z][a-z]+ [A-Z][a-z]+\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex FullNamePatternRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-e]\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SurveyMarkerRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[A-Za-z][A-Za-z ]{0,20}:\s+\S+", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex LabelValuePatternRegex();
}
