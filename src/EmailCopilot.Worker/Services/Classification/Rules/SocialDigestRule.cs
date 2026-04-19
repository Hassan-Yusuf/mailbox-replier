namespace EmailCopilot.Worker;

public sealed class SocialDigestRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:SOCIAL_DIGEST";
    public override string ReasonCode => ClassificationReasonCodes.SocialOrDigest;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(
                fromAddress,
                "quora",
                "beehiiv",
                "jobleads",
                "jobalert",
                "service@veo.co",
                "notification.",
                "bebee",
                "linkedin",
                "substack",
                "newsletter",
                "notifications",
                "updates",
                "alerts",
                "jobs@",
                "news@") ||
            ContainsAny(subject, "tagged you", "mentioned you", "commented on", "reacted to your", "shared with you") ||
            ContainsAny(subject, "digest", "daily update", "weekly update", "top stories", "trending stories", "lens") ||
            ContainsAny(body, "new answers", "recommended for you", "top posts", "people are talking about", "tagged you in a video", "tagged you in", "mentioned you", "watch the highlight"))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
