namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleExtractorExclamationRateTests
{
    private readonly StyleExtractor _extractor = new(new StyleAuthoredBodyPipeline(), new StyleSentenceFilterPipeline());

    [Test]
    public void Rate_is_zero_when_no_sample_ends_with_exclamation()
    {
        var samples = new[]
        {
            CreateSample("Hi Sam,\n\nThat works for me and I will see you then.\n\nThanks,\nHassan"),
            CreateSample("Hi Sam,\n\nI will send the file over to you shortly.\n\nThanks,\nHassan")
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.ExclamationUsageRate, Is.EqualTo(0.0));
    }

    [Test]
    public void Rate_reflects_share_of_samples_ending_with_exclamation()
    {
        var samples = new[]
        {
            CreateSample("Hi Sam,\n\nThat works for me and I will see you then!\n\nThanks,\nHassan"),
            CreateSample("Hi Sam,\n\nThat works for me and I will see you then!\n\nThanks,\nHassan"),
            CreateSample("Hi Sam,\n\nI will send the file over to you shortly.\n\nThanks,\nHassan"),
            CreateSample("Hi Sam,\n\nI will send the file over to you shortly.\n\nThanks,\nHassan")
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.ExclamationUsageRate, Is.EqualTo(0.5).Within(0.001));
    }

    [Test]
    public void Rate_falls_back_to_fallback_profile_when_no_usable_samples()
    {
        var fallback = CreateFallbackProfile() with { ExclamationUsageRate = 0.42 };

        var profile = _extractor.BuildProfile("test", Array.Empty<SentEmailSample>(), fallback);

        Assert.That(profile.ExclamationUsageRate, Is.EqualTo(0.42));
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
