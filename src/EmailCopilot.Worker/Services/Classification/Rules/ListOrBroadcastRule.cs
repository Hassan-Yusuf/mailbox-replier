namespace EmailCopilot.Worker;

public sealed class ListOrBroadcastRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:LIST_OR_BROADCAST";
    public override string ReasonCode => ClassificationReasonCodes.ListOrBroadcastMail;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var fromDisplayName = email.From.DisplayName.Trim().ToLowerInvariant();
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();
        var precedence = email.Precedence.Trim().ToLowerInvariant();

        if (email.HasListUnsubscribeHeader || email.HasListIdHeader)
        {
            return Matched(RuleName, ReasonCode, "list-header");
        }

        if (ContainsAny(precedence, "bulk", "list", "junk"))
        {
            return Matched(RuleName, ReasonCode, $"precedence={precedence}");
        }

        if (ContainsAny(subject,
                "black friday",
                "cyber monday",
                "exclusive",
                "deals",
                "offers",
                "sale",
                "you've got matches",
                "you have got matches",
                "your matches",
                "new matches for you",
                "did we miss the mark",
                "how did we do",
                "rate your experience",
                "your opinion matters",
                "share your feedback",
                "feedback from donation",
                "tell us about your experience",
                "changes to our",
                "loyalty programme",
                "privacy notice",
                "user agreement",
                "member update",
                "weekly update",
                "monthly update",
                "newsletter",
                "roundup",
                "edition"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (ContainsAny(body,
                "in this update",
                "what's coming up",
                "existing members",
                "our members",
                "no action is needed from you",
                "did we miss the mark",
                "how did we do",
                "rate your experience",
                "member survey",
                "facility survey",
                "tell us about your experience with us",
                "we would be very grateful if you could take a few moments of your time",
                "take a few moments of your time to tell us about your experience",
                "black friday deals",
                "check out our incredible offers",
                "upgrade your membership"))
        {
            return Matched(RuleName, ReasonCode, "broadcast-body");
        }

        if ((fromAddress.StartsWith("info@", StringComparison.Ordinal) ||
             fromAddress.StartsWith("newsletter@", StringComparison.Ordinal) ||
             fromAddress.StartsWith("updates@", StringComparison.Ordinal)) &&
            ContainsAny(subject + "\n" + body + "\n" + fromDisplayName,
                "member",
                "members",
                "survey",
                "deals",
                "offers",
                "newsletter",
                "update",
                "privacy notice",
                "user agreement"))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
