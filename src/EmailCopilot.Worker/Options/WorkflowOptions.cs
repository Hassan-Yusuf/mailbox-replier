namespace EmailCopilot.Worker;

public sealed class WorkflowOptions
{
    public string Mode { get; set; } = WorkflowModes.ReviewBeforeSend;
}

public static class WorkflowModes
{
    public const string ReviewBeforeSend = "ReviewBeforeSend";
    public const string SuggestOnly = "SuggestOnly";

    public static bool IsKnown(string? mode) =>
        string.Equals(mode, ReviewBeforeSend, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mode, SuggestOnly, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? mode) =>
        string.Equals(mode, SuggestOnly, StringComparison.OrdinalIgnoreCase)
            ? SuggestOnly
            : ReviewBeforeSend;
}
