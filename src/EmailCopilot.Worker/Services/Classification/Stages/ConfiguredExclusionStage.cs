namespace EmailCopilot.Worker;

public sealed class ConfiguredExclusionStage : IClassificationStage
{
    private readonly ConfiguredExclusionClassifier _classifier;

    public ConfiguredExclusionStage(ConfiguredExclusionClassifier classifier)
    {
        _classifier = classifier;
    }

    public async Task<StageOutcome> EvaluateAsync(IncomingEmail email, CancellationToken cancellationToken = default)
    {
        var result = await _classifier.ClassifyAsync(email, cancellationToken);

        if (!result.RequiresReply)
        {
            return StageOutcome.Definitive(result);
        }

        return StageOutcome.Continue(result.Trace);
    }
}
