using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class SuspiciousSpamRule : BuiltInClassificationRule
{
    private static readonly Regex RandomTokenPattern = new(
        @"\b[A-Za-z0-9]{12,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public override string RuleName => "BUILT_IN:SUSPICIOUS_SPAM";

    public override string ReasonCode => ClassificationReasonCodes.SuspiciousOrSpam;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var subject = email.Subject.Trim();
        var body = email.BodyText.Trim();
        var loweredSubject = subject.ToLowerInvariant();
        var loweredBody = body.ToLowerInvariant();

        if (LooksLikeAttachmentLure(loweredBody) && LooksLikeTokenSoup(body))
        {
            return Matched(RuleName, ReasonCode, "attachment-lure-with-token-soup");
        }

        if (LooksLikePrizeOrGiveaway(loweredSubject) && LooksLikeConfusableSpam(subject))
        {
            return Matched(RuleName, ReasonCode, "obfuscated-prize-subject");
        }

        if (LooksLikeTokenSoup(body) && CountQuotedPipeSegments(body) >= 3)
        {
            return Matched(RuleName, ReasonCode, "structured-token-soup");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={loweredSubject}");
    }

    private static bool LooksLikeAttachmentLure(string body) =>
        ContainsAny(
            body,
            "please find",
            "attached sheet",
            "attached file",
            "for your reference");

    private static bool LooksLikePrizeOrGiveaway(string subject) =>
        ContainsAny(
            subject,
            "claim your free",
            "free kit",
            "winner",
            "gift card",
            "prize");

    private static bool LooksLikeConfusableSpam(string subject) =>
        subject.Any(character => character is 'α' or 'ı' or 'е' or 'ο' or 'ѕ');

    private static bool LooksLikeTokenSoup(string body) =>
        RandomTokenPattern.Matches(body).Count >= 3;

    private static int CountQuotedPipeSegments(string body) =>
        body.Count(character => character == '|');
}
