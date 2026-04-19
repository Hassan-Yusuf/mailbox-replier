namespace EmailCopilot.Worker;

public interface IReplyShapePlanner
{
    ReplyPlan Plan(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis);
}
