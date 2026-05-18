namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ConfidenceTierTests
{
    [TestCase(0.0, ConfidenceTier.Low)]
    [TestCase(0.4, ConfidenceTier.Low)]
    [TestCase(0.5499, ConfidenceTier.Low)]
    [TestCase(0.55, ConfidenceTier.Medium)]
    [TestCase(0.6, ConfidenceTier.Medium)]
    [TestCase(0.7499, ConfidenceTier.Medium)]
    [TestCase(0.75, ConfidenceTier.High)]
    [TestCase(0.9, ConfidenceTier.High)]
    [TestCase(1.0, ConfidenceTier.High)]
    public void FromScore_returns_expected_tier(double score, ConfidenceTier expected)
    {
        Assert.That(ConfidenceTierClassifier.FromScore(score), Is.EqualTo(expected));
    }
}
