namespace EmailCopilot.Worker;

public sealed class RuleToggleEvaluator
{
    public static readonly RuleToggleEvaluator AllEnabled =
        new(new Dictionary<string, bool>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, bool> _snapshot;

    public RuleToggleEvaluator(IReadOnlyDictionary<string, bool> snapshot)
    {
        _snapshot = snapshot;
    }

    public bool IsEnabled(string ruleName) =>
        !_snapshot.TryGetValue(ruleName, out var enabled) || enabled;
}
