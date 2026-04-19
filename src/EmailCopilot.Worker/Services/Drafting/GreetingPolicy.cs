using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class GreetingPolicy
{
    private static readonly HashSet<string> GenericInboxNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "enquiries",
        "enquiry",
        "support",
        "info",
        "hello",
        "team",
        "contact",
        "admin",
        "help",
        "service",
        "sales",
        "careers",
        "notifications",
        "notification",
        "alert",
        "updates",
        "news",
        "reply",
        "mailer",
        "postmaster",
        "noreply",
        "no-reply",
        "bbc",
        "dominos",
        "uber",
        "amazon",
        "paypal",
        "linkedin",
        "indeed"
    };

    public string? DetermineSenderFirstName(EmailAddress sender)
    {
        var displayNameCandidate = ExtractHumanNameCandidate(sender.DisplayName);
        if (!string.IsNullOrWhiteSpace(displayNameCandidate))
        {
            return displayNameCandidate;
        }

        if (string.IsNullOrWhiteSpace(sender.Address))
        {
            return null;
        }

        var localPart = sender.Address.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(localPart))
        {
            return null;
        }

        var localPartTokens = localPart
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (localPartTokens.Length < 2)
        {
            return null;
        }

        return ExtractHumanNameCandidate(localPartTokens[0]);
    }

    public string NormalizeGreeting(string draft, EmailAddress sender)
    {
        var senderFirstName = DetermineSenderFirstName(sender);
        var normalized = draft.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var lines = normalized.Split('\n');
        if (lines.Length == 0)
        {
            return normalized;
        }

        var firstLine = lines[0].Trim();
        var greetingMatch = Regex.Match(
            firstLine,
            @"^(hi|hello|hey)\s+([A-Za-z][A-Za-z'\-]+),?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!greetingMatch.Success)
        {
            return normalized;
        }

        var usedName = greetingMatch.Groups[2].Value;
        if (!string.IsNullOrWhiteSpace(senderFirstName) &&
            string.Equals(usedName, senderFirstName, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        lines[0] = "Hi,";
        return string.Join('\n', lines).Trim();
    }

    private static string? ExtractHumanNameCandidate(string? rawCandidate)
    {
        if (string.IsNullOrWhiteSpace(rawCandidate))
        {
            return null;
        }

        var normalized = rawCandidate.Trim().Trim(',', ';', ':', '-', '"', '\'');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var commaIndex = normalized.IndexOf(',');
        if (commaIndex > 0)
        {
            normalized = normalized[..commaIndex].TrimEnd();
        }

        var tokens = normalized
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        normalized = tokens[0];

        if (normalized.Contains('@', StringComparison.Ordinal) ||
            normalized.Contains('.', StringComparison.Ordinal) ||
            normalized.Any(char.IsDigit))
        {
            return null;
        }

        if (GenericInboxNames.Contains(normalized))
        {
            return null;
        }

        if (tokens.Length == 1 && normalized.Length > 12)
        {
            return null;
        }

        if (normalized.Length < 2 || normalized.Length > 20 || !normalized.All(char.IsLetter))
        {
            return null;
        }

        return char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
