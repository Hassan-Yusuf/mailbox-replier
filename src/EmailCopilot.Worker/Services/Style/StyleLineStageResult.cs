namespace EmailCopilot.Worker;

public sealed record StyleLineStageResult(StyleLineAction Action, string StageName, string? Detail = null)
{
    public static StyleLineStageResult Keep(string stageName) => new(StyleLineAction.Keep, stageName);
    public static StyleLineStageResult Skip(string stageName, string? detail = null) => new(StyleLineAction.Skip, stageName, detail);
    public static StyleLineStageResult Stop(string stageName, string? detail = null) => new(StyleLineAction.Stop, stageName, detail);
}
