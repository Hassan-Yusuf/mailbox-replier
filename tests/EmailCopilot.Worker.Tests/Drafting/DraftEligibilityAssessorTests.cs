namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class DraftEligibilityAssessorTests
{
    private readonly DraftEligibilityAssessor _assessor = new();

    [Test]
    public void Should_mark_clear_question_as_single_draft()
    {
        var result = _assessor.Assess(
            CreateEmail("sophie@example.com", "Sophie", "Can you send the readings by Friday?"),
            CreateProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Can you send the readings by Friday?", AskTypes.Question, false)],
                [],
                ["by Friday"],
                UrgencyLevels.Medium,
                false));

        Assert.That(result.Decision, Is.EqualTo(DraftEligibilityDecisions.SingleDraft));
    }

    [Test]
    public void Should_mark_high_stakes_informational_update_as_variant_candidate()
    {
        var result = _assessor.Assess(
            CreateEmail("clerk@court.example", "Court Clerk", "The hearing is no longer going ahead."),
            CreateProfile(),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Hearing cancelled", [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest])],
                [],
                UrgencyLevels.Low,
                false));

        Assert.That(result.Decision, Is.EqualTo(DraftEligibilityDecisions.VariantCandidate));
        Assert.That(result.AmbiguityScore, Is.GreaterThan(0.45));
    }

    [Test]
    public void Should_mark_non_conversational_update_as_ineligible()
    {
        var result = _assessor.Assess(
            CreateEmail("membership@example.com", "Membership Team", "We've updated our T&Cs and Privacy Policy"),
            CreateProfile(),
            EmailRequestAnalysis.Empty);

        Assert.That(result.Decision, Is.EqualTo(DraftEligibilityDecisions.Ineligible));
    }

    [Test]
    public void PersonalConfirmation_flag_pushes_score_above_variant_candidate_threshold()
    {
        var result = _assessor.Assess(
            CreateEmail("scheduler@example.com", "Scheduler", "Can you work the 8am-4pm shift tomorrow?"),
            CreateProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Can you work the 8am-4pm shift tomorrow?", AskTypes.Scheduling, false)],
                [],
                ["tomorrow"],
                UrgencyLevels.Medium,
                true));

        Assert.That(result.Decision, Is.EqualTo(DraftEligibilityDecisions.VariantCandidate));
        Assert.That(result.AmbiguityScore, Is.GreaterThan(0.45));
    }

    private static IncomingEmail CreateEmail(string fromAddress, string displayName, string body) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(fromAddress, displayName),
            "Test subject",
            body,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private static StyleProfile CreateProfile() =>
        new(
            "relationship-professional",
            "Hi",
            "Best regards",
            "professional and concise",
            string.Empty,
            [],
            10,
            0.6,
            0.1,
            0.35,
            0.1,
            0.2,
            0.05,
            0.2,
            1,
            2,
            0.55,
            30,
            DateTimeOffset.UtcNow);
}
