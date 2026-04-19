using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed class ReplyShapePlanner : IReplyShapePlanner
{
    public ReplyPlan Plan(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis)
    {
        var subject = email.Subject.Trim();
        var body = email.BodyText.Trim();
        var combined = $"{subject}\n{body}";
        var asks = analysis.Asks
            .Where(static ask => !ask.IsOptional)
            .Select(static ask => ask.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var branchShapes = analysis.DecisionBranches
            .SelectMany(static branch => branch.ViableReplyShapes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (analysis.Asks.Count == 1 && analysis.DecisionBranches.Count == 0 && LooksLikeDirectQuestion(combined))
        {
            return new ReplyPlan(
                [
                    new ReplyShapeOption(ReplyShapes.DirectAnswer, "Direct answer", 0.86, asks)
                ]);
        }

        if (branchShapes.Count > 0)
        {
            var options = new List<ReplyShapeOption>();
            var highStakes = LooksLikeHighStakesContext(combined);
            var logistics = LooksLikeLogisticsOrConfirmation(combined);

            if (branchShapes.Contains(ReplyShapes.DirectAnswer) && analysis.Asks.Count > 0)
            {
                AddOption(options, ReplyShapes.DirectAnswer, "Direct answer", 0.64, asks);
            }

            if (branchShapes.Contains(ReplyShapes.Acknowledge))
            {
                AddOption(options, ReplyShapes.Acknowledge, "Acknowledge only", highStakes ? 0.62 : 0.58, asks);
            }

            if (branchShapes.Contains(ReplyShapes.Decline))
            {
                AddOption(options, ReplyShapes.Decline, "Decline", 0.61, asks);
            }

            if (branchShapes.Contains(ReplyShapes.AcknowledgeAndAsk) && (styleProfile.QuestionEndingRate >= 0.3 || highStakes || analysis.Asks.Count > 0))
            {
                AddOption(options, ReplyShapes.AcknowledgeAndAsk, "Acknowledge and ask", highStakes ? 0.6 : 0.54, asks);
            }

            if (branchShapes.Contains(ReplyShapes.ConfirmAndRequest) && (highStakes || logistics || analysis.Asks.Count > 0))
            {
                AddOption(options, ReplyShapes.ConfirmAndRequest, "Confirm and request detail", highStakes ? 0.58 : 0.49, asks);
            }

            if (branchShapes.Contains(ReplyShapes.ConfirmAndClose) && (logistics || styleProfile.QuestionEndingRate < 0.3))
            {
                AddOption(options, ReplyShapes.ConfirmAndClose, "Confirm and close", logistics ? 0.57 : 0.46, asks);
            }

            if (options.Count == 0)
            {
                options.Add(new ReplyShapeOption(ReplyShapes.Acknowledge, "Acknowledge only", 0.52, asks));
            }

            return new ReplyPlan(Deduplicate(options));
        }

        var defaultShape = styleProfile.QuestionEndingRate > 0.5
            ? new ReplyShapeOption(ReplyShapes.AcknowledgeAndAsk, "Acknowledge and ask", 0.51, asks)
            : new ReplyShapeOption(ReplyShapes.Acknowledge, "Acknowledge only", 0.5, asks);

        return new ReplyPlan([defaultShape]);
    }

    private static void AddOption(ICollection<ReplyShapeOption> options, string shape, string label, double confidenceScore, IReadOnlyList<string> mustAddressAsks)
    {
        if (options.Any(existing => string.Equals(existing.Shape, shape, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        options.Add(new ReplyShapeOption(shape, label, confidenceScore, mustAddressAsks));
    }

    private static IReadOnlyList<ReplyShapeOption> Deduplicate(IEnumerable<ReplyShapeOption> options)
    {
        return options
            .GroupBy(
                option => $"{option.Label}|{string.Join('|', option.MustAddressAsks ?? [])}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(option => option.ConfidenceScore).First())
            .ToArray();
    }

    private static bool LooksLikeDirectQuestion(string value)
    {
        // ? alone is too broad — informational emails routinely contain incidental question marks.
        // Keyword patterns alone are too broad — nearly every professional email contains "please" or "let me know".
        // Both are required: a ? present AND an explicit direct-question opener.
        if (!value.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(can you|could you|would you|are you|do you|please (send|confirm|let me know|advise|provide))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeLogisticsOrConfirmation(string value) =>
        Regex.IsMatch(
            value,
            @"\b(schedule|hearing|appointment|booking|viewing|timing|time works|that works|confirmed|location|readings)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool LooksLikeHighStakesContext(string value) =>
        Regex.IsMatch(
            value,
            @"\b(hearing|barrister|court|tribunal|client|solicitor|legal|case|claim|application update|position update)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
