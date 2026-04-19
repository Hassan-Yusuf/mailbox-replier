using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class ConfiguredExclusionClassifier : IEmailClassifier
{
    private readonly IOptionsMonitor<ExclusionRulesOptions> _optionsMonitor;

    public ConfiguredExclusionClassifier(IOptionsMonitor<ExclusionRulesOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<RuleEvaluation>();

        foreach (var rule in _optionsMonitor.CurrentValue.Rules)
        {
            var evaluation = Evaluate(rule, email);
            if (evaluation is null)
            {
                continue;
            }

            evaluations.Add(evaluation);

            if (!evaluation.Matched)
            {
                continue;
            }

            var ruleName = string.IsNullOrWhiteSpace(rule.Name) ? "unnamed" : rule.Name.Trim();
            var reasonCode = string.IsNullOrWhiteSpace(rule.ReasonCode)
                ? ClassificationReasonCodes.UserExclusionRule
                : rule.ReasonCode.Trim();

            return Task.FromResult(new ClassificationResult(
                false,
                reasonCode,
                $"{ClassificationDecisionSources.ConfiguredRulePrefix}:{ruleName}",
                new DecisionTrace(evaluations)));
        }

        evaluations.Add(new RuleEvaluation(
            "CONFIGURED:NO_MATCH",
            true,
            ClassificationReasonCodes.DefaultReply,
            $"{ClassificationDecisionSources.ConfiguredRulePrefix}:NO_MATCH",
            "no configured exclusion rules matched"));

        return Task.FromResult(new ClassificationResult(
            true,
            ClassificationReasonCodes.DefaultReply,
            $"{ClassificationDecisionSources.ConfiguredRulePrefix}:NO_MATCH",
            new DecisionTrace(evaluations)));
    }

    private static bool HasCriteria(ConfiguredExclusionRule rule) =>
        rule.SenderPatterns.Count > 0 ||
        rule.DomainPatterns.Count > 0 ||
        rule.DisplayNamePatterns.Count > 0 ||
        rule.SubjectContains.Count > 0 ||
        rule.BodyContains.Count > 0;

    private static RuleEvaluation? Evaluate(ConfiguredExclusionRule rule, IncomingEmail email)
    {
        if (!rule.Enabled || !HasCriteria(rule))
        {
            return null;
        }

        var ruleName = string.IsNullOrWhiteSpace(rule.Name) ? "unnamed" : rule.Name.Trim();
        var decisionSource = $"{ClassificationDecisionSources.ConfiguredRulePrefix}:{ruleName}";
        var reasonCode = string.IsNullOrWhiteSpace(rule.ReasonCode)
            ? ClassificationReasonCodes.UserExclusionRule
            : rule.ReasonCode.Trim();

        var address = email.From.Address;
        var domain = email.From.Domain;
        var displayName = email.From.DisplayName.Trim();
        var subject = email.Subject.Trim();
        var body = email.BodyText.Trim();
        var detailParts = new List<string>();

        if (rule.SenderPatterns.Count > 0)
        {
            var matchedPattern = rule.SenderPatterns.FirstOrDefault(pattern => MatchesPattern(address, pattern));
            if (matchedPattern is null)
            {
                return new RuleEvaluation(
                    $"CONFIGURED:{ruleName}",
                    false,
                    reasonCode,
                    decisionSource,
                    $"sender mismatch: {address}");
            }

            detailParts.Add($"sender={matchedPattern}");
        }

        if (rule.DomainPatterns.Count > 0)
        {
            var matchedPattern = rule.DomainPatterns.FirstOrDefault(pattern => MatchesPattern(domain, pattern));
            if (matchedPattern is null)
            {
                return new RuleEvaluation(
                    $"CONFIGURED:{ruleName}",
                    false,
                    reasonCode,
                    decisionSource,
                    $"domain mismatch: {domain}");
            }

            detailParts.Add($"domain={matchedPattern}");
        }

        if (rule.DisplayNamePatterns.Count > 0)
        {
            var matchedPattern = rule.DisplayNamePatterns.FirstOrDefault(pattern => MatchesPattern(displayName, pattern));
            if (matchedPattern is null)
            {
                return new RuleEvaluation(
                    $"CONFIGURED:{ruleName}",
                    false,
                    reasonCode,
                    decisionSource,
                    $"display-name mismatch: {displayName}");
            }

            detailParts.Add($"display-name={matchedPattern}");
        }

        if (rule.SubjectContains.Count > 0)
        {
            var matchedMarker = rule.SubjectContains.FirstOrDefault(marker => ContainsIgnoreCase(subject, marker));
            if (matchedMarker is null)
            {
                return new RuleEvaluation(
                    $"CONFIGURED:{ruleName}",
                    false,
                    reasonCode,
                    decisionSource,
                    $"subject mismatch: {subject}");
            }

            detailParts.Add($"subject~={matchedMarker}");
        }

        if (rule.BodyContains.Count > 0)
        {
            var matchedMarker = rule.BodyContains.FirstOrDefault(marker => ContainsIgnoreCase(body, marker));
            if (matchedMarker is null)
            {
                return new RuleEvaluation(
                    $"CONFIGURED:{ruleName}",
                    false,
                    reasonCode,
                    decisionSource,
                    $"body mismatch");
            }

            detailParts.Add($"body~={matchedMarker}");
        }

        return new RuleEvaluation(
            $"CONFIGURED:{ruleName}",
            true,
            reasonCode,
            decisionSource,
            detailParts.Count == 0
                ? "matched"
                : string.Join("; ", detailParts));
    }

    private static bool ContainsIgnoreCase(string value, string marker) =>
        !string.IsNullOrWhiteSpace(marker) &&
        value.Contains(marker.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var normalizedPattern = pattern.Trim();
        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(value, normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
