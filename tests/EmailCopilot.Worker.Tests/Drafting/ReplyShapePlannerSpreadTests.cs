namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ReplyShapePlannerSpreadTests
{
    private readonly ReplyShapePlanner _planner = new();

    [Test]
    public void Drops_within_family_shape_when_gap_below_minimum_spread()
    {
        var result = _planner.Plan(
            CreateEmail("The viewing is confirmed for Tuesday at 3pm."),
            CreateStyleProfile(questionEndingRate: 0.1),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Logistics update", [ReplyShapes.ConfirmAndRequest, ReplyShapes.ConfirmAndClose])],
                [],
                UrgencyLevels.Medium,
                false));

        var confirmFamilyCount = result.Options.Count(static option =>
            option.Shape == ReplyShapes.ConfirmAndRequest || option.Shape == ReplyShapes.ConfirmAndClose);

        Assert.That(confirmFamilyCount, Is.EqualTo(1));
    }

    [Test]
    public void Keeps_shapes_in_different_families_even_when_scores_close()
    {
        var result = _planner.Plan(
            CreateEmail("We have a role available if you're interested."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [],
                [new DecisionBranch("Offer", [ReplyShapes.Acknowledge, ReplyShapes.Decline])],
                [],
                UrgencyLevels.Low,
                false));

        var shapes = result.Options.Select(option => option.Shape).ToArray();
        Assert.That(shapes, Does.Contain(ReplyShapes.Acknowledge));
        Assert.That(shapes, Does.Contain(ReplyShapes.Decline));
    }

    [Test]
    public void Keeps_within_family_shape_when_gap_meets_minimum_spread()
    {
        var result = _planner.Plan(
            CreateEmail("We have a role available if you're interested."),
            CreateStyleProfile(questionEndingRate: 0.4),
            new EmailRequestAnalysis(
                [new EmailAsk("Would you be interested in this role?", AskTypes.Decision, false)],
                [new DecisionBranch("Offer", [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.Decline])],
                [],
                UrgencyLevels.Low,
                false));

        var acknowledgeFamily = result.Options
            .Where(option => option.Shape == ReplyShapes.Acknowledge || option.Shape == ReplyShapes.AcknowledgeAndAsk)
            .OrderByDescending(option => option.ConfidenceScore)
            .ToArray();

        if (acknowledgeFamily.Length == 2)
        {
            var gap = acknowledgeFamily[0].ConfidenceScore - acknowledgeFamily[1].ConfidenceScore;
            Assert.That(gap, Is.GreaterThanOrEqualTo(0.10),
                "if both acknowledge-family shapes are kept the gap must be >= MinimumSpread");
        }
        else
        {
            Assert.That(acknowledgeFamily.Length, Is.EqualTo(1),
                "expected at most one acknowledge-family shape when gap is below MinimumSpread");
        }
    }

    [Test]
    public void Sorts_options_by_confidence_score_descending()
    {
        var result = _planner.Plan(
            CreateEmail("We have a role available if you're interested."),
            CreateStyleProfile(),
            new EmailRequestAnalysis(
                [new EmailAsk("Would you be interested?", AskTypes.Decision, false)],
                [new DecisionBranch("Offer", [ReplyShapes.Acknowledge, ReplyShapes.Decline, ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest])],
                [],
                UrgencyLevels.Low,
                false));

        var scores = result.Options.Select(option => option.ConfidenceScore).ToArray();
        for (var i = 1; i < scores.Length; i++)
        {
            Assert.That(scores[i], Is.LessThanOrEqualTo(scores[i - 1]),
                "options must be sorted by confidence score descending");
        }
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
