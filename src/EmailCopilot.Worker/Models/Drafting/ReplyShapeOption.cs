namespace EmailCopilot.Worker;

public sealed record ReplyShapeOption(
    string Shape,
    string Label,
    double ConfidenceScore,
    IReadOnlyList<string>? MustAddressAsks = null);
