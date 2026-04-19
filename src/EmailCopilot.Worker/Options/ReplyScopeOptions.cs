namespace EmailCopilot.Worker;

public sealed class ReplyScopeOptions
{
    public string Mode { get; set; } = ReplyScopeModes.All;

    public string[] AllowedDomains { get; set; } = [];
}
