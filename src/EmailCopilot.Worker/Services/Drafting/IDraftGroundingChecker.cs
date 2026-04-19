namespace EmailCopilot.Worker;

public interface IDraftGroundingChecker
{
    string? Check(IncomingEmail email, string draftText, EmailRequestAnalysis analysis, IReadOnlyList<string> mustAddressAsks);
}
