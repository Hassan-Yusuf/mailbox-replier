namespace EmailCopilot.Worker;

public sealed class BulkPromotionalRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:BULK_MARKER";
    public override string ReasonCode => ClassificationReasonCodes.BulkOrPromotional;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(subject,
                "newsletter",
                "roundup",
                "edition",
                "job alert",
                "your skills matched",
                "recommended for you",
                "unsubscribe",
                "manage preferences",
                "view in browser") ||
            ContainsAny(body,
                "unsubscribe",
                "manage preferences",
                "manage subscriptions",
                "email preferences",
                "view in browser",
                "why did i get this email",
                "you received this email because",
                "do not reply to this email",
                "list-unsubscribe",
                "list-id"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
