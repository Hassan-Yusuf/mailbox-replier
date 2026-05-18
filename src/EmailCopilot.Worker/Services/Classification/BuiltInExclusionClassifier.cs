namespace EmailCopilot.Worker;

public sealed class BuiltInExclusionClassifier : IEmailClassifier
{
    // URL query strings produce incidental "=XX" hex matches (e.g. "?ref_=fed_yo_default"
    // contains =fe). We strip URLs before running the broad QP scan so these accidental
    // fragments never reach the detector.
    private static readonly System.Text.RegularExpressions.Regex UrlPattern =
        new(@"\bhttps?://\S+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Broad QP-escape pattern. Run only on URL-stripped text so the threshold can stay tight.
    private static readonly System.Text.RegularExpressions.Regex QpTokenPattern =
        new(@"=[0-9A-Fa-f]{2}",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Consecutive multi-byte UTF-8 in QP (e.g. =E2=80=99 for a curly apostrophe) is an
    // unambiguous "actually encoded" signal even when only one pair appears.
    private static readonly System.Text.RegularExpressions.Regex ConsecutiveHighByteQpPattern =
        new(@"=[89A-Fa-f][0-9A-Fa-f]=[89A-Fa-f][0-9A-Fa-f]",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SoftLineBreakPattern =
        new(@"=\r?\n", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

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

    private readonly IRuleToggleStore? _ruleToggleStore;

    public BuiltInExclusionClassifier(IRuleToggleStore? ruleToggleStore = null)
    {
        _ruleToggleStore = ruleToggleStore;
    }

    public IReadOnlyList<IExclusionRule> Rules => _rules;

    public async Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default)
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

            return new ClassificationResult(
                false,
                ClassificationReasonCodes.UndecodedBody,
                ClassificationDecisionSources.BuiltIn,
                new DecisionTrace(evaluations));
        }

        var toggles = _ruleToggleStore is null
            ? RuleToggleEvaluator.AllEnabled
            : new RuleToggleEvaluator(await _ruleToggleStore.LoadSnapshotAsync(cancellationToken));

        foreach (var rule in _rules)
        {
            if (!toggles.IsEnabled(rule.RuleName))
            {
                continue;
            }

            var evaluation = rule.Evaluate(email);
            evaluations.Add(evaluation);

            if (evaluation.Matched)
            {
                return new ClassificationResult(
                    false,
                    evaluation.ReasonCode,
                    evaluation.DecisionSource,
                    new DecisionTrace(evaluations));
            }
        }

        evaluations.Add(new RuleEvaluation(
            "BUILT_IN:DEFAULT_REPLY",
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            "no built-in exclusion rules matched"));

        return new ClassificationResult(
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            new DecisionTrace(evaluations));
    }

    private static bool LooksLikeUndecodedMimeBody(string bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return false;
        }

        // Soft line breaks (`=\n` at end of line) are unambiguous QP signal.
        if (SoftLineBreakPattern.IsMatch(bodyText))
        {
            return true;
        }

        // =3C / =3E (<, >) only appear when HTML tags survive into a "plain" body — strong signal.
        if (bodyText.Contains("=3C", StringComparison.OrdinalIgnoreCase) ||
            bodyText.Contains("=3E", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Consecutive high-byte pairs (=E2=80=99) are how multi-byte UTF-8 looks in real QP.
        // A single pair is enough — URL params never produce back-to-back =XX=XX.
        if (ConsecutiveHighByteQpPattern.IsMatch(bodyText))
        {
            return true;
        }

        // Strip URLs first so Amazon/Quora/LinkedIn affiliate links don't poison the count,
        // then count any remaining =XX tokens. Three or more isolated escapes (e.g. Latin-1
        // bodies like "caf=E9" with sparse high bytes) reliably indicate undecoded MIME.
        var cleaned = UrlPattern.Replace(bodyText, string.Empty);
        return QpTokenPattern.Matches(cleaned).Count >= 3;
    }
}
