namespace EmailCopilot.Worker;

public sealed class LlmOptions
{
    public bool UseMock { get; set; } = true;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
}
