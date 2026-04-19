namespace EmailCopilot.Worker;

public sealed class WorkflowAcknowledgementRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:WORKFLOW_ACKNOWLEDGEMENT";
    public override string ReasonCode => ClassificationReasonCodes.WorkflowAcknowledgement;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(
                subject,
                "we received your job application",
                "application received",
                "application submitted",
                "thanks for applying",
                "thank you for applying",
                "thank you for your interest",
                "your application has been received",
                "we received your application"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (ContainsAny(
                body,
                "your application for the position listed below was successfully submitted",
                "please check your email for updates",
                "thank you for applying",
                "thank you for your interest in",
                "your details have been forwarded to our recruitment team for review",
                "we are unable to personally speak with everyone that applies",
                "we appreciate your patience as we review all applications",
                "we have successfully received your application"))
        {
            return Matched(RuleName, ReasonCode, "workflow-body");
        }

        var parts = SplitAddress(fromAddress);
        if (parts is null)
        {
            return NotMatched(RuleName, ReasonCode, "invalid-address");
        }

        var (_, domain) = parts.Value;
        if (domain.Contains("workflow.", StringComparison.Ordinal) ||
            domain.Contains(".workflow", StringComparison.Ordinal) ||
            domain.Contains("smartrecruiters.com", StringComparison.Ordinal) ||
            domain.Contains("oracle.com", StringComparison.Ordinal) ||
            domain.Contains("oraclecloud.com", StringComparison.Ordinal))
        {
            return Matched(RuleName, ReasonCode, $"domain={domain}");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
