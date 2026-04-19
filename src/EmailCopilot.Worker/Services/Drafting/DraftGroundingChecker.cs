using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class DraftGroundingChecker : IDraftGroundingChecker
{
    public string? Check(IncomingEmail email, string draftText, EmailRequestAnalysis analysis, IReadOnlyList<string> mustAddressAsks)
    {
        var warnings = new List<string>();
        var sourceText = $"{email.Subject}\n{email.BodyText}";

        AddNovelMatches(warnings, "unverified_date", draftText, sourceText, @"\b(?:\d{1,2}(?::\d{2})?\s*(?:am|pm)|monday|tuesday|wednesday|thursday|friday|saturday|sunday|today|tomorrow)\b");
        AddNovelMatches(warnings, "unverified_number", draftText, sourceText, @"\b\d+(?:[.,]\d+)?\b");
        AddNovelMatches(warnings, "unverified_url", draftText, sourceText, @"https?://\S+|www\.\S+");

        foreach (var ask in mustAddressAsks.Where(static ask => !string.IsNullOrWhiteSpace(ask)))
        {
            if (!DraftMentionsAsk(draftText, ask))
            {
                warnings.Add($"missed_ask:{ask}");
            }
        }

        return warnings.Count == 0
            ? null
            : string.Join("; ", warnings.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddNovelMatches(List<string> warnings, string warningCode, string draftText, string sourceText, string pattern)
    {
        var sourceMatches = Regex.Matches(sourceText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(static match => match.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var draftMatches = Regex.Matches(draftText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(static match => match.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        if (draftMatches.Any(match => !sourceMatches.Contains(match)))
        {
            warnings.Add(warningCode);
        }
    }

    private static bool DraftMentionsAsk(string draftText, string ask)
    {
        var keywords = Regex.Matches(ask, @"\b[\p{L}\p{N}']+\b", RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .Where(static value => value.Length > 3)
            .Select(static value => value.ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (keywords.Length == 0)
        {
            return true;
        }

        var normalizedDraft = draftText.ToLowerInvariant();
        var matchedKeywords = keywords.Count(normalizedDraft.Contains);
        var requiredMatches = normalizedDraft.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 35
            ? 1
            : Math.Min(2, keywords.Length);
        return matchedKeywords >= requiredMatches;
    }
}
