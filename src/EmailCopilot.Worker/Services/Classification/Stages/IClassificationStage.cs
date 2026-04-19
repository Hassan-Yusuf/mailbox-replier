namespace EmailCopilot.Worker;

public interface IClassificationStage
{
    Task<StageOutcome> EvaluateAsync(IncomingEmail email, CancellationToken cancellationToken = default);
}
