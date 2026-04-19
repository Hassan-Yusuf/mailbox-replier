namespace EmailCopilot.Worker;

public interface IDraftEligibilityAssessor
{
    DraftEligibilityResult Assess(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis);
}
