namespace EmailCopilot.Worker;

public sealed class RejectContaminatedMarkerSentenceStage : IStyleSentenceStage
{
    private static readonly string[] ContaminatedPhraseMarkers =
    [
        "impact on your team's projects",
        "is not detailed in my attached cv",
        "mail for windows",
        "get outlook for",
        "shared a file with you",
        "shared a folder",
        "shared the folder",
        "shared a video with you",
        "view in browser",
        "name is hassan yusuf",
        "your date of birth",
        "i am a 6 ft 4 athletic wing",
        "highlight tape",
        "game recordings"
    ];

    private static readonly string[] ContaminatedDomainMarkers =
    [
        "www.cargurus.co.uk",
        "cargurus",
        "autotrader",
        "gumtree",
        "ebay"
    ];

    public RuleEvaluation Evaluate(string sentence)
    {
        var matched =
            sentence.Contains("www.", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            sentence.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            ContaminatedPhraseMarkers.Any(marker => sentence.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            ContaminatedDomainMarkers.Any(marker => sentence.Contains(marker, StringComparison.OrdinalIgnoreCase));

        return new RuleEvaluation(
            nameof(RejectContaminatedMarkerSentenceStage),
            matched,
            "STYLE_REJECT_CONTAMINATED_MARKER",
            ClassificationDecisionSources.BuiltIn,
            matched ? sentence : null);
    }
}
