namespace EmailCopilot.Worker;

public interface IEmailClassifier
{
    Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default);
}
