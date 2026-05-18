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
    public void Heuristic_analysis_flags_personal_confirmation_for_shift_request()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sophie@cateringelite.co.uk", "Sophie"),
            "URGENT shift tomorrow",
            "Hi, can you work tomorrow at the Ipswich box from 4pm to 11pm?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.RequiresPersonalConfirmation, Is.True);
    }

    [Test]
    public void Heuristic_analysis_flags_personal_confirmation_for_property_knowledge_question()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("hayley@acreproperties.com", "Hayley"),
            "Dehumidifiers at the property",
            "Hi, is there a dehumidifier at the property? How many do you have?",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.RequiresPersonalConfirmation, Is.True);
    }

    [Test]
    public void Heuristic_analysis_flags_personal_confirmation_for_experience_question()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("lee@cateringelite.co.uk", "Lee"),
            "Work available",
            "Hi, we have work available. Please reply with your catering experience and qualifications.",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.RequiresPersonalConfirmation, Is.True);
    }

    [Test]
    public void Heuristic_analysis_does_not_flag_personal_confirmation_for_informational_email()
    {
        var email = new IncomingEmail(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("noreply@example.com", "System"),
            "Your weekly digest",
            "Here is your weekly digest. No action required.",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

        var result = EmailRequestAnalyzer.BuildHeuristicAnalysisForTesting(email);

        Assert.That(result.RequiresPersonalConfirmation, Is.False);
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
