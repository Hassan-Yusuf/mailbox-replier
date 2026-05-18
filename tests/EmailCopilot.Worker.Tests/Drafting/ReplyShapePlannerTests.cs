namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ReplyShapePlannerTests
{
    private readonly ReplyShapePlanner _planner = new();

    [Test]
    public void Should_resolve_direct_answer_for_clear_question()
    {
        var result = _planner.Plan(
            CreateEmail("Can you send the readings by Friday?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Can you send the readings by Friday?", AskTypes.Question, false)],
                [],
                ["by Friday"],
                UrgencyLevels.Medium,
                false));

        Assert.That(result.Options, Has.Count.EqualTo(1));
        Assert.That(result.Options[0].Shape, Is.EqualTo(ReplyShapes.DirectAnswer));
    }

    [Test]
    public void Should_offer_multiple_intents_for_informational_update()
    {
        var result = _planner.Plan(
            CreateEmail("The hearing is no longer going ahead."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Hearing cancelled", [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest, ReplyShapes.ConfirmAndClose])],
                [],
                UrgencyLevels.Low,
                false));

        var shapes = result.Options.Select(option => option.Shape).ToArray();
        Assert.That(result.Options.Count, Is.GreaterThan(1));
        Assert.That(
            shapes,
            Does.Contain(ReplyShapes.Acknowledge).Or.Contain(ReplyShapes.AcknowledgeAndAsk),
            "expected at least one acknowledge-family shape");
        Assert.That(
            shapes,
            Does.Contain(ReplyShapes.ConfirmAndRequest).Or.Contain(ReplyShapes.ConfirmAndClose),
            "expected at least one confirm-family shape");
    }

    [Test]
    public void Should_offer_decline_path_for_invitation_or_offer()
    {
        var result = _planner.Plan(
            CreateEmail("We have a role available if you're interested."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Offer or invitation", [ReplyShapes.AcknowledgeAndAsk, ReplyShapes.Decline])],
                [],
                UrgencyLevels.Low,
                false));

        Assert.That(result.Options.Count, Is.GreaterThan(1));
        Assert.That(result.Options.Select(option => option.Shape), Does.Contain(ReplyShapes.Decline));
    }

    [Test]
    public void Should_place_decline_in_top_three_when_analysis_surfaces_it()
    {
        var result = _planner.Plan(
            CreateEmail("We have a role available if you're interested."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Would you be interested in this role?", AskTypes.Decision, false)],
                [new DecisionBranch("Offer or invitation", [ReplyShapes.Acknowledge, ReplyShapes.Decline, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest])],
                [],
                UrgencyLevels.Low,
                false));

        Assert.That(result.Options.Take(3).Select(option => option.Shape), Does.Contain(ReplyShapes.Decline));
    }

    [Test]
    public void Should_not_offer_decline_when_analysis_does_not_surface_it()
    {
        var result = _planner.Plan(
            CreateEmail("The hearing is no longer going ahead."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Hearing cancelled", [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest])],
                [],
                UrgencyLevels.Low,
                false));

        Assert.That(result.Options.Select(option => option.Shape), Does.Not.Contain(ReplyShapes.Decline));
    }

    [Test]
    public void Should_offer_acknowledge_and_confirm_for_logistics_update_without_question()
    {
        var result = _planner.Plan(
            CreateEmail("The viewing is confirmed for Tuesday at 3pm."),
            CreateStyleProfile(questionEndingRate: 0.1),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Logistics update", [ReplyShapes.Acknowledge, ReplyShapes.ConfirmAndRequest, ReplyShapes.ConfirmAndClose])],
                ["Tuesday at 3pm"],
                UrgencyLevels.Medium,
                false));

        var shapes = result.Options.Select(option => option.Shape).ToArray();
        Assert.That(result.Options.Count, Is.GreaterThan(1));
        Assert.That(shapes, Does.Contain(ReplyShapes.Acknowledge));
        Assert.That(shapes, Does.Not.Contain(ReplyShapes.AcknowledgeAndAsk));
        Assert.That(
            shapes,
            Does.Contain(ReplyShapes.ConfirmAndRequest).Or.Contain(ReplyShapes.ConfirmAndClose),
            "expected at least one confirm-family shape");
        var confirmFamilyCount = shapes.Count(static shape =>
            shape == ReplyShapes.ConfirmAndRequest || shape == ReplyShapes.ConfirmAndClose);
        Assert.That(
            confirmFamilyCount,
            Is.EqualTo(1),
            "minimum-spread should keep only one confirm-family shape when scores are within 0.10");
    }

    [Test]
    public void Should_not_short_circuit_to_direct_answer_when_question_mark_present_but_no_direct_opener()
    {
        // "Let me know" + "?" without a direct-question opener ("can you", "are you", etc.)
        // was previously routing everything to DIRECT_ANSWER. It should fall through to
        // the informational-update or default branch instead.
        var result = _planner.Plan(
            CreateEmail("Just to update you — the appointment is confirmed. Let me know if anything changes?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Let me know if anything changes?", AskTypes.Question, true)],
                [new DecisionBranch("Appointment confirmed", [ReplyShapes.Acknowledge, ReplyShapes.ConfirmAndClose])],
                [],
                UrgencyLevels.Low,
                false));

        Assert.That(result.Options[0].Shape, Is.Not.EqualTo(ReplyShapes.DirectAnswer));
    }

    [Test]
    public void Should_resolve_direct_answer_for_question_with_explicit_direct_opener()
    {
        var result = _planner.Plan(
            CreateEmail("Are you available to cover the shift on Saturday?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Are you available to cover the shift on Saturday?", AskTypes.Question, false)],
                [],
                ["Saturday"],
                UrgencyLevels.Medium,
                false));

        Assert.That(result.Options, Has.Count.EqualTo(1));
        Assert.That(result.Options[0].Shape, Is.EqualTo(ReplyShapes.DirectAnswer));
    }

    [Test]
    public void Should_gate_direct_answer_when_email_asks_unanswerable_factual_question()
    {
        var result = _planner.Plan(
            CreateEmail("Is there a dehumidifier at the property? How many do you have?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Is there a dehumidifier at the property?", AskTypes.Question, false)],
                [],
                [],
                UrgencyLevels.Medium,
                false));

        Assert.That(result.Options.Select(option => option.Shape), Does.Not.Contain(ReplyShapes.DirectAnswer));
        Assert.That(result.Options[0].Shape, Is.EqualTo(ReplyShapes.AcknowledgeAndAsk));
    }

    [Test]
    public void Should_gate_direct_answer_inside_branch_options_when_unanswerable_factual_question()
    {
        var result = _planner.Plan(
            CreateEmail("How many dehumidifiers do you have at the flat?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("How many dehumidifiers do you have?", AskTypes.Question, false)],
                [new DecisionBranch("Property knowledge question", [ReplyShapes.DirectAnswer, ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk])],
                [],
                UrgencyLevels.Medium,
                false));

        Assert.That(result.Options.Select(option => option.Shape), Does.Not.Contain(ReplyShapes.DirectAnswer));
        Assert.That(result.Options.Select(option => option.Shape), Does.Contain(ReplyShapes.AcknowledgeAndAsk));
    }

    [Test]
    public void Should_keep_direct_answer_for_availability_question_even_with_personal_confirmation()
    {
        // "Can you work tomorrow?" is answerable in the user's voice (yes/no + placeholder time).
        // The unanswerable-info gate should NOT fire here.
        var result = _planner.Plan(
            CreateEmail("Can you work tomorrow at the Ipswich box from 4pm to 11pm?"),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Can you work tomorrow?", AskTypes.Question, false)],
                [],
                ["tomorrow"],
                UrgencyLevels.High,
                true));

        Assert.That(result.Options[0].Shape, Is.EqualTo(ReplyShapes.DirectAnswer));
    }

    private static IncomingEmail CreateEmail(string body) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sender@example.com", "Sender"),
            "Test subject",
            body,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private static StyleProfile CreateStyleProfile(double questionEndingRate = 0.4) =>
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
            questionEndingRate,
            0.02,
            0.05,
            0.3,
            0.1,
            0.15,
            1,
            2,
            0.5,
            20,
            DateTimeOffset.UtcNow);
}
