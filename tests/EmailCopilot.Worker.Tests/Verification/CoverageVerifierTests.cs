using Microsoft.Extensions.Logging.Abstractions;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class CoverageVerifierTests
{
    [Test]
    public async Task Returns_null_when_analysis_has_no_asks()
    {
        var verifier = new CoverageVerifier(new StubEmbeddingClient(), NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis([], [], [], UrgencyLevels.Low, false);

        var warning = await verifier.VerifyAsync("Some draft body.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    [Test]
    public async Task Returns_null_when_only_optional_asks_present()
    {
        var verifier = new CoverageVerifier(new StubEmbeddingClient(), NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Optional question?", AskTypes.Question, true)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Hi there. Thanks.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    [Test]
    public async Task Returns_missing_warning_when_best_score_below_borderline()
    {
        var stub = new StubEmbeddingClient((askIndex, sentenceIndex) => 0.50f);
        var verifier = new CoverageVerifier(stub, NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("I will reply later. Thanks.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.EqualTo(CoverageVerifier.MissingWarningCode));
    }

    [Test]
    public async Task Returns_borderline_warning_when_best_score_in_borderline_band()
    {
        var stub = new StubEmbeddingClient((askIndex, sentenceIndex) => 0.75f);
        var verifier = new CoverageVerifier(stub, NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Confirming the date now. Thanks.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.EqualTo(CoverageVerifier.BorderlineWarningCode));
    }

    [Test]
    public async Task Returns_null_when_best_score_above_covered_threshold()
    {
        var stub = new StubEmbeddingClient((askIndex, sentenceIndex) => 0.90f);
        var verifier = new CoverageVerifier(stub, NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Yes the date is confirmed. Thanks.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    [Test]
    public async Task Returns_null_when_embedding_client_throws()
    {
        var verifier = new CoverageVerifier(new ThrowingEmbeddingClient(), NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Some draft body. Thanks.", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    [Test]
    public async Task Returns_missing_warning_when_draft_body_has_no_sentences()
    {
        var verifier = new CoverageVerifier(new StubEmbeddingClient(), NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("   ", ReplyShapes.DirectAnswer, analysis, CancellationToken.None);

        Assert.That(warning, Is.EqualTo(CoverageVerifier.MissingWarningCode));
    }

    [Test]
    public async Task Returns_null_when_reply_shape_is_decline()
    {
        var stub = new StubEmbeddingClient((askIndex, sentenceIndex) => 0.50f);
        var verifier = new CoverageVerifier(stub, NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Sorry I cannot help. Thanks.", ReplyShapes.Decline, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    [Test]
    public async Task Returns_null_when_reply_shape_is_acknowledge()
    {
        var stub = new StubEmbeddingClient((askIndex, sentenceIndex) => 0.50f);
        var verifier = new CoverageVerifier(stub, NullLogger<CoverageVerifier>.Instance);
        var analysis = new EmailRequestAnalysis(
            [new EmailAsk("Please confirm date.", AskTypes.Question, false)],
            [],
            [],
            UrgencyLevels.Low,
            false);

        var warning = await verifier.VerifyAsync("Thanks, noted. Speak soon.", ReplyShapes.Acknowledge, analysis, CancellationToken.None);

        Assert.That(warning, Is.Null);
    }

    private sealed class StubEmbeddingClient : IEmbeddingClient
    {
        private readonly Func<int, int, float>? _scoreOverride;

        public StubEmbeddingClient(Func<int, int, float>? scoreOverride = null)
        {
            _scoreOverride = scoreOverride;
        }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
        {
            // Build orthogonal-ish vectors per input so cosine = 1 with self, 0 otherwise — then
            // override pairwise dot product if scoreOverride is provided. We achieve target
            // cosine X by mixing two unit dimensions: ask gets [1,0,0,...]; sentence gets [X, sqrt(1-X^2), 0,...].
            var dim = Math.Max(8, inputs.Count + 4);
            var vectors = new List<float[]>(inputs.Count);

            // Find which inputs are asks vs sentences. We can't know structure here, but
            // CoverageVerifier passes asks first then sentences — so we'll use a marker: caller
            // must split via the score function which receives indices anyway. Simpler: produce
            // vectors where vectors[i] = unit on dim i, and rely on scoreOverride==null for
            // identity test (no-asks/optional/empty paths don't reach embed call anyway).
            for (var i = 0; i < inputs.Count; i++)
            {
                var v = new float[dim];
                v[i % dim] = 1f;
                vectors.Add(v);
            }

            if (_scoreOverride is null)
            {
                return Task.FromResult<IReadOnlyList<float[]>>(vectors);
            }

            // Re-engineer: assume single ask at index 0, then sentences. Build vectors so that
            // cosine(ask, sentence_j) = scoreOverride(0, j). Use 2D embedding for clarity.
            var rebuilt = new List<float[]>(inputs.Count);
            // ask at index 0:
            rebuilt.Add(new[] { 1f, 0f });
            for (var j = 1; j < inputs.Count; j++)
            {
                var target = _scoreOverride(0, j - 1);
                target = Math.Clamp(target, -1f, 1f);
                var orth = (float)Math.Sqrt(Math.Max(0, 1 - target * target));
                rebuilt.Add(new[] { target, orth });
            }

            return Task.FromResult<IReadOnlyList<float[]>>(rebuilt);
        }
    }

    private sealed class ThrowingEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
            => throw new InvalidOperationException("simulated embedding failure");
    }
}
