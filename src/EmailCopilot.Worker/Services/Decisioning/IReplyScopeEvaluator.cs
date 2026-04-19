namespace EmailCopilot.Worker;

public interface IReplyScopeEvaluator
{
    ReplyScopeDecision Evaluate(IncomingEmail email);
}
