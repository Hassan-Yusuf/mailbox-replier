namespace EmailCopilot.Worker;

public abstract class BuiltInClassificationRule : IExclusionRule
{
    public abstract string RuleName { get; }

    public abstract string ReasonCode { get; }

    public abstract RuleEvaluation Evaluate(IncomingEmail email);

    protected static RuleEvaluation Matched(string ruleName, string reasonCode, string? detail = null) =>
        new(
            ruleName,
            true,
            reasonCode,
            ClassificationDecisionSources.BuiltIn,
            detail);

    protected static RuleEvaluation NotMatched(string ruleName, string reasonCode, string? detail = null) =>
        new(
            ruleName,
            false,
            reasonCode,
            ClassificationDecisionSources.BuiltIn,
            detail);

    protected static (string LocalPart, string Domain)? SplitAddress(string fromAddress)
    {
        var atIndex = fromAddress.IndexOf('@');
        if (atIndex <= 0 || atIndex == fromAddress.Length - 1)
        {
            return null;
        }

        return (fromAddress[..atIndex], fromAddress[(atIndex + 1)..]);
    }

    protected static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));
}
