namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class DraftGroundingCheckerTests
{
    private readonly DraftGroundingChecker _checker = new();

    [Test]
    public void Should_flag_unverified_dates_and_missed_asks()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sender@example.com", "Sender"),
            "Broken chair",
            "Can you send photos of the broken chair?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Can you send photos of the broken chair?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = _checker.Check(
            email,
            "Hi,\n\nI'll send that tomorrow after I finish checking the maintenance records and speaking to the property team. I will come back to you with a fuller update once I have gone through everything in detail and confirmed the next steps.",
            analysis,
            ["Can you send photos of the broken chair?"]);

        Assert.That(warning, Does.Contain("unverified_date"));
        Assert.That(warning, Does.Contain("missed_ask:Can you send photos of the broken chair?"));
    }

    [Test]
    public void Should_not_emit_missed_ask_warning_for_short_acknowledgement_with_one_keyword_match()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sender@example.com", "Sender"),
            "Broken chair",
            "Can you send photos of the broken chair?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Can you send photos of the broken chair?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = _checker.Check(
            email,
            "Hi,\n\nI'll send photos shortly.",
            analysis,
            ["Can you send photos of the broken chair?"]);

        Assert.That(warning, Is.Null.Or.Empty);
    }

    [Test]
    public void Should_emit_missed_ask_warning_for_long_draft_with_insufficient_keyword_overlap()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sender@example.com", "Sender"),
            "Broken chair",
            "Can you send photos of the broken chair?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Can you send photos of the broken chair?", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = _checker.Check(
            email,
            "Hi, I have looked into the issue and will coordinate with the team on the property as soon as possible. I will update you once I have more information from maintenance and the inspection process.",
            analysis,
            ["Can you send photos of the broken chair?"]);

        Assert.That(warning, Does.Contain("missed_ask:Can you send photos of the broken chair?"));
    }
}
