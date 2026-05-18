namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorAvoidPhrasesTests
{
    private const string AvoidHeader = "The user's writing style avoids phrases like:";

    [Test]
    public void Prompt_contains_avoid_section_when_mode_is_StaticFallback()
    {
        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.StaticFallback, learnedPhrases: null);

        Assert.That(prompt, Does.Contain(AvoidHeader));
        Assert.That(prompt, Does.Contain("I hope this email finds you well"));
    }

    [Test]
    public void Prompt_does_not_contain_avoid_section_when_mode_is_Disabled()
    {
        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.Disabled, learnedPhrases: null);

        Assert.That(prompt, Does.Not.Contain(AvoidHeader));
        Assert.That(prompt, Does.Not.Contain("I hope this email finds you well"));
    }

    [Test]
    public void Avoid_section_is_capped_at_seven_phrases()
    {
        var manyPhrases = Enumerable.Range(1, 12)
            .Select(i => $"unique-learned-phrase-{i}")
            .ToArray();

        var learned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ReplyShapes.DirectAnswer] = manyPhrases
        };

        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.StaticFallback, learned);

        var bulletCount = manyPhrases.Count(p => prompt.Contains($"\"{p}\""));
        Assert.That(bulletCount, Is.EqualTo(7));
        Assert.That(prompt, Does.Contain("unique-learned-phrase-1"));
        Assert.That(prompt, Does.Contain("unique-learned-phrase-7"));
        Assert.That(prompt, Does.Not.Contain("unique-learned-phrase-8"));
    }

    [Test]
    public void Learned_phrases_take_precedence_over_static_phrases()
    {
        var learned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ReplyShapes.DirectAnswer] = new[] { "my-learned-marker-phrase" }
        };

        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.StaticFallback, learned);

        Assert.That(prompt, Does.Contain("my-learned-marker-phrase"));
        Assert.That(prompt, Does.Not.Contain("I hope this email finds you well"));
    }

    [Test]
    public void Only_selected_shape_phrases_appear()
    {
        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.StaticFallback, learnedPhrases: null);

        Assert.That(prompt, Does.Contain("I hope this email finds you well"));
        Assert.That(prompt, Does.Not.Contain("Thank you so much for your email"));
        Assert.That(prompt, Does.Not.Contain("I regret to inform you"));
    }

    [Test]
    public void Avoid_section_falls_back_to_static_when_learned_dictionary_lacks_shape()
    {
        var learned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ReplyShapes.Decline] = new[] { "decline-only-phrase" }
        };

        var prompt = BuildPrompt(ReplyShapes.DirectAnswer, AvoidPhrasesMode.StaticFallback, learned);

        Assert.That(prompt, Does.Contain("I hope this email finds you well"));
        Assert.That(prompt, Does.Not.Contain("decline-only-phrase"));
    }

    private static string BuildPrompt(
        string replyShape,
        AvoidPhrasesMode mode,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? learnedPhrases)
    {
        return LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(learnedPhrases),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            replyShape,
            "Test shape",
            ["Can you confirm if that viewing time works for you?"],
            [],
            mode);
    }

    private static IncomingEmail CreateEmail() =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("enquiries@loc8me.co.uk", "Enquiries"),
            "Notification of Viewing",
            "Can you confirm if the viewing time works for you?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private static StyleProfile CreateStyleProfile(IReadOnlyDictionary<string, IReadOnlyList<string>>? avoidPhrases) =>
        new(
            "domain:loc8me.co.uk",
            "Hi",
            "Thank you",
            "professional and concise",
            string.Empty,
            [],
            8.0,
            0.7,
            0.1,
            0.45,
            0.02,
            0.05,
            0.55,
            0.2,
            0.25,
            1,
            2,
            0.4,
            12,
            DateTimeOffset.UtcNow,
            AvoidPhrases: avoidPhrases,
            AvoidPhrasesUpdatedAtUtc: avoidPhrases is null ? null : DateTimeOffset.UtcNow);

    private static EmailRequestAnalysis CreateAnalysis() =>
        new(
            [new EmailAsk("Can you confirm if that viewing time works for you?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);
}
