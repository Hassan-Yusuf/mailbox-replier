namespace EmailCopilot.Worker;

public sealed class SelfServiceActionRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:SELF_SERVICE_ACTION";
    public override string ReasonCode => ClassificationReasonCodes.SelfServiceAction;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var subject = email.Subject.Trim().ToLowerInvariant();
        var body = email.BodyText.Trim().ToLowerInvariant();

        if (ContainsAny(
                subject,
                "action required",
                "renewal of consent",
                "renew your consent",
                "write a review for",
                "profile is no longer being shared",
                "job profile is no longer being shared",
                "mark yourself as actively looking",
                "oauth application approval",
                "access your data",
                "download your data",
                "request your data",
                "export your data",
                "verify your",
                "confirm your",
                "complete your",
                "complete your profile",
                "reactivate",
                "get started",
                "reset your password",
                "trip has been automatically parked",
                "trip ended automatically"))
        {
            return Matched(RuleName, ReasonCode, $"subject={subject}");
        }

        if (ContainsAny(
                body,
                "please login to your candidate portal",
                "renew your consent",
                "follow the instructions below",
                "click the button below",
                "use the link below",
                "complete this action",
                "mark your application as actively looking",
                "update your profile",
                "complete your profile",
                "personal profile",
                "introducing yourself on your personal profile",
                "authorized to have access",
                "revoke access",
                "access your data",
                "download a copy of your data",
                "request a copy of your data",
                "export your data",
                "data portability",
                "finish setting up your account",
                "candidate portal",
                "verify your email",
                "reset your password",
                "keep your account active",
                "be considered for future opportunities",
                "last account activity was",
                "previously registered for positions",
                "your feedback is private until they review you too",
                "write a review",
                "leave a review",
                "open the app to unpause your ride",
                "your ride will end in",
                "unpause your ride",
                "trip ended due to",
                "minutes of inactivity",
                "help ending your ride"))
        {
            return Matched(RuleName, ReasonCode, "self-service-body");
        }

        return NotMatched(RuleName, ReasonCode, $"subject={subject}");
    }
}
