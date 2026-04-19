namespace EmailCopilot.Worker;

public sealed class BroadcastFormattedRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:BROADCAST_FORMATTED";

    public override string ReasonCode => ClassificationReasonCodes.BroadcastFormattedMail;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(
                subject,
                "we've got news",
                "we have got news",
                "joining forces with",
                "we've updated our",
                "we have updated our",
                "t&cs",
                "privacy policy",
                "terms of use",
                "terms of service",
                "important update to",
                "changes to your",
                "notice of changes"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (ContainsAny(
                body,
                "view online",
                "joining forces with",
                "we've got news",
                "we have got news",
                "privacy policy",
                "terms and conditions",
                "terms of service",
                "we've updated our",
                "we have updated our",
                "no action is needed from you"))
        {
            return Matched(RuleName, ReasonCode, "broadcast-formatted-body");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
