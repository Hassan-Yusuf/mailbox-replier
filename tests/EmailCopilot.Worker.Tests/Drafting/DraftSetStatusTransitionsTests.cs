namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class DraftSetStatusTransitionsTests
{
    [TestCase(DraftSetStatuses.Pending, DraftSetStatuses.Approved)]
    [TestCase(DraftSetStatuses.Pending, DraftSetStatuses.Dismissed)]
    [TestCase(DraftSetStatuses.Approved, DraftSetStatuses.PushedToOutlook)]
    [TestCase(DraftSetStatuses.Approved, DraftSetStatuses.Dismissed)]
    [TestCase(DraftSetStatuses.PushedToOutlook, DraftSetStatuses.Sent)]
    public void Allowed_transitions_return_true(string from, string to)
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed(from, to), Is.True);
    }

    [TestCase(DraftSetStatuses.Pending, DraftSetStatuses.PushedToOutlook)]
    [TestCase(DraftSetStatuses.Pending, DraftSetStatuses.Sent)]
    [TestCase(DraftSetStatuses.Approved, DraftSetStatuses.Pending)]
    [TestCase(DraftSetStatuses.Approved, DraftSetStatuses.Sent)]
    [TestCase(DraftSetStatuses.PushedToOutlook, DraftSetStatuses.Pending)]
    [TestCase(DraftSetStatuses.PushedToOutlook, DraftSetStatuses.Approved)]
    [TestCase(DraftSetStatuses.PushedToOutlook, DraftSetStatuses.Dismissed)]
    [TestCase(DraftSetStatuses.Dismissed, DraftSetStatuses.Approved)]
    [TestCase(DraftSetStatuses.Dismissed, DraftSetStatuses.Pending)]
    [TestCase(DraftSetStatuses.Dismissed, DraftSetStatuses.PushedToOutlook)]
    [TestCase(DraftSetStatuses.Dismissed, DraftSetStatuses.Sent)]
    [TestCase(DraftSetStatuses.Sent, DraftSetStatuses.Pending)]
    [TestCase(DraftSetStatuses.Sent, DraftSetStatuses.Approved)]
    [TestCase(DraftSetStatuses.Sent, DraftSetStatuses.PushedToOutlook)]
    [TestCase(DraftSetStatuses.Sent, DraftSetStatuses.Dismissed)]
    public void Disallowed_transitions_return_false(string from, string to)
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed(from, to), Is.False);
    }

    [Test]
    public void Same_status_is_allowed_for_idempotent_updates()
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed(DraftSetStatuses.Pending, DraftSetStatuses.Pending), Is.True);
        Assert.That(DraftSetStatusTransitions.IsAllowed(DraftSetStatuses.Sent, DraftSetStatuses.Sent), Is.True);
    }

    [Test]
    public void Comparison_is_case_insensitive()
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed("pending", "approved"), Is.True);
        Assert.That(DraftSetStatusTransitions.IsAllowed("PENDING", "approved"), Is.True);
    }

    [Test]
    public void Unknown_from_status_is_rejected()
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed("UNKNOWN", DraftSetStatuses.Approved), Is.False);
    }

    [Test]
    public void Null_or_empty_inputs_are_rejected()
    {
        Assert.That(DraftSetStatusTransitions.IsAllowed(string.Empty, DraftSetStatuses.Approved), Is.False);
        Assert.That(DraftSetStatusTransitions.IsAllowed(DraftSetStatuses.Pending, string.Empty), Is.False);
        Assert.That(DraftSetStatusTransitions.IsAllowed("   ", DraftSetStatuses.Approved), Is.False);
    }
}
