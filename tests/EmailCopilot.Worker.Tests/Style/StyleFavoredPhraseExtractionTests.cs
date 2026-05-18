namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleFavoredPhraseExtractionTests
{
    private readonly StyleExtractor _extractor = new(new StyleAuthoredBodyPipeline(), new StyleSentenceFilterPipeline());

    [Test]
    public void Bucket_acknowledge_opener_when_sample_starts_with_got_it()
    {
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Got it, that works for me and I can make it.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.Acknowledge), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.Acknowledge],
            Has.Some.Contains("Got it"));
    }

    [Test]
    public void Bucket_direct_answer_when_sample_starts_with_can_do()
    {
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Can do Sunday afternoon if that still works for you.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.DirectAnswer), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.DirectAnswer],
            Has.Some.Contains("Can do"));
    }

    [Test]
    public void Bucket_decline_when_sample_starts_with_no_sorry()
    {
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Sorry, I cannot make Tuesday but Thursday would work for me.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.Decline), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.Decline],
            Has.Some.Contains("Sorry"));
    }

    [Test]
    public void Bucket_acknowledge_and_ask_when_acknowledge_opener_and_trailing_question()
    {
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Got it, are you happy to confirm by tomorrow morning?

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.AcknowledgeAndAsk), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.AcknowledgeAndAsk],
            Has.Some.Contains("?"));
        // A combined-cue phrase should not also be filed as plain ACKNOWLEDGE.
        Assert.That(
            profile.FavoredPhrases.ContainsKey(ReplyShapes.Acknowledge) &&
            profile.FavoredPhrases[ReplyShapes.Acknowledge].Any(p => p.Contains("?")),
            Is.False);
    }

    [Test]
    public void Skip_phrase_with_recipient_name_prefix()
    {
        // "Sarah, that's confirmed..." matches FavoredNamePrefixRegex (^[A-Z][a-z]+,)
        // and contains a ConfirmVerbRegex match ("confirmed"). Without the name-prefix
        // skip, it would be bucketed into CONFIRM_AND_CLOSE. With the skip, nothing
        // survives, so FavoredPhrases is null.
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Sarah, that's confirmed for tomorrow morning at ten.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Null);
    }

    [Test]
    public void Skip_phrase_below_min_count_when_sample_size_large()
    {
        // 10 samples = usedSampleCount >= 10 -> favoredMinCount = 2.
        // The "Got it" phrase appears in only one sample -> Count = 1 -> dropped.
        var samples = new List<SentEmailSample>();
        samples.Add(CreateSample("""
            Hi Sam,

            Got it, that works for me and I can make it.

            Thanks,
            Hassan
            """));
        for (var i = 0; i < 9; i++)
        {
            samples.Add(CreateSample($"""
                Hi Sam,

                Filler sentence number {i} that has enough words to count.

                Thanks,
                Hassan
                """));
        }

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.SampleSize, Is.GreaterThanOrEqualTo(10));
        Assert.That(
            profile.FavoredPhrases is null ||
            !profile.FavoredPhrases.ContainsKey(ReplyShapes.Acknowledge),
            Is.True,
            "Phrase appearing only once across >=10 samples should be dropped.");
    }

    [Test]
    public void Keep_phrase_with_count_one_when_sample_size_small()
    {
        // 1 sample -> favoredMinCount = 1, so single-occurrence phrase is retained.
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Got it, that works for me and I can make it.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.SampleSize, Is.LessThan(10));
        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.Acknowledge), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.Acknowledge], Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Cap_at_three_per_shape()
    {
        // Five distinct ACKNOWLEDGE phrases, each appearing twice (Count = 2)
        // so they pass the >=2 threshold at usedSampleCount >= 10. Cap should clamp to 3.
        var ackPhrases = new[]
        {
            "Got it, that all works for me and I can make it.",
            "Got it, all sorted on my side for next Tuesday afternoon.",
            "Got it, happy to go ahead with that plan from here.",
            "Got it, nothing extra needed from me on the brief.",
            "Got it, no further changes from my side at this point."
        };

        var samples = new List<SentEmailSample>();
        foreach (var phrase in ackPhrases)
        {
            // Two copies of each phrase -> Count = 2 -> survives favoredMinCount=2.
            samples.Add(CreateSample($"""
                Hi Sam,

                {phrase}

                Thanks,
                Hassan
                """));
            samples.Add(CreateSample($"""
                Hi Sam,

                {phrase}

                Thanks,
                Hassan
                """));
        }

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.SampleSize, Is.GreaterThanOrEqualTo(10));
        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.Acknowledge), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.Acknowledge], Has.Count.EqualTo(3));
    }

    [Test]
    public void Combined_acknowledge_and_ask_cue_takes_precedence_over_plain_acknowledge_opener()
    {
        // Two phrases sharing the same opener: one ends with '?', one does not.
        // The one ending in '?' must go to ACKNOWLEDGE_AND_ASK, not ACKNOWLEDGE.
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Got it, are you happy to confirm by tomorrow morning?

                Thanks,
                Hassan
                """),
            CreateSample("""
                Hi Sam,

                Got it, that works for me and I can make it.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(profile.FavoredPhrases, Is.Not.Null);
        Assert.That(profile.FavoredPhrases!.ContainsKey(ReplyShapes.AcknowledgeAndAsk), Is.True);
        Assert.That(profile.FavoredPhrases[ReplyShapes.AcknowledgeAndAsk],
            Has.Some.Contains("?"));
        if (profile.FavoredPhrases.ContainsKey(ReplyShapes.Acknowledge))
        {
            Assert.That(
                profile.FavoredPhrases[ReplyShapes.Acknowledge].All(p => !p.Contains("?")),
                Is.True);
        }
    }

    [Test]
    public void Skip_promotional_phrase_via_LooksLikePromotional_gate()
    {
        // Phrase contains "book now" (PromotionalTravelRegex match) and would otherwise
        // be classified into DIRECT_ANSWER ("Yes, ..."). The promotional gate must drop it.
        var samples = new[]
        {
            CreateSample("""
                Hi Sam,

                Yes, please book now and confirm by tomorrow morning.

                Thanks,
                Hassan
                """)
        };

        var profile = _extractor.BuildProfile("test", samples, CreateFallbackProfile());

        Assert.That(
            profile.FavoredPhrases is null ||
            !profile.FavoredPhrases.ContainsKey(ReplyShapes.DirectAnswer),
            Is.True,
            "Promotional phrase should be dropped before bucketing.");
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
