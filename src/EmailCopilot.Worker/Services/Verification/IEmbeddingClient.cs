namespace EmailCopilot.Worker;

public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}
