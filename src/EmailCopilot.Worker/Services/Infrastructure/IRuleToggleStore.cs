namespace EmailCopilot.Worker;

public interface IRuleToggleStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, bool>> LoadSnapshotAsync(CancellationToken cancellationToken);

    Task SetAsync(string ruleName, bool isEnabled, CancellationToken cancellationToken);
}
