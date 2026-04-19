namespace EmailCopilot.Worker;

public sealed record StyleProfile(
    string SegmentKey,
    string Greeting,
    string Closing,
    string Tone,
    string Signature,
    IReadOnlyList<string> CommonPhrases,
    double AverageSentenceLength,
    double GreetingUsageRate,
    double SignoffUsageRate,
    double QuestionEndingRate,
    double GratitudeUsageRate,
    double ContractionUsageRate,
    double FragmentUsageRate,
    double ExplicitNextStepRate,
    int TypicalSentenceCountMin,
    int TypicalSentenceCountMax,
    double FormalityScore,
    int SampleSize,
    DateTimeOffset BuiltAtUtc);
