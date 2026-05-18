namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class WorkflowOptionsTests
{
    [Test]
    public void Default_mode_is_review_before_send()
    {
        var options = new WorkflowOptions();
        Assert.That(options.Mode, Is.EqualTo(WorkflowModes.ReviewBeforeSend));
    }

    [TestCase("SuggestOnly", WorkflowModes.SuggestOnly)]
    [TestCase("suggestonly", WorkflowModes.SuggestOnly)]
    [TestCase("ReviewBeforeSend", WorkflowModes.ReviewBeforeSend)]
    [TestCase("reviewbeforesend", WorkflowModes.ReviewBeforeSend)]
    [TestCase("", WorkflowModes.ReviewBeforeSend)]
    [TestCase(null, WorkflowModes.ReviewBeforeSend)]
    [TestCase("AutoSend", WorkflowModes.ReviewBeforeSend)]
    public void Normalize_falls_back_to_review_before_send_for_unknown_values(string? input, string expected)
    {
        Assert.That(WorkflowModes.Normalize(input), Is.EqualTo(expected));
    }

    [TestCase("SuggestOnly", true)]
    [TestCase("ReviewBeforeSend", true)]
    [TestCase("suggestonly", true)]
    [TestCase("AutoSend", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsKnown_recognises_supported_modes(string? input, bool expected)
    {
        Assert.That(WorkflowModes.IsKnown(input), Is.EqualTo(expected));
    }
}
