namespace EmailCopilot.Worker;

public sealed class AutomatedSenderRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:AUTOMATED_SENDER";
    public override string ReasonCode => ClassificationReasonCodes.AutomatedSender;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var fromAddress = email.From.Address;
        var parts = SplitAddress(fromAddress);
        var domain = parts?.Domain ?? string.Empty;

        if (email.IsAutoSubmitted ||
            LooksLikeHashedLocalPart(fromAddress) ||
            fromAddress.Contains("noreply", StringComparison.Ordinal) ||
            fromAddress.Contains("no-reply", StringComparison.Ordinal) ||
            fromAddress.Contains("do-not-reply", StringComparison.Ordinal) ||
            fromAddress.Contains("mailer-daemon", StringComparison.Ordinal) ||
            fromAddress.Contains("postmaster", StringComparison.Ordinal) ||
            domain.StartsWith("infomails.", StringComparison.Ordinal) ||
            domain.Contains(".infomails.", StringComparison.Ordinal))
        {
            return Matched(RuleName, ReasonCode, $"address={fromAddress}");
        }

        return NotMatched(RuleName, ReasonCode, $"address={fromAddress}");
    }

    private static bool LooksLikeHashedLocalPart(string fromAddress)
    {
        var parts = SplitAddress(fromAddress);
        if (parts is null)
        {
            return false;
        }

        var (localPart, _) = parts.Value;
        return localPart.Length >= 12 &&
               localPart.Any(char.IsDigit) &&
               localPart.Count(character => character is '-' or '_') >= 2;
    }
}
