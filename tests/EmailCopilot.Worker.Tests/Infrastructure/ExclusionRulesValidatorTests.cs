namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ExclusionRulesValidatorTests
{
    private readonly ExclusionRulesValidator _validator = new();

    [Test]
    public void Should_report_enabled_rule_without_any_criteria()
    {
        var issues = _validator.Validate(
        [
            new ConfiguredExclusionRule
            {
                Name = "empty-enabled-rule",
                Enabled = true
            }
        ]);

        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0], Does.Contain("enabled but has no matching criteria"));
    }

    [Test]
    public void Should_report_duplicate_rule_names()
    {
        var issues = _validator.Validate(
        [
            new ConfiguredExclusionRule
            {
                Name = "repeat",
                SenderPatterns = ["alerts@*"]
            },
            new ConfiguredExclusionRule
            {
                Name = "repeat",
                SubjectContains = ["statement"]
            }
        ]);

        Assert.That(issues, Has.Some.Contains("duplicate Name"));
    }

    [Test]
    public void Should_allow_disabled_rule_without_criteria()
    {
        var issues = _validator.Validate(
        [
            new ConfiguredExclusionRule
            {
                Name = "disabled-placeholder",
                Enabled = false
            }
        ]);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Should_accept_well_formed_rule()
    {
        var issues = _validator.Validate(
        [
            new ConfiguredExclusionRule
            {
                Name = "loc8me-viewing",
                DomainPatterns = ["loc8me.co.uk"],
                SubjectContains = ["Viewing"]
            }
        ]);

        Assert.That(issues, Is.Empty);
    }
}
