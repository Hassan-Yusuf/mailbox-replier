namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class BuiltInExclusionClassifierToggleTests
{
    [Test]
    public async Task With_no_toggle_store_classifies_no_reply_sender_as_skipped()
    {
        var classifier = new BuiltInExclusionClassifier();

        var result = await classifier.ClassifyAsync(NoReplyEmail());

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.NoReplySender));
    }

    [Test]
    public async Task With_all_rules_enabled_classifies_no_reply_sender_as_skipped()
    {
        var classifier = new BuiltInExclusionClassifier(new InMemoryRuleToggleStore());

        var result = await classifier.ClassifyAsync(NoReplyEmail());

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.NoReplySender));
    }

    [Test]
    public async Task Disabled_rule_is_not_evaluated_and_emits_no_trace_entry()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        toggleStore.Set("BUILT_IN:NO_REPLY_PLATFORM_SENDER", false);
        var classifier = new BuiltInExclusionClassifier(toggleStore);

        var result = await classifier.ClassifyAsync(NoReplyEmail());

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
        Assert.That(
            result.Trace.Evaluations.Any(e => e.RuleName == "BUILT_IN:NO_REPLY_PLATFORM_SENDER"),
            Is.False,
            "Disabled rule should not appear in the decision trace.");
    }

    [Test]
    public async Task When_all_rules_are_disabled_default_reply_is_returned()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        var classifier = new BuiltInExclusionClassifier(toggleStore);
        foreach (var rule in classifier.Rules)
        {
            toggleStore.Set(rule.RuleName, false);
        }

        var result = await classifier.ClassifyAsync(NoReplyEmail());

        Assert.That(result.RequiresReply, Is.True);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.DefaultReply));
        Assert.That(result.Trace.Evaluations, Has.Count.EqualTo(1));
        Assert.That(result.Trace.Evaluations[0].RuleName, Is.EqualTo("BUILT_IN:DEFAULT_REPLY"));
    }

    [Test]
    public async Task Undecoded_mime_short_circuit_runs_even_when_all_rules_disabled()
    {
        var toggleStore = new InMemoryRuleToggleStore();
        var classifier = new BuiltInExclusionClassifier(toggleStore);
        foreach (var rule in classifier.Rules)
        {
            toggleStore.Set(rule.RuleName, false);
        }

        var email = CreateEmail(
            "person@example.com",
            "Person",
            "Encoded",
            "Hello=3Cbr=3EYour code is=20here=3D42");

        var result = await classifier.ClassifyAsync(email);

        Assert.That(result.RequiresReply, Is.False);
        Assert.That(result.ReasonCode, Is.EqualTo(ClassificationReasonCodes.UndecodedBody));
    }

    private static IncomingEmail NoReplyEmail() =>
        CreateEmail(
            "shipping@amazon.co.uk",
            "Amazon",
            "Quick question",
            "Hello, can you confirm the delivery window for my package?");

    private static IncomingEmail CreateEmail(
        string fromAddress,
        string displayName,
        string subject,
        string bodyText) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(fromAddress, displayName),
            subject,
            bodyText,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private sealed class InMemoryRuleToggleStore : IRuleToggleStore
    {
        private readonly Dictionary<string, bool> _toggles = new(StringComparer.Ordinal);

        public void Set(string ruleName, bool isEnabled) => _toggles[ruleName] = isEnabled;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, bool>> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, bool> snapshot = new Dictionary<string, bool>(_toggles, StringComparer.Ordinal);
            return Task.FromResult(snapshot);
        }

        public Task SetAsync(string ruleName, bool isEnabled, CancellationToken cancellationToken)
        {
            _toggles[ruleName] = isEnabled;
            return Task.CompletedTask;
        }
    }
}
