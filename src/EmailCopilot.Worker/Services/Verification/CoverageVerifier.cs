using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public interface ICoverageVerifier
{
    Task<string?> VerifyAsync(
        string draftBody,
        string replyShape,
        EmailRequestAnalysis analysis,
        CancellationToken cancellationToken);
}

public sealed class CoverageVerifier : ICoverageVerifier
{
    public const string MissingWarningCode = "GENERATION_MISSING_REQUIRED_RESPONSE";
    public const string BorderlineWarningCode = "GENERATION_BORDERLINE_COVERAGE";

    private const double CoveredThreshold = 0.82;
    private const double BorderlineThreshold = 0.72;

    private static readonly Regex SentenceSplitter = new(
        @"(?<=[\.\!\?])\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogger<CoverageVerifier> _logger;

    public CoverageVerifier(IEmbeddingClient embeddingClient, ILogger<CoverageVerifier> logger)
    {
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<string?> VerifyAsync(
        string draftBody,
        string replyShape,
        EmailRequestAnalysis analysis,
        CancellationToken cancellationToken)
    {
        // Coverage verification only makes sense for shapes that are expected to
        // address the sender's asks. Decline / Acknowledge shapes intentionally
        // do not answer asks — running coverage checks against them produces
        // false-positive MISSING warnings.
        if (replyShape is not (ReplyShapes.DirectAnswer
            or ReplyShapes.ConfirmAndClose
            or ReplyShapes.ConfirmAndRequest))
        {
            return null;
        }

        if (analysis is null || analysis.Asks.Count == 0)
        {
            return null;
        }

        var requiredAsks = analysis.Asks
            .Where(ask => !ask.IsOptional && !string.IsNullOrWhiteSpace(ask.Text))
            .Select(ask => ask.Text.Trim())
            .ToArray();

        if (requiredAsks.Length == 0)
        {
            return null;
        }

        var sentences = SplitSentences(draftBody);
        if (sentences.Count == 0)
        {
            return MissingWarningCode;
        }

        IReadOnlyList<float[]> embeddings;
        try
        {
            var inputs = new List<string>(requiredAsks.Length + sentences.Count);
            inputs.AddRange(requiredAsks);
            inputs.AddRange(sentences);
            embeddings = await _embeddingClient.EmbedAsync(inputs, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Coverage verification failed; skipping.");
            return null;
        }

        if (embeddings.Count != requiredAsks.Length + sentences.Count)
        {
            _logger.LogWarning(
                "Embedding count mismatch: expected {Expected}, got {Actual}; skipping coverage verification.",
                requiredAsks.Length + sentences.Count,
                embeddings.Count);
            return null;
        }

        var askVectors = embeddings.Take(requiredAsks.Length).ToArray();
        var sentenceVectors = embeddings.Skip(requiredAsks.Length).ToArray();

        var hasMissing = false;
        var hasBorderline = false;

        for (var i = 0; i < askVectors.Length; i++)
        {
            var bestScore = double.MinValue;
            for (var j = 0; j < sentenceVectors.Length; j++)
            {
                var score = CosineSimilarity(askVectors[i], sentenceVectors[j]);
                if (score > bestScore)
                {
                    bestScore = score;
                }
            }

            _logger.LogInformation(
                "Coverage score for ask #{AskIndex} ({AskPreview}): {Score:0.000}.",
                i,
                Preview(requiredAsks[i]),
                bestScore);

            if (bestScore < BorderlineThreshold)
            {
                hasMissing = true;
            }
            else if (bestScore < CoveredThreshold)
            {
                hasBorderline = true;
            }
        }

        if (hasMissing)
        {
            return MissingWarningCode;
        }

        if (hasBorderline)
        {
            return BorderlineWarningCode;
        }

        return null;
    }

    private static List<string> SplitSentences(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        var collapsed = body.Replace("\r\n", "\n").Replace('\r', '\n');
        return SentenceSplitter
            .Split(collapsed)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0.0;
        }

        double dot = 0.0;
        double magA = 0.0;
        double magB = 0.0;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA <= 0.0 || magB <= 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private static string Preview(string text)
    {
        const int max = 60;
        return text.Length <= max ? text : text[..max] + "...";
    }
}
