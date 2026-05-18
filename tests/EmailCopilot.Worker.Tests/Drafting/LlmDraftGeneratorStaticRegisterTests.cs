namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorStaticRegisterTests
{
    [Test]
    public void Prompt_no_longer_contains_static_register_rules_header()
    {
        var prompt = BuildPrompt();

        Assert.That(prompt, Does.Not.Contain("Register rules"));
    }

    [Test]
    public void Prompt_no_longer_contains_static_exclamation_ban()
    {
        var prompt = BuildPrompt();

        Assert.That(prompt, Does.Not.Contain("Do NOT use exclamation marks"));
    }

    [Test]
    public void Prompt_no_longer_contains_static_hard_sentence_cap()
    {
        var prompt = BuildPrompt();

        Assert.That(prompt, Does.Not.Contain("Hard cap: 3 sentences"));
    }

    [Test]
    public void Prompt_opens_with_persona_framing_as_the_user()
    {
        var prompt = BuildPrompt();

        Assert.That(prompt, Does.Contain("You are writing this reply AS the user"));
        Assert.That(prompt, Does.Contain("not as an AI assistant"));
        Assert.That(prompt, Does.Contain("Match their tone and voice so closely"));
    }

    private static string BuildPrompt() =>
        LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            CreateProfile(),
            [],
            CreateAnalysis(),
            false,
            "Can you confirm if that viewing time works for you?",
            null,
            ReplyShapes.AcknowledgeAndAsk,
            "Acknowledge and ask",
            ["Can you confirm if that viewing time works for you?"],
            []);

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

    private static StyleProfile CreateProfile() =>
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
            DateTimeOffset.UtcNow);

    private static EmailRequestAnalysis CreateAnalysis() =>
        new(
            [new EmailAsk("Can you confirm if that viewing time works for you?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);
}
