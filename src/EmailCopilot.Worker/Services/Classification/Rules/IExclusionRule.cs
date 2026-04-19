namespace EmailCopilot.Worker;

public interface IExclusionRule
{
    string RuleName { get; }
    string ReasonCode { get; }
    RuleEvaluation Evaluate(IncomingEmail email);
}
