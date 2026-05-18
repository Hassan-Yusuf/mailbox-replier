using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed partial class StyleExampleExtractor
{
    private const int MinimumWordCount = 8;
    private const int MaximumSentenceCount = 5;
    private const int MinimumSentenceCount = 1;
    private const double SimilarityThreshold = 0.8;
    private const int MaximumExamplesPerSegment = 12;
    private const int MaximumExamplesPerDomain = 3;
    private readonly StyleAuthoredBodyPipeline _styleAuthoredBodyPipeline;

    public StyleExampleExtractor(StyleAuthoredBodyPipeline styleAuthoredBodyPipeline)
    {
        _styleAuthoredBodyPipeline = styleAuthoredBodyPipeline;
    }

    public Task<IReadOnlyList<StyleExample>> ExtractAsync(
        string segmentKey,
        IReadOnlyList<SentEmailSample> sentEmails,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var orderedCandidates = sentEmails
            .OrderByDescending(static sample => sample.SentAtUtc)
            .ToArray();

        var selected = new List<StyleExample>();
        var normalizedBodies = new List<string>();
        var seenExactBodies = new HashSet<string>(StringComparer.Ordinal);
        var examplesPerDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in orderedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sample.Subject))
            {
                continue;
            }

            var authoredBody = _styleAuthoredBodyPipeline.Extract(sample.BodyText);
            if (string.IsNullOrWhiteSpace(authoredBody))
            {
                continue;
            }

            var sentenceCount = CountSentences(authoredBody);
            if (sentenceCount < MinimumSentenceCount || sentenceCount > MaximumSentenceCount)
            {
                continue;
            }

            if (CountWords(authoredBody) < MinimumWordCount)
            {
                continue;
            }

            var exactDedupKey = NormalizeForDedup(authoredBody);
            if (string.IsNullOrWhiteSpace(exactDedupKey) || !seenExactBodies.Add(exactDedupKey))
            {
                continue;
            }

            var normalizedBody = NormalizeForSimilarity(authoredBody);
            if (string.IsNullOrWhiteSpace(normalizedBody))
            {
                continue;
            }

            var isDuplicate = normalizedBodies.Any(existing => CalculateOverlapRatio(existing, normalizedBody) >= SimilarityThreshold);
            if (isDuplicate)
            {
                continue;
            }

            var recipientDomain = sample.RecipientDomain ?? string.Empty;
            if (examplesPerDomain.GetValueOrDefault(recipientDomain) >= MaximumExamplesPerDomain)
            {
                continue;
            }

            selected.Add(new StyleExample(
                segmentKey,
                authoredBody,
                sample.Subject,
                sample.SentAtUtc));
            normalizedBodies.Add(normalizedBody);
            examplesPerDomain[recipientDomain] = examplesPerDomain.GetValueOrDefault(recipientDomain) + 1;

            if (selected.Count >= MaximumExamplesPerSegment)
            {
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<StyleExample>>(selected);
    }

    private static int CountWords(string value) =>
        WordRegex().Matches(value).Count;

    private static int CountSentences(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return 0;
        }

        return Math.Max(1, SentenceBoundaryRegex().Matches(trimmed).Count);
    }

    private static string NormalizeForSimilarity(string value)
    {
        var collapsedWhitespace = WhitespaceRegex().Replace(value, " ").Trim().ToLowerInvariant();
        return NonAlphanumericRegex().Replace(collapsedWhitespace, string.Empty);
    }

    private static string NormalizeForDedup(string value) =>
        WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim().ToLowerInvariant();

    private static double CalculateOverlapRatio(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var leftShingles = BuildCharacterShingles(left);
        var rightShingles = BuildCharacterShingles(right);
        var intersectionCount = leftShingles.Intersect(rightShingles, StringComparer.Ordinal).Count();
        var unionCount = leftShingles.Union(rightShingles, StringComparer.Ordinal).Count();

        if (unionCount == 0)
        {
            return 0;
        }

        return (double)intersectionCount / unionCount;
    }

    private static HashSet<string> BuildCharacterShingles(string value)
    {
        const int shingleLength = 3;
        var shingles = new HashSet<string>(StringComparer.Ordinal);

        if (value.Length <= shingleLength)
        {
            shingles.Add(value);
            return shingles;
        }

        for (var index = 0; index <= value.Length - shingleLength; index++)
        {
            shingles.Add(value.Substring(index, shingleLength));
        }

        return shingles;
    }

    [GeneratedRegex(@"\b[\p{L}\p{N}']+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[.!?](?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumericRegex();
}
