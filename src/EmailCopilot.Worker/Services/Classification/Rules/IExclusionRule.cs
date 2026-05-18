namespace EmailCopilot.Worker;

public interface IExclusionRule
{
    string RuleName { get; }
    string ReasonCode { get; }
    string Description { get; }
    RuleCategory Category { get; }
    RuleEvaluation Evaluate(IncomingEmail email);
}
