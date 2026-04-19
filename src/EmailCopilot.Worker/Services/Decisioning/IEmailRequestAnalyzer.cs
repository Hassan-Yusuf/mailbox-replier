namespace EmailCopilot.Worker;

public interface IEmailRequestAnalyzer
{
    Task<EmailRequestAnalysis> AnalyzeAsync(IncomingEmail email, CancellationToken cancellationToken);
}
