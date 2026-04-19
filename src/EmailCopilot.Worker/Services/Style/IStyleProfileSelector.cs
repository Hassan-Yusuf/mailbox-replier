namespace EmailCopilot.Worker;

public interface IStyleProfileSelector
{
    Task<StyleProfileSelection> GetOrBuildProfileSelectionAsync(
        IncomingEmail email,
        CancellationToken cancellationToken);
}
