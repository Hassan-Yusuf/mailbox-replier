namespace EmailCopilot.Worker;

public sealed record InboxScanRunResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    int CandidateWindowsScanned,
    int CandidatesEvaluated,
    int SkippedCount,
    IReadOnlyDictionary<string, int> SkipsByReasonCode,
    bool DraftCreated,
    int DraftCount,
    long? DraftId,
    uint? DraftSourceImapUid)
{
    public TimeSpan Duration => FinishedAtUtc - StartedAtUtc;
}
