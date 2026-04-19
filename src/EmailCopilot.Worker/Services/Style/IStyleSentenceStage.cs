namespace EmailCopilot.Worker;

public interface IStyleSentenceStage
{
    RuleEvaluation Evaluate(string sentence);
}
