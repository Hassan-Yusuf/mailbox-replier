using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed partial class StyleExtractor
{
    private const int MaxCommonPhrases = 5;
    private const int MinimumAuthoredWordCount = 8;
    private readonly StyleAuthoredBodyPipeline _authoredBodyPipeline;
    private readonly StyleSentenceFilterPipeline _sentenceFilterPipeline;

    public StyleExtractor(
        StyleAuthoredBodyPipeline authoredBodyPipeline,
        StyleSentenceFilterPipeline sentenceFilterPipeline)
    {
        _authoredBodyPipeline = authoredBodyPipeline;
        _sentenceFilterPipeline = sentenceFilterPipeline;
    }

    public StyleProfile BuildProfile(
        string segmentKey,
        IReadOnlyList<SentEmailSample> sentEmails,
        StyleProfile fallbackProfile)
    {
        if (sentEmails.Count == 0)
        {
            return fallbackProfile;
        }

        var greetingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var closingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var signatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var phraseCounts = new Dictionary<string, PhraseSample>(StringComparer.OrdinalIgnoreCase);

        var sentenceLengths = new List<int>();
        var sentenceCounts = new List<int>();
        var contractionCount = 0;
        var totalWordCount = 0;
        var exclamationCount = 0;
        var greetingUsageCount = 0;
        var signoffUsageCount = 0;
        var questionEndingCount = 0;
        var gratitudeUsageCount = 0;
        var contractionUsageCount = 0;
        var fragmentUsageCount = 0;
        var explicitNextStepCount = 0;

        var usedSampleCount = 0;

        foreach (var sentEmail in sentEmails)
        {
            if (string.IsNullOrWhiteSpace(sentEmail.BodyText))
            {
                continue;
            }

            var authoredBody = ExtractAuthoredBody(sentEmail.BodyText);
            if (string.IsNullOrWhiteSpace(authoredBody))
            {
                continue;
            }

            if (CountWords(authoredBody) < MinimumAuthoredWordCount)
            {
                continue;
            }

            usedSampleCount++;

            var lines = authoredBody
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .ToArray();

            var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (nonEmptyLines.Length == 0)
            {
                continue;
            }

            var greeting = ExtractGreeting(nonEmptyLines);
            if (!string.IsNullOrWhiteSpace(greeting))
            {
                greetingUsageCount++;
                IncrementCount(greetingCounts, greeting);
            }

            var closingIndex = FindClosingIndex(nonEmptyLines);
            if (closingIndex >= 0)
            {
                signoffUsageCount++;
                var closingToken = ExtractClosingToken(nonEmptyLines[closingIndex]);
                if (!string.IsNullOrWhiteSpace(closingToken))
                {
                    IncrementCount(closingCounts, closingToken);
                }

                var signature = ExtractSignature(nonEmptyLines, closingIndex);
                if (!string.IsNullOrWhiteSpace(signature))
                {
                    IncrementCount(signatureCounts, signature);
                }
            }

            var contentLines = GetContentLines(nonEmptyLines, greeting, closingIndex);
            var contentText = string.Join(Environment.NewLine, contentLines).Trim();
            var contentSentences = SentenceSplitRegex()
                .Split(contentText)
                .Select(sentence => sentence.Trim())
                .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
                .ToArray();

            if (contentText.TrimEnd().EndsWith("?", StringComparison.Ordinal))
            {
                questionEndingCount++;
            }

            if (ContainsGratitude(contentText))
            {
                gratitudeUsageCount++;
            }

            if (ContractionRegex().IsMatch(contentText))
            {
                contractionUsageCount++;
            }

            if (ContainsExplicitNextStep(contentText))
            {
                explicitNextStepCount++;
            }

            sentenceCounts.Add(Math.Max(1, contentSentences.Length));

            var hasFragment = false;
            foreach (var sentence in contentSentences)
            {
                var wordCount = CountWords(sentence);
                if (wordCount == 0)
                {
                    continue;
                }

                sentenceLengths.Add(wordCount);
                totalWordCount += wordCount;
                hasFragment |= LooksLikeFragment(sentence, wordCount);
            }

            if (hasFragment)
            {
                fragmentUsageCount++;
            }

            contractionCount += ContractionRegex().Matches(contentText).Count;
            exclamationCount += contentText.Count(static character => character == '!');

            foreach (var phrase in ExtractPhraseCandidates(contentLines))
            {
                if (!phraseCounts.TryGetValue(phrase.Normalized, out var existing))
                {
                    phraseCounts[phrase.Normalized] = phrase;
                    continue;
                }

                phraseCounts[phrase.Normalized] = existing with { Count = existing.Count + 1 };
            }
        }

        var averageSentenceLength = sentenceLengths.Count == 0
            ? fallbackProfile.AverageSentenceLength
            : Math.Round(sentenceLengths.Average(), 1);
        var typicalSentenceCountRange = BuildSentenceCountRange(sentenceCounts, fallbackProfile);
        var greetingUsageRate = BuildRate(greetingUsageCount, usedSampleCount, fallbackProfile.GreetingUsageRate);
        var signoffUsageRate = BuildRate(signoffUsageCount, usedSampleCount, fallbackProfile.SignoffUsageRate);
        var questionEndingRate = BuildRate(questionEndingCount, usedSampleCount, fallbackProfile.QuestionEndingRate);
        var gratitudeUsageRate = BuildRate(gratitudeUsageCount, usedSampleCount, fallbackProfile.GratitudeUsageRate);
        var contractionUsageRate = BuildRate(contractionUsageCount, usedSampleCount, fallbackProfile.ContractionUsageRate);
        var fragmentUsageRate = BuildRate(fragmentUsageCount, usedSampleCount, fallbackProfile.FragmentUsageRate);
        var explicitNextStepRate = BuildRate(explicitNextStepCount, usedSampleCount, fallbackProfile.ExplicitNextStepRate);

        var tone = BuildToneDescription(
            averageSentenceLength,
            totalWordCount == 0 ? 0 : (double)contractionCount / totalWordCount,
            totalWordCount == 0 ? 0 : (double)exclamationCount / totalWordCount,
            fallbackProfile.Tone);

        if (usedSampleCount == 0)
        {
            return fallbackProfile;
        }

        return new StyleProfile(
            SegmentKey: segmentKey,
            Greeting: SelectMostCommonOrFallback(greetingCounts, fallbackProfile.Greeting),
            Closing: SelectMostCommonOrFallback(closingCounts, fallbackProfile.Closing),
            Tone: tone,
            Signature: SelectMostCommonOrFallback(signatureCounts, string.Empty),
            CommonPhrases: phraseCounts.Values
                .Where(sample => sample.Count > 1)
                .OrderByDescending(sample => sample.Count)
                .ThenBy(sample => sample.Original, StringComparer.OrdinalIgnoreCase)
                .Take(MaxCommonPhrases)
                .Select(sample => sample.Original)
                .ToArray(),
            AverageSentenceLength: averageSentenceLength,
            GreetingUsageRate: greetingUsageRate,
            SignoffUsageRate: signoffUsageRate,
            QuestionEndingRate: questionEndingRate,
            GratitudeUsageRate: gratitudeUsageRate,
            ContractionUsageRate: contractionUsageRate,
            FragmentUsageRate: fragmentUsageRate,
            ExplicitNextStepRate: explicitNextStepRate,
            TypicalSentenceCountMin: typicalSentenceCountRange.Min,
            TypicalSentenceCountMax: typicalSentenceCountRange.Max,
            FormalityScore: BuildFormalityScore(
                signoffUsageRate,
                contractionUsageRate,
                fragmentUsageRate,
                averageSentenceLength,
                typicalSentenceCountRange.Max),
            SampleSize: usedSampleCount,
            BuiltAtUtc: DateTimeOffset.UtcNow);
    }

    private IEnumerable<PhraseSample> ExtractPhraseCandidates(IReadOnlyList<string> contentLines)
    {
        foreach (var line in contentLines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 18 || trimmed.Length > 120)
            {
                continue;
            }

            if (IsHeaderLikeLine(trimmed) || IsQuotedLine(trimmed) || LooksLikeMetadataLine(trimmed))
            {
                continue;
            }

            if (!_sentenceFilterPipeline.ShouldKeep(trimmed))
            {
                continue;
            }

            var normalized = NormalizePhrase(trimmed);
            var wordCount = CountWords(normalized);

            if (wordCount < 4 || wordCount > 14)
            {
                continue;
            }

            if (GreetingRegex().IsMatch(normalized) || ClosingRegex().IsMatch(normalized))
            {
                continue;
            }

            yield return new PhraseSample(normalized, trimmed, 1);
        }
    }

    private static string BuildToneDescription(
        double averageSentenceLength,
        double contractionRatio,
        double exclamationRatio,
        string fallbackTone)
    {
        if (averageSentenceLength <= 0)
        {
            return fallbackTone;
        }

        var openness = contractionRatio >= 0.03 || exclamationRatio >= 0.01
            ? "friendly"
            : "professional";

        var pacing = averageSentenceLength <= 14
            ? "concise"
            : averageSentenceLength >= 22
                ? "detailed"
                : "balanced";

        return $"{openness} and {pacing}";
    }

    private static double BuildRate(int count, int total, double fallback)
    {
        if (total <= 0)
        {
            return fallback;
        }

        return Math.Round((double)count / total, 3);
    }

    private static SentenceCountRange BuildSentenceCountRange(
        IReadOnlyList<int> sentenceCounts,
        StyleProfile fallbackProfile)
    {
        if (sentenceCounts.Count == 0)
        {
            return new SentenceCountRange(fallbackProfile.TypicalSentenceCountMin, fallbackProfile.TypicalSentenceCountMax);
        }

        var ordered = sentenceCounts.OrderBy(count => count).ToArray();
        var min = Percentile(ordered, 0.25);
        var max = Percentile(ordered, 0.75);

        return new SentenceCountRange(Math.Max(1, min), Math.Max(Math.Max(1, min), max));
    }

    private static int Percentile(IReadOnlyList<int> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 1;
        }

        var index = (int)Math.Round((values.Count - 1) * percentile, MidpointRounding.AwayFromZero);
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }

    private static double BuildFormalityScore(
        double signoffUsageRate,
        double contractionUsageRate,
        double fragmentUsageRate,
        double averageSentenceLength,
        int maxTypicalSentenceCount)
    {
        var signoffComponent = signoffUsageRate;
        var contractionComponent = 1 - contractionUsageRate;
        var fragmentComponent = 1 - fragmentUsageRate;
        var sentenceLengthComponent = Math.Clamp((averageSentenceLength - 8) / 16, 0, 1);
        var structureComponent = Math.Clamp((maxTypicalSentenceCount - 1) / 3.0, 0, 1);

        return Math.Round(
            (signoffComponent + contractionComponent + fragmentComponent + sentenceLengthComponent + structureComponent) / 5.0,
            3);
    }

    private string ExtractAuthoredBody(string bodyText) => _authoredBodyPipeline.Extract(bodyText);

    private static string ExtractGreeting(IReadOnlyList<string> lines)
    {
        var firstLine = lines[0];
        var match = GreetingRegex().Match(firstLine);
        return match.Success ? NormalizeToken(match.Value) : string.Empty;
    }

    private static IReadOnlyList<string> GetContentLines(
        IReadOnlyList<string> lines,
        string greeting,
        int closingIndex)
    {
        var startIndex = string.IsNullOrWhiteSpace(greeting) ? 0 : 1;
        var endIndexExclusive = closingIndex >= 0 ? closingIndex : lines.Count;

        if (startIndex >= endIndexExclusive)
        {
            return [];
        }

        return lines
            .Skip(startIndex)
            .Take(endIndexExclusive - startIndex)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static int FindClosingIndex(IReadOnlyList<string> lines)
    {
        for (var index = lines.Count - 1; index >= Math.Max(0, lines.Count - 5); index--)
        {
            if (ClosingRegex().IsMatch(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ExtractClosingToken(string closingLine)
    {
        var match = ClosingRegex().Match(closingLine);
        return match.Success ? NormalizeToken(match.Value) : string.Empty;
    }

    private static string ExtractSignature(IReadOnlyList<string> lines, int closingIndex)
    {
        if (closingIndex < 0 || closingIndex >= lines.Count)
        {
            return string.Empty;
        }

        var signatureLines = new List<string>();

        var closingRemainder = ExtractInlineSignatureRemainder(lines[closingIndex]);
        if (!string.IsNullOrWhiteSpace(closingRemainder))
        {
            signatureLines.Add(closingRemainder);
        }

        signatureLines.AddRange(lines
            .Skip(closingIndex + 1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(3)
            .ToArray());

        if (signatureLines.Count == 0)
        {
            return string.Empty;
        }

        var signature = string.Join(Environment.NewLine, signatureLines.Distinct(StringComparer.Ordinal)).Trim();
        if (signature.Length > 120 || !LooksLikeSignature(signature))
        {
            return string.Empty;
        }

        return signature;
    }

    private static string ExtractInlineSignatureRemainder(string closingLine)
    {
        var match = ClosingRegex().Match(closingLine);
        if (!match.Success)
        {
            return string.Empty;
        }

        var remainder = closingLine[match.Length..]
            .Trim()
            .Trim(',', ';', ':', '-', ' ');

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return string.Empty;
        }

        if (LooksLikeMetadataLine(remainder) || CountWords(remainder) > 4)
        {
            return string.Empty;
        }

        return remainder;
    }

    private static void IncrementCount(IDictionary<string, int> counts, string value)
    {
        if (counts.TryGetValue(value, out var current))
        {
            counts[value] = current + 1;
            return;
        }

        counts[value] = 1;
    }

    private static string SelectMostCommonOrFallback(
        IReadOnlyDictionary<string, int> counts,
        string fallback)
    {
        return counts.Count == 0
            ? fallback
            : counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .First()
                .Key;
    }

    private static string NormalizePhrase(string value)
    {
        var collapsed = PhraseCleanupRegex().Replace(value, " ");
        return string.Join(' ', collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().ToLowerInvariant();
    }

    internal static string NormalizeBodySectionForTesting(IReadOnlyList<string> lines) => NormalizeBodySection(lines);

    private static string NormalizeBodySection(IReadOnlyList<string> lines)
    {
        var collapsed = new List<string>(lines.Count);
        var previousWasBlank = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!previousWasBlank)
                {
                    collapsed.Add(string.Empty);
                }

                previousWasBlank = true;
                continue;
            }

            collapsed.Add(line);
            previousWasBlank = false;
        }

        return string.Join(Environment.NewLine, collapsed).Trim();
    }

    private static bool IsHeaderLikeLine(string line) => HeaderLineRegex().IsMatch(line);

    private static bool IsQuotedLine(string line) => line.StartsWith(">", StringComparison.Ordinal);

    private static bool LooksLikeMetadataLine(string line) =>
        line.Contains('@') ||
        line.Contains('<') ||
        line.Contains('>') ||
        line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsGratitude(string text) =>
        GratitudeRegex().IsMatch(text);

    private static bool ContainsExplicitNextStep(string text) =>
        NextStepRegex().IsMatch(text);

    private static bool LooksLikeFragment(string sentence, int wordCount)
    {
        if (wordCount <= 3)
        {
            return true;
        }

        return !VerbRegex().IsMatch(sentence);
    }

    private static bool LooksLikeSignature(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (signature.Contains('!') || signature.Contains('?') || signature.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (signature.StartsWith("best", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("regards", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("thanks", StringComparison.OrdinalIgnoreCase) ||
            signature.StartsWith("thank you", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CountWords(signature) <= 4;
    }

    private static string NormalizeToken(string value)
    {
        var trimmed = value.Trim().Trim(',', ';', ':', '-');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var lower = trimmed.ToLowerInvariant();
        return lower switch
        {
            "thank you" => "Thank you",
            "best regards" => "Best regards",
            "kind regards" => "Kind regards",
            "regards" => "Regards",
            "cheers" => "Cheers",
            "sincerely" => "Sincerely",
            "hello" => "Hello",
            "hey" => "Hey",
            "dear" => "Dear",
            "good morning" => "Good morning",
            "good afternoon" => "Good afternoon",
            "good evening" => "Good evening",
            _ when lower.StartsWith("hi", StringComparison.Ordinal) => "Hi",
            _ when lower.StartsWith("thanks", StringComparison.Ordinal) => "Thanks",
            _ => char.ToUpperInvariant(trimmed[0]) + trimmed[1..]
        };
    }

    private static int CountWords(string value) => WordRegex().Matches(value).Count;

    [GeneratedRegex(@"^(hi|hey|hello|dear|good morning|good afternoon|good evening)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GreetingRegex();

    [GeneratedRegex(@"^(best regards|kind regards|thank you|thanks|regards|best|cheers|sincerely)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingRegex();

    [GeneratedRegex(@"^(from|to|cc|bcc|sent|subject|date)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderLineRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}' ]+", RegexOptions.CultureInvariant)]
    private static partial Regex PhraseCleanupRegex();

    [GeneratedRegex(@"\b[\p{L}\p{N}']+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b\w+'\w+\b", RegexOptions.CultureInvariant)]
    private static partial Regex ContractionRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSplitRegex();

    [GeneratedRegex(@"\b(thanks|thank you|appreciate it|appreciated)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GratitudeRegex();

    [GeneratedRegex(@"\b(i'll|i will|let me know|i can|i'll send|i will send|i'll check|i will check|i'll confirm|i will confirm)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NextStepRegex();

    [GeneratedRegex(@"\b(am|is|are|was|were|be|being|been|do|does|did|have|has|had|can|could|will|would|should|need|send|confirm|book|apply|begin|start|check|help|know|open|look|review|arrange|work|receive|get|let)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerbRegex();

    private sealed record PhraseSample(string Normalized, string Original, int Count);

    private sealed record SentenceCountRange(int Min, int Max);
}
