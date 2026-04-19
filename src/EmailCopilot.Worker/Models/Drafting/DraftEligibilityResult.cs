namespace EmailCopilot.Worker;

public sealed record DraftEligibilityResult(
    string Decision,
    string Reason,
    double AmbiguityScore)
{
    public bool IsIneligible => string.Equals(Decision, DraftEligibilityDecisions.Ineligible, StringComparison.OrdinalIgnoreCase);

    public bool IsVariantCandidate => string.Equals(Decision, DraftEligibilityDecisions.VariantCandidate, StringComparison.OrdinalIgnoreCase);
}
