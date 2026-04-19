using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ConfiguredExclusionClassifierTests
{
    [Test]
    public async Task Should_record_non_matching_rule_evaluations_before_a_match()
    {
        var classifier = CreateClassifier(
        [
            new ConfiguredExclusionRule
            {
                Name = "newsletter",
                SenderPatterns = ["newsletter@*"]
            },
            new ConfiguredExclusionRule
            {
                Name = "loc8me-viewing",
                DomainPatterns = ["loc8me.co.uk"],
                SubjectContains = ["Viewing"]
            }
        ]);

        var email = CreateEmail("enquiries@loc8me.co.uk", "Enquiries", "Viewing arranged");

        var result = await classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.DecisionSource, Is.EqualTo("CONFIGURED_RULE:loc8me-viewing"));
        Assert.That(result.Trace.Evaluations.Count, Is.EqualTo(2));
        Assert.That(result.Trace.Evaluations[0].RuleName, Is.EqualTo("CONFIGURED:newsletter"));
        Assert.That(result.Trace.Evaluations[0].Matched, Is.False);
        Assert.That(result.Trace.Evaluations[1].RuleName, Is.EqualTo("CONFIGURED:loc8me-viewing"));
        Assert.That(result.Trace.Evaluations[1].Matched, Is.True);
    }

    [Test]
    public async Task Should_emit_no_match_trace_entry_when_no_rules_match()
    {
        var classifier = CreateClassifier(
        [
            new ConfiguredExclusionRule
            {
                Name = "payments",
                SubjectContains = ["invoice"]
            }
        ]);

        var email = CreateEmail("person@example.com", "Person Example", "Hello there");

        var result = await classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
        Assert.That(result.Trace.Evaluations.Count, Is.EqualTo(2));
        Assert.That(result.Trace.Evaluations[0].Matched, Is.False);
        Assert.That(result.Trace.Evaluations[1].RuleName, Is.EqualTo("CONFIGURED:NO_MATCH"));
        Assert.That(result.Trace.Evaluations[1].Matched, Is.True);
    }

    private static ConfiguredExclusionClassifier CreateClassifier(
        IReadOnlyList<ConfiguredExclusionRule> rules)
    {
        var options = new ExclusionRulesOptions
        {
            Rules = rules.ToList()
        };

        return new ConfiguredExclusionClassifier(new TestOptionsMonitor(options));
    }

    private static IncomingEmail CreateEmail(string fromAddress, string displayName, string subject) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(fromAddress, displayName),
            subject,
            "Representative body text for configured-rule tests.",
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private sealed class TestOptionsMonitor : IOptionsMonitor<ExclusionRulesOptions>
    {
        public TestOptionsMonitor(ExclusionRulesOptions value)
        {
            CurrentValue = value;
        }

        public ExclusionRulesOptions CurrentValue { get; }

        public ExclusionRulesOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ExclusionRulesOptions, string?> listener) => null;
    }
}
