namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class EmailRequestAnalyzerTests
{
    [Test]
    public void Heuristic_analysis_should_extract_question_and_deadline()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sophie@example.com", "Sophie"),
            "Viewing question",
            "Can you send the readings by Friday?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.Asks, Has.Count.EqualTo(1));
        Assert.That(result.Asks[0].AskType, Is.EqualTo(AskTypes.Question));
        Assert.That(result.StatedDeadlines, Does.Contain("by Friday"));
    }

    [Test]
    public void Heuristic_analysis_should_extract_decision_branch_for_cancellation_update()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("court@example.com", "Court Clerk"),
            "Hearing update",
            "The hearing is no longer going ahead.",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.DecisionBranches.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.DecisionBranches.SelectMany(branch => branch.ViableReplyShapes), Does.Contain(ReplyShapes.AcknowledgeAndAsk));
    }
}
