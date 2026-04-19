namespace EmailCopilot.Worker;

public sealed class ClassificationPipeline : IEmailClassifier
{
    private readonly IReadOnlyList<IClassificationStage> _stages;

    public ClassificationPipeline(
        BuiltInExclusionStage builtInStage,
        ConfiguredExclusionStage configuredStage)
    {
        _stages =
        [
            builtInStage,
            configuredStage
        ];
    }

    public async Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default)
    {
        var allEvaluations = new List<RuleEvaluation>();

        foreach (var stage in _stages)
        {
            var outcome = await stage.EvaluateAsync(email, cancellationToken);
            allEvaluations.AddRange(outcome.Trace.Evaluations);

            if (outcome.IsDefinitive && outcome.Result is not null)
            {
                return outcome.Result with
                {
                    Trace = new DecisionTrace(allEvaluations)
                };
            }
        }

        allEvaluations.Add(new RuleEvaluation(
            "PIPELINE:DEFAULT_REPLY",
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            "no classification stages returned a definitive skip"));

        return new ClassificationResult(
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            new DecisionTrace(allEvaluations));
    }
}
