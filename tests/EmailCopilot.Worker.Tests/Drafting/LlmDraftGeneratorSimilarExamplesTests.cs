namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class LlmDraftGeneratorSimilarExamplesTests
{
    [Test]
    public void Subject_with_substantial_content_after_re_prefix_is_used_as_query()
    {
        var email = BuildEmail(subject: "RE: Senior Sous Chef Opportunity at Colchester", body: "irrelevant");

        var query = LlmDraftGenerator.BuildSimilarityQueryTextForTesting(email);

        Assert.That(query, Is.EqualTo("Senior Sous Chef Opportunity at Colchester"));
    }

    [Test]
    public void Empty_subject_after_re_prefix_falls_back_to_body_when_body_is_substantial()
    {
        var email = BuildEmail(
            subject: "RE:",
            body: "Hi, we have shifts available next week for Friday and Saturday evenings. Can you do them?");

        var query = LlmDraftGenerator.BuildSimilarityQueryTextForTesting(email);

        Assert.That(query, Does.StartWith("Hi, we have shifts available"));
    }

    [Test]
    public void Junk_subject_and_thin_body_returns_null_so_caller_falls_back()
    {
        var email = BuildEmail(subject: "RE: Fwd:", body: "ok");

        var query = LlmDraftGenerator.BuildSimilarityQueryTextForTesting(email);

        Assert.That(query, Is.Null);
    }

    [Test]
    public void Fwd_prefix_is_stripped_case_insensitively()
    {
        var email = BuildEmail(subject: "fwd: Notification of Viewing 6b Havelock Street", body: string.Empty);

        var query = LlmDraftGenerator.BuildSimilarityQueryTextForTesting(email);

        Assert.That(query, Is.EqualTo("Notification of Viewing 6b Havelock Street"));
    }

    [Test]
    public async Task Similarity_ranks_examples_closest_to_query_first()
    {
        // Query vector aligned with the "shift" axis (index 0).
        var queryVector = new float[] { 1f, 0f, 0f };
        var farVector = new float[] { 0f, 1f, 0f };
        var midVector = new float[] { 0.7f, 0.7f, 0f };
        var nearVector = new float[] { 0.95f, 0.31f, 0f };

        var examples = new[]
        {
            new StyleExample("seg", "I'll send the invoice tomorrow.", "Invoice question", DateTimeOffset.UtcNow.AddDays(-3)),
            new StyleExample("seg", "Yes, I can do that shift, drop me the address.", "Shift request", DateTimeOffset.UtcNow.AddDays(-2)),
            new StyleExample("seg", "Hi, that shift sounds good — what time?", "Shift confirm", DateTimeOffset.UtcNow.AddDays(-1))
        };

        // Match order: query[0] (queryVector), then bodies in declared order.
        var fake = new FixedEmbeddingClient(new[]
        {
            queryVector,    // for queryText
            farVector,      // examples[0]
            midVector,      // examples[1]
            nearVector      // examples[2]
        });

        var ranked = await LlmDraftGenerator.RankExamplesBySimilarityAsync(
            "shift availability question",
            examples,
            fake,
            topN: 2,
            CancellationToken.None);

        Assert.That(ranked, Has.Count.EqualTo(2));
        Assert.That(ranked[0].SubjectHint, Is.EqualTo("Shift confirm"), "highest-cosine example should be first");
        Assert.That(ranked[1].SubjectHint, Is.EqualTo("Shift request"));
    }

    [Test]
    public async Task Tied_cosine_breaks_by_sent_at_desc()
    {
        // Both example vectors are identical to the query → cosine = 1 for both.
        var aligned = new float[] { 1f, 0f };

        var olderExample = new StyleExample("seg", "Older reply body", "Older", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newerExample = new StyleExample("seg", "Newer reply body", "Newer", new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        var fake = new FixedEmbeddingClient(new[] { aligned, aligned, aligned });

        var ranked = await LlmDraftGenerator.RankExamplesBySimilarityAsync(
            "query",
            new[] { olderExample, newerExample },
            fake,
            topN: 1,
            CancellationToken.None);

        Assert.That(ranked, Has.Count.EqualTo(1));
        Assert.That(ranked[0].SubjectHint, Is.EqualTo("Newer"));
    }

    [Test]
    public void Embed_input_count_mismatch_throws_so_caller_falls_back()
    {
        var queryVector = new float[] { 1f, 0f };
        var examples = new[]
        {
            new StyleExample("seg", "Body A", "A", DateTimeOffset.UtcNow.AddDays(-1)),
            new StyleExample("seg", "Body B", "B", DateTimeOffset.UtcNow)
        };

        // Returning fewer vectors than inputs → exception so SelectExamplesAsync falls back.
        var fake = new FixedEmbeddingClient(new[] { queryVector });

        Assert.That(
            async () => await LlmDraftGenerator.RankExamplesBySimilarityAsync(
                "query",
                examples,
                fake,
                topN: 2,
                CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());
    }

    private static IncomingEmail BuildEmail(string subject, string body) =>
        new(
            1,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts("sender@example.com", "Sender"),
            subject,
            body,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private sealed class FixedEmbeddingClient : IEmbeddingClient
    {
        private readonly IReadOnlyList<float[]> _vectors;

        public FixedEmbeddingClient(IReadOnlyList<float[]> vectors)
        {
            _vectors = vectors;
        }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            Task.FromResult(_vectors);
    }
}
