namespace EmailCopilot.Worker;

public interface IReplyDraftGenerator
{
    bool UseMock { get; }

    Task<string> GenerateDraftAsync(
        IncomingEmail email,
        StyleProfile styleProfile,
        IReadOnlyList<StyleExample> styleExamples,
        EmailRequestAnalysis analysis,
        string replyShape,
        string replyShapeLabel,
        IReadOnlyList<string> mustAddressAsks,
        CancellationToken cancellationToken);
}
