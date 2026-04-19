namespace EmailCopilot.Worker;

public sealed record StyleExample(
    string SegmentKey,
    string ExampleBody,
    string SubjectHint,
    DateTimeOffset SentAtUtc);
