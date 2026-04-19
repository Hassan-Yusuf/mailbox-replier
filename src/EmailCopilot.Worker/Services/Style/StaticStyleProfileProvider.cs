using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class StaticStyleProfileProvider
{
    private readonly StyleProfileOptions _options;

    public StaticStyleProfileProvider(IOptions<StyleProfileOptions> options)
    {
        _options = options.Value;
    }

    public StaticStyleProfile GetProfile() => new(
        _options.Greeting.Trim(),
        _options.Closing.Trim(),
        _options.Tone.Trim());

    public StyleProfile GetFallbackProfile() => new(
        SegmentKey: "global-default",
        Greeting: _options.Greeting.Trim(),
        Closing: _options.Closing.Trim(),
        Tone: _options.Tone.Trim(),
        Signature: string.Empty,
        CommonPhrases: [],
        AverageSentenceLength: 14,
        GreetingUsageRate: 0.7,
        SignoffUsageRate: 0.35,
        QuestionEndingRate: 0.3,
        GratitudeUsageRate: 0.2,
        ContractionUsageRate: 0.4,
        FragmentUsageRate: 0.05,
        ExplicitNextStepRate: 0.25,
        TypicalSentenceCountMin: 1,
        TypicalSentenceCountMax: 2,
        FormalityScore: 0.5,
        SampleSize: 0,
        BuiltAtUtc: DateTimeOffset.UtcNow);
}
