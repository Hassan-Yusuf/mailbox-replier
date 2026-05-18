namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorExclamationTests
{
    [Test]
    public void Low_exclamation_rate_emits_almost_never_guidance()
    {
        var profile = CreateProfile(exclamationUsageRate: 0.01);

        var prompt = BuildPromptFor(profile);

        Assert.That(prompt, Does.Contain("The user almost never uses exclamation marks."));
        Assert.That(prompt, Does.Not.Contain("Exclamation marks can be natural for this user."));
    }

    [Test]
    public void High_exclamation_rate_emits_can_be_natural_guidance()
    {
        var profile = CreateProfile(exclamationUsageRate: 0.45);

        var prompt = BuildPromptFor(profile);

        Assert.That(prompt, Does.Contain("Exclamation marks can be natural for this user."));
        Assert.That(prompt, Does.Not.Contain("The user almost never uses exclamation marks."));
    }

    [Test]
    public void Middle_exclamation_rate_emits_neither_band()
    {
        var profile = CreateProfile(exclamationUsageRate: 0.15);

        var prompt = BuildPromptFor(profile);

        Assert.That(prompt, Does.Not.Contain("The user almost never uses exclamation marks."));
        Assert.That(prompt, Does.Not.Contain("Exclamation marks can be natural for this user."));
    }

    private static string BuildPromptFor(StyleProfile profile) =>
        LlmDraftGenerator.BuildPromptForTesting(
            CreateEmail(),
            profile,
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

    private static StyleProfile CreateProfile(double exclamationUsageRate) =>
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
            exclamationUsageRate,
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
