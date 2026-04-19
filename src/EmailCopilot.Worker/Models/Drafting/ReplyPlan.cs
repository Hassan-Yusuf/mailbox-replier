namespace EmailCopilot.Worker;

public sealed record ReplyPlan(
    IReadOnlyList<ReplyShapeOption> Options);
