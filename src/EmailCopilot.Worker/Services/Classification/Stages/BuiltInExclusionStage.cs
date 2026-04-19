namespace EmailCopilot.Worker;

public sealed class BuiltInExclusionStage : IClassificationStage
{
    private readonly BuiltInExclusionClassifier _classifier;

    public BuiltInExclusionStage(BuiltInExclusionClassifier classifier)
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
