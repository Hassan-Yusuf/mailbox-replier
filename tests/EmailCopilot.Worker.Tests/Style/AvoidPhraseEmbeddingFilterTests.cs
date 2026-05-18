using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class AvoidPhraseEmbeddingFilterTests
{
    [Test]
    public async Task Disabled_filter_returns_disabled_status_without_calling_embedding_client()
    {
        var (filter, fakeClient) = BuildFilter(enabled: false);

        var result = await filter.EvaluateAsync(
            "I'll check my availability and let you know.",
            ReplyShapes.Acknowledge,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Disabled));
        Assert.That(fakeClient.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Sentence_within_threshold_to_any_seed_is_rejected()
    {
        var seedVector = new float[] { 1f, 0f, 0f };
        var sentenceVector = new float[] { 1f, 0.05f, 0f };
        var fakeClient = new FakeEmbeddingClient(sentenceVector);
        var embeddings = new AvoidPhraseEmbeddings(new[]
        {
            new AvoidPhraseCentroid(
                new AvoidPhraseFamily(
                    "schedule-check-stall",
                    new[] { "I'll check my schedule and get back to you" },
                    new[] { ReplyShapes.Acknowledge }),
                new[] { seedVector })
        });
        var filter = BuildFilterWith(fakeClient, embeddings, threshold: 0.82);

        var result = await filter.EvaluateAsync(
            "I'll check my availability and get back to you.",
            ReplyShapes.Acknowledge,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Rejected));
        Assert.That(result.MatchedFamilyId, Is.EqualTo("schedule-check-stall"));
    }

    [Test]
    public async Task Family_with_multiple_seeds_takes_max_cosine()
    {
        var firstSeed = new float[] { 1f, 0f, 0f };   // cos with sentenceVector = 0
        var secondSeed = new float[] { 0f, 1f, 0f };  // cos with sentenceVector = 1 (perfect match)
        var sentenceVector = new float[] { 0f, 1f, 0f };
        var fakeClient = new FakeEmbeddingClient(sentenceVector);
        var embeddings = new AvoidPhraseEmbeddings(new[]
        {
            new AvoidPhraseCentroid(
                new AvoidPhraseFamily(
                    "schedule-check-stall",
                    new[]
                    {
                        "I'll check my schedule and get back to you",
                        "I'll check my availability and get back to you"
                    },
                    new[] { ReplyShapes.Acknowledge }),
                new[] { firstSeed, secondSeed })
        });
        var filter = BuildFilterWith(fakeClient, embeddings, threshold: 0.82);

        var result = await filter.EvaluateAsync(
            "I'll check my availability for those shifts and get back to you.",
            ReplyShapes.Acknowledge,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Rejected));
        Assert.That(result.Cosine, Is.GreaterThanOrEqualTo(0.99));
    }

    [Test]
    public async Task Sentence_far_from_all_seeds_is_clean()
    {
        var seedVector = new float[] { 1f, 0f, 0f };
        var sentenceVector = new float[] { 0f, 1f, 0f };
        var fakeClient = new FakeEmbeddingClient(sentenceVector);
        var embeddings = new AvoidPhraseEmbeddings(new[]
        {
            new AvoidPhraseCentroid(
                new AvoidPhraseFamily(
                    "schedule-check-stall",
                    new[] { "I'll check my schedule and get back to you" },
                    new[] { ReplyShapes.Acknowledge }),
                new[] { seedVector })
        });
        var filter = BuildFilterWith(fakeClient, embeddings, threshold: 0.82);

        var result = await filter.EvaluateAsync(
            "Yeah I can do Tuesday at three.",
            ReplyShapes.Acknowledge,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Clean));
    }

    [Test]
    public async Task Anti_centroid_rule_skips_family_when_user_marker_overlaps_any_seed()
    {
        var seedVector = new float[] { 1f, 0f, 0f };
        var sentenceVector = new float[] { 1f, 0.05f, 0f };
        var fakeClient = new FakeEmbeddingClient(sentenceVector);
        var embeddings = new AvoidPhraseEmbeddings(new[]
        {
            new AvoidPhraseCentroid(
                new AvoidPhraseFamily(
                    "warm-thanks-closer",
                    new[] { "Thanks so much, cheers!", "All the best" },
                    new[] { ReplyShapes.Acknowledge }),
                new[] { seedVector, seedVector })
        });
        var filter = BuildFilterWith(fakeClient, embeddings, threshold: 0.82);

        var result = await filter.EvaluateAsync(
            "Thanks so much, cheers.",
            ReplyShapes.Acknowledge,
            new[] { "cheers" },
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Clean));
        Assert.That(fakeClient.CallCount, Is.EqualTo(0), "marker overlap with any seed in the family should skip the whole family before embed");
    }

    [Test]
    public async Task Sentence_under_four_words_is_not_checked()
    {
        var seedVector = new float[] { 1f, 0f, 0f };
        var fakeClient = new FakeEmbeddingClient(new float[] { 1f, 0f, 0f });
        var embeddings = new AvoidPhraseEmbeddings(new[]
        {
            new AvoidPhraseCentroid(
                new AvoidPhraseFamily(
                    "generic-opener",
                    new[] { "I hope this email finds you well" },
                    new[] { ReplyShapes.Acknowledge }),
                new[] { seedVector })
        });
        var filter = BuildFilterWith(fakeClient, embeddings, threshold: 0.82);

        var result = await filter.EvaluateAsync(
            "Got it.",
            ReplyShapes.Acknowledge,
            Array.Empty<string>(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(AvoidFilterStatus.Clean));
        Assert.That(fakeClient.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void Cosine_is_one_for_identical_vectors()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 1f, 2f, 3f };

        Assert.That(AvoidPhraseEmbeddingFilter.Cosine(a, b), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void Cosine_is_zero_for_orthogonal_vectors()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };

        Assert.That(AvoidPhraseEmbeddingFilter.Cosine(a, b), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Marker_overlap_is_case_insensitive_and_word_bounded()
    {
        Assert.That(
            AvoidPhraseEmbeddingFilter.MarkerOverlapsAnySeed(
                new[] { "Thanks so much, cheers!" }, new[] { "Cheers" }),
            Is.True);
        Assert.That(
            AvoidPhraseEmbeddingFilter.MarkerOverlapsAnySeed(
                new[] { "Thanks for the cheerful note" }, new[] { "cheer" }),
            Is.False);
    }

    [Test]
    public void Marker_overlap_catches_match_in_any_seed_of_family()
    {
        // First seed has no overlap, second seed has overlap — must still return true.
        Assert.That(
            AvoidPhraseEmbeddingFilter.MarkerOverlapsAnySeed(
                new[] { "Looking forward to hearing from you", "Cheers and thanks" },
                new[] { "cheers" }),
            Is.True);
    }

    private static AvoidPhraseEmbeddingFilter BuildFilterWith(
        IEmbeddingClient client,
        AvoidPhraseEmbeddings embeddings,
        double threshold) =>
        new(
            client,
            embeddings,
            Options.Create(new LlmOptions
            {
                AvoidFilter = new AvoidFilterOptions { Enabled = true, CosineThreshold = threshold }
            }));

    private static (AvoidPhraseEmbeddingFilter filter, FakeEmbeddingClient client) BuildFilter(bool enabled)
    {
        var fakeClient = new FakeEmbeddingClient(new float[] { 0f });
        var embeddings = new AvoidPhraseEmbeddings(Array.Empty<AvoidPhraseCentroid>());
        var filter = new AvoidPhraseEmbeddingFilter(
            fakeClient,
            embeddings,
            Options.Create(new LlmOptions
            {
                AvoidFilter = new AvoidFilterOptions { Enabled = enabled }
            }));
        return (filter, fakeClient);
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        private readonly float[] _fixedVector;

        public FakeEmbeddingClient(float[] fixedVector)
        {
            _fixedVector = fixedVector;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
        {
            CallCount += inputs.Count;
            var vectors = Enumerable.Repeat(_fixedVector, inputs.Count).ToArray();
            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        }
    }
}
