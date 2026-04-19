namespace EmailCopilot.Worker;

public sealed class WorkerOptions
{
    public bool Enabled { get; set; } = true;

    public int MaxDraftsPerRun { get; set; } = 1;
}
