using EmailCopilot.Worker;

namespace EmailCopilot.Worker.Tests.Infrastructure;

public sealed class EditDistanceTests
{
    [Test]
    public void Levenshtein_returns_zero_for_identical_strings()
    {
        Assert.That(EditDistance.Levenshtein("hello", "hello"), Is.EqualTo(0));
    }

    [Test]
    public void Levenshtein_returns_target_length_when_source_empty()
    {
        Assert.That(EditDistance.Levenshtein("", "hello"), Is.EqualTo(5));
    }

    [Test]
    public void Levenshtein_returns_source_length_when_target_empty()
    {
        Assert.That(EditDistance.Levenshtein("hello", ""), Is.EqualTo(5));
    }

    [Test]
    public void Levenshtein_counts_single_substitution()
    {
        Assert.That(EditDistance.Levenshtein("kitten", "sitten"), Is.EqualTo(1));
    }

    [Test]
    public void Levenshtein_counts_insertion()
    {
        Assert.That(EditDistance.Levenshtein("cat", "cats"), Is.EqualTo(1));
    }

    [Test]
    public void Levenshtein_counts_deletion()
    {
        Assert.That(EditDistance.Levenshtein("cats", "cat"), Is.EqualTo(1));
    }

    [Test]
    public void Levenshtein_classic_kitten_sitting_distance_is_three()
    {
        Assert.That(EditDistance.Levenshtein("kitten", "sitting"), Is.EqualTo(3));
    }

    [Test]
    public void Levenshtein_throws_on_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => EditDistance.Levenshtein(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => EditDistance.Levenshtein("x", null!));
    }
}
