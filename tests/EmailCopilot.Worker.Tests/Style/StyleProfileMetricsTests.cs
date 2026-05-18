namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleProfileMetricsTests
{
    private readonly StyleExtractor _extractor = new(new StyleAuthoredBodyPipeline(), new StyleSentenceFilterPipeline());

    [Test]
    public void BuildProfile_should_capture_behavioral_rates_from_sent_samples()
    {
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                I can do Sunday afternoon if that still works. Does 2pm work for you?

                Thanks,
                Hassan
                """),
            CreateSample("""
                Hi Sam,

                I'll send it over shortly once I finish the notes from today.
                """),
            CreateSample("""
                Can you confirm the start date and whether there is anything to prepare first?
                """),
            CreateSample("""
                Thanks, that works for me and I can make that time.
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.SampleSize, Is.EqualTo(4));
        Assert.That(profile.GreetingUsageRate, Is.EqualTo(0.5).Within(0.001));
        Assert.That(profile.SignoffUsageRate, Is.EqualTo(0.5).Within(0.001));
        Assert.That(profile.QuestionEndingRate, Is.EqualTo(0.5).Within(0.001));
        Assert.That(profile.GratitudeUsageRate, Is.EqualTo(0.0).Within(0.001));
        Assert.That(profile.ContractionUsageRate, Is.GreaterThan(0));
        Assert.That(profile.ExplicitNextStepRate, Is.GreaterThan(0));
        Assert.That(profile.TypicalSentenceCountMin, Is.GreaterThanOrEqualTo(1));
        Assert.That(profile.TypicalSentenceCountMax, Is.GreaterThanOrEqualTo(profile.TypicalSentenceCountMin));
        Assert.That(profile.FormalityScore, Is.InRange(0.0, 1.0));
    }

    [Test]
    public void BuildProfile_should_fall_back_when_no_samples_are_usable()
    {
        var fallback = CreateFallbackProfile();

        var profile = _extractor.BuildProfile("empty", [], fallback);

        Assert.That(profile, Is.EqualTo(fallback));
    }

    [Test]
    public void BuildProfile_should_produce_sane_sentence_count_for_heavily_newline_formatted_email()
    {
        // An email body with many blank lines between short fragments should not produce
        // a sentence count in the hundreds. The sentence splitter must not treat bare
        // newlines as sentence boundaries (regression for focusforce.com anomaly where
        // TypicalSentenceCountMin/Max reached 170/294).
        var manyNewlines = string.Join(
            "\n\n",
            Enumerable.Range(1, 40).Select(i => $"Line {i} of content here."));

        var samples = new[] { CreateSample(manyNewlines) };
        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.TypicalSentenceCountMax, Is.LessThanOrEqualTo(50),
            "Sentence count should reflect punctuation-bounded sentences, not raw newline count.");
    }

    private static SentEmailSample CreateSample(string body) =>
        new(
            Guid.NewGuid().ToString("N"),
            "Re: Test",
            "sam@example.com",
            "example.com",
            body,
            DateTimeOffset.UtcNow);

    private static StyleProfile CreateFallbackProfile() =>
        new(
            "fallback",
            "Hi",
            "Thanks",
            "friendly and concise",
            string.Empty,
            [],
            10,
            0.5,
            0.2,
            0.3,
            0.02,
            0.2,
            0.4,
            0.1,
            0.25,
            1,
            2,
            0.5,
            1,
            DateTimeOffset.UtcNow);
}
