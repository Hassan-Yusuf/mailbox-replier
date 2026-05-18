namespace EmailCopilot.Worker;

public sealed class EmbeddingOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "text-embedding-3-small";
}
