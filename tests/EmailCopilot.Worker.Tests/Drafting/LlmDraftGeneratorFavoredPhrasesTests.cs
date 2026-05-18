namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorFavoredPhrasesTests
{
    private const string FavoredHeader =
        "Phrases the user actually writes when replying in this shape - use one only if it fits naturally, do not force:";

    [Test]
    public void Prompt_contains_favored_section_when_profile_has_phrases_for_shape()
    {
        var favored = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ReplyShapes.Acknowledge] = new[] { "Got it, that works for me." }
        };

        var prompt = BuildPrompt(ReplyShapes.Acknowledge, favored);

        Assert.That(prompt, Does.Contain(FavoredHeader));
        Assert.That(prompt, Does.Contain("Got it, that works for me."));
    }

    [Test]
    public void Prompt_omits_favored_section_when_dictionary_lacks_current_shape()
    {
        var favored = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ReplyShapes.Decline] = new[] { "Sorry, I cannot make that." }
        };

        var prompt = BuildPrompt(ReplyShapes.Acknowledge, favored);

        Assert.That(prompt, Does.Not.Contain(FavoredHeader));
        Assert.That(prompt, Does.Not.Contain("Sorry, I cannot make that."));
    }

    [Test]
    public void Prompt_omits_favored_section_when_profile_favored_is_null()
    {
        var prompt = BuildPrompt(ReplyShapes.Acknowledge, favoredPhrases: null);

        Assert.That(prompt, Does.Not.Contain(FavoredHeader));
    }

    [Test]
    public void Favored_section_is_capped_at_three_phrases()
    {
        var manyPhrases = Enumerable.Range(1, 8)
            .Select(i => $"unique-favored-phrase-{i}")
            .ToArray();

        var favored = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ReplyShapes.Acknowledge] = manyPhrases
        };

        var prompt = BuildPrompt(ReplyShapes.Acknowledge, favored);

        var bulletCount = manyPhrases.Count(p => prompt.Contains($"\"{p}\""));
        Assert.That(bulletCount, Is.EqualTo(3));
        Assert.That(prompt, Does.Contain("unique-favored-phrase-1"));
        Assert.That(prompt, Does.Contain("unique-favored-phrase-3"));
        Assert.That(prompt, Does.Not.Contain("unique-favored-phrase-4"));
    }

    [Test]
    public void Favored_section_uses_soft_framing_language()
    {
        var favored = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [ReplyShapes.Acknowledge] = new[] { "Got it, that works for me." }
        };

        var prompt = BuildPrompt(ReplyShapes.Acknowledge, favored);

        Assert.That(prompt, Does.Contain("use one only if it fits naturally, do not force"));
    }

    private static string BuildPrompt(
        string replyShape,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? favoredPhrases)
    {
        return LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateStyleProfile(favoredPhrases),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            replyShape,
            "Test shape",
            ["Can you confirm if that viewing time works for you?"],
            [],
            AvoidPhrasesMode.Disabled);
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

    private static StyleProfile CreateStyleProfile(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? favoredPhrases) =>
        new(
            "domain:loc8me.co.uk",
            "Hi",
            "Thanks",
            "neutral",
            string.Empty,
            [],
            8.0,
            0.5,
            0.5,
            0.1,
            0.02,
            0.1,
            0.4,
            0.1,
            0.2,
            1,
            2,
            0.5,
            5,
            DateTimeOffset.UtcNow,
            FavoredPhrases: favoredPhrases,
            FavoredPhrasesUpdatedAtUtc: favoredPhrases is null ? null : DateTimeOffset.UtcNow);

    private static EmailRequestAnalysis CreateAnalysis() =>
        new(
            [new EmailAsk("Can you confirm if that viewing time works for you?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);
}
