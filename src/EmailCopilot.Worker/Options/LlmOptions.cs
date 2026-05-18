namespace EmailCopilot.Worker;

public sealed class LlmOptions
{
    public bool UseMock { get; set; } = true;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public AvoidPhrasesMode AvoidPhrasesMode { get; set; } = AvoidPhrasesMode.StaticFallback;

    public AvoidFilterOptions AvoidFilter { get; set; } = new();

    public SimilarExamplesOptions SimilarExamples { get; set; } = new();
}

public sealed class SimilarExamplesOptions
{
    public bool Enabled { get; set; } = false;

    public int TopN { get; set; } = 4;
}

public enum AvoidPhrasesMode
{
    Disabled,
    StaticFallback,
    WithExtraction
}

public sealed class AvoidFilterOptions
{
    public bool Enabled { get; set; } = false;

    public double CosineThreshold { get; set; } = 0.82;
}
