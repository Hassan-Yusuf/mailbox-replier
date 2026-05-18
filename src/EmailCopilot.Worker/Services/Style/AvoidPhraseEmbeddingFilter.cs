using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed record AvoidFilterResult(
    AvoidFilterStatus Status,
    string? OffendingSentence,
    string? MatchedFamilyId,
    double? Cosine);

public enum AvoidFilterStatus
{
    Disabled,
    Clean,
    Rejected
}

public sealed class AvoidPhraseEmbeddingFilter
{
    private static readonly Regex SentenceSplit = new(@"(?<=[\.\!\?])\s+", RegexOptions.Compiled);
    private const int MinWordsForCheck = 4;

    private readonly IEmbeddingClient _embeddingClient;
    private readonly AvoidPhraseEmbeddings _embeddings;
    private readonly LlmOptions _options;
    private readonly ILogger<AvoidPhraseEmbeddingFilter>? _logger;

    public AvoidPhraseEmbeddingFilter(
        IEmbeddingClient embeddingClient,
        AvoidPhraseEmbeddings embeddings,
        IOptions<LlmOptions> options,
        ILogger<AvoidPhraseEmbeddingFilter>? logger = null)
    {
        _embeddingClient = embeddingClient;
        _embeddings = embeddings;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AvoidFilterResult> EvaluateAsync(
        string draftBody,
        string replyShape,
        IReadOnlyList<string> userMarkerWords,
        CancellationToken cancellationToken)
    {
        if (!_options.AvoidFilter.Enabled)
        {
            return new AvoidFilterResult(AvoidFilterStatus.Disabled, null, null, null);
        }

        var applicable = _embeddings.ForShape(replyShape)
            .Where(c => !MarkerOverlapsAnySeed(c.Family.SeedTexts, userMarkerWords))
            .ToArray();

        if (applicable.Length == 0)
        {
            return new AvoidFilterResult(AvoidFilterStatus.Clean, null, null, null);
        }

        var sentences = SplitSentences(draftBody)
            .Where(s => CountWords(s) >= MinWordsForCheck)
            .ToArray();

        if (sentences.Length == 0)
        {
            return new AvoidFilterResult(AvoidFilterStatus.Clean, null, null, null);
        }

        var sentenceVectors = await _embeddingClient.EmbedAsync(sentences, cancellationToken);
        var threshold = _options.AvoidFilter.CosineThreshold;

        for (var i = 0; i < sentences.Length; i++)
        {
            foreach (var centroid in applicable)
            {
                var bestCosine = 0.0;
                foreach (var seedVector in centroid.Vectors)
                {
                    var cosine = Cosine(sentenceVectors[i], seedVector);
                    if (cosine > bestCosine)
                    {
                        bestCosine = cosine;
                    }
                }

                if (bestCosine >= threshold)
                {
                    _logger?.LogInformation(
                        "Avoid filter rejected sentence \"{Sentence}\" matched family {FamilyId} at cosine {Cosine:F3} (threshold {Threshold:F3}).",
                        sentences[i],
                        centroid.Family.FamilyId,
                        bestCosine,
                        threshold);

                    return new AvoidFilterResult(
                        AvoidFilterStatus.Rejected,
                        sentences[i],
                        centroid.Family.FamilyId,
                        bestCosine);
                }
            }
        }

        return new AvoidFilterResult(AvoidFilterStatus.Clean, null, null, null);
    }

    internal static bool MarkerOverlapsAnySeed(IReadOnlyList<string> seedTexts, IReadOnlyList<string> userMarkerWords)
    {
        if (userMarkerWords.Count == 0)
        {
            return false;
        }

        foreach (var seed in seedTexts)
        {
            foreach (var marker in userMarkerWords)
            {
                if (string.IsNullOrWhiteSpace(marker))
                {
                    continue;
                }

                var pattern = $@"\b{Regex.Escape(marker)}\b";
                if (Regex.IsMatch(seed, pattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> SplitSentences(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        return SentenceSplit.Split(body.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
    }

    internal static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
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

        if (magA == 0.0 || magB == 0.0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private static int CountWords(string value) =>
        Regex.Matches(value, @"\b[\p{L}\p{N}']+\b", RegexOptions.CultureInvariant).Count;
}
