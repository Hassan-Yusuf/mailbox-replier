using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class DraftEligibilityAssessor : IDraftEligibilityAssessor
{
    public DraftEligibilityResult Assess(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis)
    {
        var body = email.BodyText.Trim();
        var subject = email.Subject.Trim();

        if (!IsGenuineConversationalEmail(email, body, analysis))
        {
            return new DraftEligibilityResult(
                DraftEligibilityDecisions.Ineligible,
                "no genuine conversational signal",
                0);
        }

        var ambiguityScore = ComputeAmbiguityScore(email, body, subject, styleProfile, analysis);
        var decision = ambiguityScore > 0.45
            ? DraftEligibilityDecisions.VariantCandidate
            : DraftEligibilityDecisions.SingleDraft;

        var reason = decision == DraftEligibilityDecisions.VariantCandidate
            ? "intent is genuinely ambiguous"
            : "one best-fit reply is likely";

        return new DraftEligibilityResult(decision, reason, ambiguityScore);
    }

    private static bool IsGenuineConversationalEmail(IncomingEmail email, string body, EmailRequestAnalysis analysis)
    {
        var senderFirstName = TryExtractSenderFirstName(email.From);
        var highStakesContext = Regex.IsMatch(
            $"{email.Subject}\n{body}\n{email.From.Domain}",
            @"\b(hearing|court|tribunal|solicitor|clerk|barrister|legal|interview|research|properties|availability|viewing)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (senderFirstName is null && !highStakesContext)
        {
            return false;
        }

        var hasDirectAddress = Regex.IsMatch(
            body,
            @"\b(hi|hello|dear)\s+[a-z]+\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var hasQuestion = body.Contains('?', StringComparison.Ordinal);
        var hasRequest = Regex.IsMatch(
            body,
            @"\b(can you|could you|would you|please|let me know|get back|send over|send me|confirm whether)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var analysisSignals = analysis.Asks.Count > 0 || analysis.DecisionBranches.Count > 0;

        return hasDirectAddress || hasQuestion || hasRequest || highStakesContext || analysisSignals || email.MessageId.Contains("reply", StringComparison.OrdinalIgnoreCase);
    }

    private static double ComputeAmbiguityScore(IncomingEmail email, string body, string subject, StyleProfile styleProfile, EmailRequestAnalysis analysis)
    {
        var score = 0d;

        if (analysis.DecisionBranches.Count >= 2)
        {
            score += 0.5;
        }
        else if (analysis.DecisionBranches.Count == 1)
        {
            score += 0.2;
        }

        if (analysis.Asks.Select(ask => ask.AskType).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
        {
            score += 0.2;
        }

        if (analysis.Asks.Count >= 2)
        {
            score += 0.15;
        }

        if (!body.Contains('?', StringComparison.Ordinal))
        {
            score += 0.3;
        }

        if (Regex.IsMatch(
                $"{subject}\n{body}",
                @"\b(unfortunately|pleased to let you know|we wanted to let you know|just to let you know|no longer going ahead|cancelled|canceled|confirmed)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            score += 0.25;
        }

        if (Regex.IsMatch(
                email.From.Domain,
                @"\b(law|legal|solicitor|chambers|research|properties)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            score += 0.2;
        }

        if (styleProfile.QuestionEndingRate < 0.5)
        {
            score += 0.1;
        }

        if (string.Equals(analysis.Urgency, UrgencyLevels.Low, StringComparison.OrdinalIgnoreCase) &&
            analysis.DecisionBranches.Count > 0)
        {
            score += 0.1;
        }

        if (Regex.IsMatch(
                $"{subject}\n{body}",
                @"\b(viewing|hearing|appointment|interview|timeline|slot|availability|confirm)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            score += 0.15;
        }

        if (analysis.RequiresPersonalConfirmation)
        {
            score += 0.5;
        }

        return Math.Min(score, 1d);
    }

    private static string? TryExtractSenderFirstName(EmailAddress address)
    {
        var displayName = address.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var first = displayName
            .Split(new[] { ' ', ',', '.', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(first) ||
            first.Length < 2 ||
            first.Any(char.IsDigit))
        {
            return null;
        }

        return first;
    }
}
