namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class GreetingPolicyTests
{
    private readonly GreetingPolicy _policy = new();

    [Test]
    public void Brand_name_display_name_should_not_be_treated_as_human_sender()
    {
        var sender = EmailAddress.FromParts("bbc@bbc.co.uk", "BBC");

        var name = _policy.DetermineSenderFirstName(sender);

        Assert.That(name, Is.Null);
    }

    [Test]
    public void Human_display_name_should_be_extracted()
    {
        var sender = EmailAddress.FromParts("sophie@cateringelite.co.uk", "Sophie Carter");

        var name = _policy.DetermineSenderFirstName(sender);

        Assert.That(name, Is.EqualTo("Sophie"));
    }

    [Test]
    public void Non_matching_greeting_should_fall_back_to_generic_hi()
    {
        var sender = EmailAddress.FromParts("bbc@bbc.co.uk", "BBC");

        var normalized = _policy.NormalizeGreeting("Hi BBC,\n\nI'll fill it in.", sender);

        Assert.That(normalized.StartsWith("Hi,\n", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void Generic_alias_display_name_should_not_be_treated_as_human_sender()
    {
        var sender = EmailAddress.FromParts("enquiries@loc8me.co.uk", "Enquiries");

        var name = _policy.DetermineSenderFirstName(sender);

        Assert.That(name, Is.Null);
    }

    [Test]
    public void Null_display_name_should_not_fall_back_to_single_token_brand_local_part()
    {
        var sender = EmailAddress.FromParts("bbc@bbc.co.uk", "");

        var name = _policy.DetermineSenderFirstName(sender);

        Assert.That(name, Is.Null);
    }

    [Test]
    public void Comma_delimited_person_name_should_still_extract_first_name()
    {
        var sender = EmailAddress.FromParts("sophie@cateringelite.co.uk", "Sophie, Catering Elite");

        var name = _policy.DetermineSenderFirstName(sender);

        Assert.That(name, Is.EqualTo("Sophie"));
    }

    [Test]
    public void Matching_human_greeting_should_be_preserved()
    {
        var sender = EmailAddress.FromParts("sophie@cateringelite.co.uk", "Sophie Carter");

        var normalized = _policy.NormalizeGreeting("Hi Sophie,\n\nI'm available.", sender);

        Assert.That(normalized.StartsWith("Hi Sophie,\n", StringComparison.Ordinal), Is.True);
    }
}
