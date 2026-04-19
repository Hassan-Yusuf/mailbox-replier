namespace EmailCopilot.Worker;

public sealed class ExclusionRulesOptions
{
    public List<ConfiguredExclusionRule> Rules { get; init; } = [];
}

public sealed class ConfiguredExclusionRule
{
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string ReasonCode { get; init; } = ClassificationReasonCodes.UserExclusionRule;
    public List<string> SenderPatterns { get; init; } = [];
    public List<string> DomainPatterns { get; init; } = [];
    public List<string> DisplayNamePatterns { get; init; } = [];
    public List<string> SubjectContains { get; init; } = [];
    public List<string> BodyContains { get; init; } = [];
}
