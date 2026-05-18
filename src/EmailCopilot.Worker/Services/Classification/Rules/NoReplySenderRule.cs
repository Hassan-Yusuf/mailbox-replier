namespace EmailCopilot.Worker;

public sealed class NoReplySenderRule : BuiltInClassificationRule
{
    private static readonly string[] NoReplyLocalPartMarkers =
    [
        "noreply",
        "no-reply",
        "donotreply",
        "do-not-reply",
        "auto-confirm",
        "auto-notify",
        "mailer-daemon",
        "postmaster",
        "bounce",
        "verification",
        "verify",
        "login",
        "security",
        "otp"
    ];

    // Common English words like "welcome" are too broad for Contains semantics —
    // they'd match legitimate human aliases (alex.welcome@...). Use StartsWith only.
    private static readonly string[] NoReplyLocalPartPrefixes =
    [
        "welcome"
    ];

    private static readonly string[] KnownNoReplyDomains =
    [
        "amazon.co.uk",
        "amazon.com",
        "ebay.co.uk",
        "ebay.com",
        "info.ebay.co.uk",
        "tfl.gov.uk",
        "halifax.co.uk",
        "barclays.co.uk",
        "trading212.com",
        "paypal.com",
        "paypal.co.uk",
        "alpaca.markets",
        "indeed.com"
    ];

    public override string RuleName => "BUILT_IN:NO_REPLY_PLATFORM_SENDER";
    public override string ReasonCode => ClassificationReasonCodes.NoReplySender;
    public override string Description =>
        "Sender appears to be a no-reply or automated platform address.";
    public override RuleCategory Category => RuleCategory.AutomatedSender;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var parts = SplitAddress(fromAddress);
        if (parts is null)
        {
            return NotMatched(RuleName, ReasonCode, "invalid-address");
        }

        var (localPart, domain) = parts.Value;

        if (NoReplyLocalPartMarkers.Any(marker =>
                localPart.StartsWith(marker, StringComparison.Ordinal) ||
                localPart.Contains(marker, StringComparison.Ordinal)))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        if (NoReplyLocalPartPrefixes.Any(prefix =>
                localPart.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        if (KnownNoReplyDomains.Any(knownDomain =>
                string.Equals(domain, knownDomain, StringComparison.Ordinal) ||
                domain.EndsWith("." + knownDomain, StringComparison.Ordinal)))
        {
            return Matched(RuleName, ReasonCode, $"domain={domain}");
        }

        return NotMatched(RuleName, ReasonCode, $"address={fromAddress}");
    }
}
