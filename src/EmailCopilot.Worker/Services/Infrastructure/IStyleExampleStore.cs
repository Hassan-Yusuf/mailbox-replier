namespace EmailCopilot.Worker;

public interface IStyleExampleStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task ReplaceBySegmentAsync(
        string segmentKey,
        IReadOnlyList<StyleExample> examples,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StyleExample>> GetBySegmentAsync(
        string segmentKey,
        CancellationToken cancellationToken);
}
