namespace EmailCopilot.Worker;

public sealed class StyleSentenceFilterPipeline
{
    private readonly IReadOnlyList<IStyleSentenceStage> _stages =
    [
        new RejectMetadataSentenceStage(),
        new RejectContaminatedMarkerSentenceStage(),
        new RejectInvalidStructureSentenceStage()
    ];

    public bool ShouldKeep(string sentence)
    {
        foreach (var stage in _stages)
        {
            if (stage.Evaluate(sentence).Matched)
            {
                return false;
            }
        }

        return true;
    }
}
