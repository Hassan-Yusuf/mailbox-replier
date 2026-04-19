using System.Globalization;

namespace EmailCopilot.Worker;

public sealed class EffectivelyEmptyBodyRule : BuiltInClassificationRule
{
    public override string RuleName => "BUILT_IN:EFFECTIVELY_EMPTY_BODY";
    public override string ReasonCode => ClassificationReasonCodes.BulkOrPromotional;

    public override RuleEvaluation Evaluate(IncomingEmail email)
    {
        var stripped = StripFormattingCharacters(email.BodyText);
        return string.IsNullOrWhiteSpace(stripped)
            ? Matched(RuleName, ReasonCode, "body-empty-after-normalization")
            : NotMatched(RuleName, ReasonCode, "body-non-empty");
    }

    private static string StripFormattingCharacters(string value) =>
        new string(value.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format).ToArray());
}
