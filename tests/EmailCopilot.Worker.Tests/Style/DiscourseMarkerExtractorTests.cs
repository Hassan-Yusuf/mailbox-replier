namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class DiscourseMarkerExtractorTests
{
    [Test]
    public void Returns_empty_when_no_samples()
    {
        var result = DiscourseMarkerExtractor.Extract(Array.Empty<string>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Emits_marker_when_rate_meets_floor()
    {
        // 1 of 10 = 10% rate, above the 5% floor
        var samples = new List<string>
        {
            "Yeah, sounds good",
            "Tomorrow at 3",
            "Confirmed",
            "Got the email",
            "Will do",
            "On my way",
            "Sure",
            "Thanks for that",
            "All good",
            "See you then"
        };

        var result = DiscourseMarkerExtractor.Extract(samples);

        Assert.That(result, Has.Some.Matches<DiscourseMarkerObservation>(m => m.Marker == "yeah"));
    }

    [Test]
    public void Drops_marker_below_five_percent_floor()
    {
        // 1 of 30 = 3.3% rate, below the 5% floor
        var samples = new List<string> { "Yeah, sounds good" };
        for (var i = 0; i < 29; i++)
        {
            samples.Add("Confirmed.");
        }

        var result = DiscourseMarkerExtractor.Extract(samples);

        Assert.That(result.Select(m => m.Marker), Does.Not.Contain("yeah"));
    }

    [Test]
    public void Caps_emitted_markers_at_six()
    {
        // Every candidate appears in every sample → all qualify; cap should still be 6.
        var saturated = string.Join(" ", DiscourseMarkers.Candidates);
        var samples = Enumerable.Repeat(saturated, 10).ToArray();

        var result = DiscourseMarkerExtractor.Extract(samples);

        Assert.That(result.Count, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void Is_case_insensitive_and_word_bounded()
    {
        var samples = new List<string>
        {
            "YEAH that works",
            "yeah!!",
            "Yeah sure",
            "got nothing here"
        };

        var result = DiscourseMarkerExtractor.Extract(samples);

        Assert.That(result, Has.Some.Matches<DiscourseMarkerObservation>(m => m.Marker == "yeah" && m.Rate >= 0.75));
    }

    [Test]
    public void Counts_by_sample_not_by_occurrence()
    {
        // One chatty sample with many "anyway"s should not inflate the rate.
        var samples = new List<string>
        {
            "anyway anyway anyway anyway anyway anyway",
            "Sure",
            "Got it",
            "Confirmed"
        };

        var result = DiscourseMarkerExtractor.Extract(samples);

        var anyway = result.SingleOrDefault(m => m.Marker == "anyway");
        Assert.That(anyway, Is.Not.Null);
        Assert.That(anyway!.Rate, Is.EqualTo(0.25).Within(0.001));
    }
}
