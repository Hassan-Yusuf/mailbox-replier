namespace EmailCopilot.Worker;

public sealed class CustomerFeedbackSurveyRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:CUSTOMER_FEEDBACK_SURVEY";
    public override string ReasonCode => ClassificationReasonCodes.FeedbackSurveyRequest;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();
        var combined = $"{subject}\n{body}";

        if (ContainsAny(
                combined,
                "rate our",
                "rate your experience",
                "how did we do",
                "how satisfied",
                "satisfaction with",
                "questionnaire about your experience",
                "complete the questionnaire",
                "short questionnaire",
                "experience with slc",
                "how helpful was",
                "feedback on your",
                "out of 5",
                "nps score",
                "net promoter") ||
            ContainsAny(
                subject,
                "got 30 secs",
                "take a moment to rate",
                "tell us how we did",
                "satisfaction with"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
