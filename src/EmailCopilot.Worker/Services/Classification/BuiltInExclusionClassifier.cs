namespace EmailCopilot.Worker;

public sealed class BuiltInExclusionClassifier : IEmailClassifier
{
    private static readonly System.Text.RegularExpressions.Regex QuotedPrintablePattern =
        new(@"=[0-9A-Fa-f]{2}", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Evaluate the strongest structural "never draft this" signals first so the
    // classifier exits early on obvious machine-generated mail before trying
    // softer promotional or social heuristics.
    private readonly IReadOnlyList<IExclusionRule> _rules =
    [
        new NoReplySenderRule(),
        new CustomerFeedbackSurveyRule(),
        new ListOrBroadcastRule(),
        new BroadcastFormattedRule(),
        new TransactionalNotificationRule(),
        new SelfServiceActionRule(),
        new WorkflowAcknowledgementRule(),
        new SuspiciousSpamRule(),
        new AutomatedSenderRule(),
        new BulkPromotionalRule(),
        new EffectivelyEmptyBodyRule(),
        new SocialDigestRule()
    ];

    public Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<RuleEvaluation>(_rules.Count + 1);

        if (LooksLikeUndecodedMimeBody(email.BodyText))
        {
            evaluations.Add(new RuleEvaluation(
                "BUILT_IN:UNDECODED_MIME_BODY",
                true,
                ClassificationReasonCodes.UndecodedBody,
                ClassificationDecisionSources.BuiltIn,
                "quoted-printable artifacts detected in body text"));

            return Task.FromResult(new ClassificationResult(
                false,
                ClassificationReasonCodes.UndecodedBody,
                ClassificationDecisionSources.BuiltIn,
                new DecisionTrace(evaluations)));
        }

        foreach (var rule in _rules)
        {
            var evaluation = rule.Evaluate(email);
            evaluations.Add(evaluation);

            if (evaluation.Matched)
            {
                return Task.FromResult(new ClassificationResult(
                    false,
                    evaluation.ReasonCode,
                    evaluation.DecisionSource,
                    new DecisionTrace(evaluations)));
            }
        }

        evaluations.Add(new RuleEvaluation(
            "BUILT_IN:DEFAULT_REPLY",
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            "no built-in exclusion rules matched"));

        return Task.FromResult(new ClassificationResult(
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            new DecisionTrace(evaluations)));
    }

    private static bool LooksLikeUndecodedMimeBody(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return false;
        }

        if (bodyText.Contains("=3C", StringComparison.OrdinalIgnoreCase) ||
            bodyText.Contains("=3E", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return QuotedPrintablePattern.Matches(bodyText).Count >= 3;
    }
}
