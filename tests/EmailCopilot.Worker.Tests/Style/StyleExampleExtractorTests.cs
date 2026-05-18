namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class StyleExampleExtractorTests
{
    private readonly StyleExampleExtractor _extractor = new(new StyleAuthoredBodyPipeline());

    [Test]
    public async Task ExtractAsync_should_skip_invalid_samples_keep_most_recent_duplicate_and_cap_results()
    {
        var duplicateBody = """
            Hi Sam,

            I can send that over this afternoon once I have it ready.
            """;

        var samples = new List<SentEmailSample>
        {
            CreateSample("", duplicateBody, new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero)),
            CreateSample("Too short", "Hi there.", new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero)),
            CreateSample("Too many sentences", "One. Two. Three. Four. Five. Six.", new DateTimeOffset(2026, 4, 1, 11, 0, 0, TimeSpan.Zero)),
            CreateSample("Older duplicate", duplicateBody, new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero)),
            CreateSample("Newer duplicate", duplicateBody, new DateTimeOffset(2026, 4, 30, 9, 0, 0, TimeSpan.Zero))
        };

        for (var index = 0; index < 14; index++)
        {
            samples.Add(CreateSample(
                $"Unique {index}",
                index switch
                {
                    0 => "Hi Sam,\n\nI'll send the tenancy form later today once I've finished checking it over.",
                    1 => "Hi Sam,\n\nThat viewing time works for me and I'll head over straight after lunch.",
                    2 => "Hi Sam,\n\nI've attached the invoice now and the payment should show by tomorrow morning.",
                    3 => "Hi Sam,\n\nI can cover the Saturday shift if you still need someone for it.",
                    4 => "Hi Sam,\n\nThe hearing bundle looks fine from my side and I'll bring a printed copy.",
                    5 => "Hi Sam,\n\nI've asked maintenance to look at the leak and I'll update you once they've been.",
                    6 => "Hi Sam,\n\nI won't be able to make that slot so I'll send over another time shortly.",
                    7 => "Hi Sam,\n\nPlease go ahead and book that in because the revised quote looks fine.",
                    8 => "Hi Sam,\n\nI've sent the photos across now and the damaged chair is in room three.",
                    9 => "Hi Sam,\n\nThat role sounds good to me and I'll send my CV over this evening.",
                    10 => "Hi Sam,\n\nI can do the Monday morning appointment if that is still available.",
                    11 => "Hi Sam,\n\nI've read through the paperwork and there isn't anything else I need at this stage.",
                    12 => "Hi Sam,\n\nThe keys have been dropped back in and everything is locked up again.",
                    _ => "Hi Sam,\n\nI spoke with the tenant earlier and they're happy with the revised arrangement."
                },
                new DateTimeOffset(2026, 4, 4 + index, 9, 0, 0, TimeSpan.Zero),
                $"domain{index % 5}.example"));
        }

        var examples = await _extractor.ExtractAsync("domain:example.com", samples, CancellationToken.None);

        Assert.That(examples, Has.Count.EqualTo(12));
        Assert.That(examples.All(static example => example.SegmentKey == "domain:example.com"), Is.True);
        Assert.That(examples.Any(static example => example.SubjectHint == "Newer duplicate"), Is.True);
        Assert.That(examples.Any(static example => example.SubjectHint == "Older duplicate"), Is.False);
        Assert.That(examples.Any(static example => example.SubjectHint == "Too short"), Is.False);
        Assert.That(examples.Any(static example => string.IsNullOrWhiteSpace(example.SubjectHint)), Is.False);
        Assert.That(
            examples.Select(static example => example.SentAtUtc),
            Is.EqualTo(examples.Select(static example => example.SentAtUtc).OrderByDescending(static value => value)));
    }

    [Test]
    public async Task ExtractAsync_should_strip_quoted_history_through_authored_body_pipeline()
    {
        var sample = CreateSample(
            "Reply",
            """
            Hi Sam,

            I can do tomorrow afternoon if that still works for you.

            From: Old Thread <old@example.com>
            Subject: Previous discussion
            """,
            new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero));

        var examples = await _extractor.ExtractAsync("domain:example.com", [sample], CancellationToken.None);

        Assert.That(examples, Has.Count.EqualTo(1));
        Assert.That(examples[0].ExampleBody, Does.Not.Contain("From: Old Thread"));
        Assert.That(examples[0].ExampleBody, Does.Contain("I can do tomorrow afternoon"));
    }

    [Test]
    public async Task ExtractAsync_should_cap_examples_per_recipient_domain()
    {
        var samples = Enumerable.Range(0, 10)
            .Select(index => CreateSample(
                $"Unique {index}",
                index switch
                {
                    0 => "Hi Sam,\n\nI'll send the tenancy form later today once I've finished checking it over.",
                    1 => "Hi Sam,\n\nThat viewing time works for me and I'll head over straight after lunch.",
                    2 => "Hi Sam,\n\nI've attached the invoice now and the payment should show by tomorrow morning.",
                    3 => "Hi Sam,\n\nI can cover the Saturday shift if you still need someone for it.",
                    4 => "Hi Sam,\n\nThe hearing bundle looks fine from my side and I'll bring a printed copy.",
                    5 => "Hi Sam,\n\nI've asked maintenance to look at the leak and I'll update you once they've been.",
                    6 => "Hi Sam,\n\nI won't be able to make that slot so I'll send over another time shortly.",
                    7 => "Hi Sam,\n\nPlease go ahead and book that in because the revised quote looks fine.",
                    8 => "Hi Sam,\n\nI've sent the photos across now and the damaged chair is in room three.",
                    _ => "Hi Sam,\n\nThe keys have been dropped back in and everything is locked up again."
                },
                new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero).AddHours(index),
                "parexel.com"))
            .ToArray();

        var examples = await _extractor.ExtractAsync("relationship-professional", samples, CancellationToken.None);

        Assert.That(examples, Has.Count.EqualTo(3));
        Assert.That(examples.All(static example => example.SegmentKey == "relationship-professional"), Is.True);
    }

    [Test]
    public async Task ExtractAsync_should_collapse_whitespace_only_duplicates_to_a_single_entry()
    {
        var baseBody = "Hi Sam,\n\nI can send that over this afternoon once I have it ready.";
        var whitespaceVariant = "Hi  Sam,\r\n\r\nI can  send that over this   afternoon once I have it ready.";

        var samples = new[]
        {
            CreateSample("Original", baseBody, new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero)),
            CreateSample("Whitespace variant", whitespaceVariant, new DateTimeOffset(2026, 4, 11, 9, 0, 0, TimeSpan.Zero))
        };

        var examples = await _extractor.ExtractAsync("domain:example.com", samples, CancellationToken.None);

        Assert.That(examples, Has.Count.EqualTo(1));
        Assert.That(examples[0].SubjectHint, Is.EqualTo("Whitespace variant"));
    }

    private static SentEmailSample CreateSample(string subject, string body, DateTimeOffset sentAtUtc, string recipientDomain = "example.com") =>
        new(
            Guid.NewGuid().ToString("N"),
            subject,
            "sam@example.com",
            recipientDomain,
            body,
            sentAtUtc);
}
