namespace EmailCopilot.Worker;

public sealed class ExclusionRulesValidator
{
    public IReadOnlyList<string> Validate(IReadOnlyList<ConfiguredExclusionRule> rules)
    {
        var issues = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var ruleLabel = string.IsNullOrWhiteSpace(rule.Name)
                ? $"rule[{index}]"
                : $"rule '{rule.Name.Trim()}'";

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                issues.Add($"{ruleLabel} is missing a Name.");
            }
            else if (!seenNames.Add(rule.Name.Trim()))
            {
                issues.Add($"{ruleLabel} has a duplicate Name.");
            }

            if (!rule.Enabled)
            {
                continue;
            }

            if (!HasCriteria(rule))
            {
                issues.Add($"{ruleLabel} is enabled but has no matching criteria.");
            }
        }

        return issues;
    }

    private static bool HasCriteria(ConfiguredExclusionRule rule) =>
        rule.SenderPatterns.Count > 0 ||
        rule.DomainPatterns.Count > 0 ||
        rule.DisplayNamePatterns.Count > 0 ||
        rule.SubjectContains.Count > 0 ||
        rule.BodyContains.Count > 0;
}
