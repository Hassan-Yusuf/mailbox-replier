namespace EmailCopilot.Worker;

public interface IStyleLineStage
{
    StyleLineStageResult Evaluate(string trimmedLine, bool hasKeptContent);
}
