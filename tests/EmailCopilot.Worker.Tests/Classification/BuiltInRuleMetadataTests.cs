namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class BuiltInRuleMetadataTests
{
    private static readonly IReadOnlyList<BuiltInClassificationRule> AllRules =
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

    [Test]
    public void Every_rule_exposes_a_non_empty_description()
    {
        foreach (var rule in AllRules)
        {
            Assert.That(
                rule.Description,
                Is.Not.Null.And.Not.Empty,
                $"Rule {rule.RuleName} must have a non-empty Description");
            Assert.That(
                rule.Description.Trim(),
                Is.EqualTo(rule.Description),
                $"Rule {rule.RuleName} Description must not have leading/trailing whitespace");
        }
    }

    [Test]
    public void Every_rule_has_a_defined_category_value()
    {
        foreach (var rule in AllRules)
        {
            Assert.That(
                Enum.IsDefined(typeof(RuleCategory), rule.Category),
                Is.True,
                $"Rule {rule.RuleName} Category {rule.Category} is not a defined RuleCategory");
        }
    }

    [Test]
    public void Rule_names_are_unique()
    {
        var names = AllRules.Select(r => r.RuleName).ToList();
        Assert.That(names, Is.Unique);
    }

    [Test]
    public void Expected_category_assignments_match()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NoReplySenderRule().Category, Is.EqualTo(RuleCategory.AutomatedSender));
            Assert.That(new AutomatedSenderRule().Category, Is.EqualTo(RuleCategory.AutomatedSender));
            Assert.That(new BulkPromotionalRule().Category, Is.EqualTo(RuleCategory.BulkBroadcast));
            Assert.That(new ListOrBroadcastRule().Category, Is.EqualTo(RuleCategory.BulkBroadcast));
            Assert.That(new BroadcastFormattedRule().Category, Is.EqualTo(RuleCategory.BulkBroadcast));
            Assert.That(new SuspiciousSpamRule().Category, Is.EqualTo(RuleCategory.Suspicious));
            Assert.That(new WorkflowAcknowledgementRule().Category, Is.EqualTo(RuleCategory.Workflow));
            Assert.That(new TransactionalNotificationRule().Category, Is.EqualTo(RuleCategory.Transactional));
            Assert.That(new CustomerFeedbackSurveyRule().Category, Is.EqualTo(RuleCategory.Survey));
            Assert.That(new SocialDigestRule().Category, Is.EqualTo(RuleCategory.Social));
            Assert.That(new SelfServiceActionRule().Category, Is.EqualTo(RuleCategory.SelfService));
            Assert.That(new EffectivelyEmptyBodyRule().Category, Is.EqualTo(RuleCategory.LowSignal));
        });
    }
}
