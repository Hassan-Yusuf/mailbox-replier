namespace EmailCopilot.Worker;

public sealed record RunRecord(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    int ExitCode,
    int CandidateWindowsScanned,
    int CandidatesEvaluated,
    int SkippedCount,
    IReadOnlyDictionary<string, int> SkipsByReasonCode,
    bool DraftCreated,
    long? DraftId,
    uint? DraftSourceImapUid,
    string? ErrorMessage)
{
    public TimeSpan Duration => FinishedAtUtc - StartedAtUtc;
}
