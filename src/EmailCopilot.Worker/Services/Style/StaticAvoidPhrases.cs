namespace EmailCopilot.Worker;

public static class StaticAvoidPhrases
{
    // Entries marked with "(evasion variant)" exist because live drafts from sets 48-54
    // showed the model evading the original phrase via synonym swap or word-drop.
    // Keep them paired with their parent phrase — dropping them re-opens the gap.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ByShape { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [ReplyShapes.DirectAnswer] = new[]
            {
                "I hope this email finds you well",
                "Please don't hesitate to reach out",
                "Should you have any further questions",
                "Feel free to let me know if you need anything else",
                "Please let me know if that works",
                "Let me know if that works", // evasion variant: "Please"-drop of preceding entry (set 52 V0)
                "Please let me know if you need anything else"
            },
            [ReplyShapes.Acknowledge] = new[]
            {
                "Thank you so much for your email",
                "I really appreciate you reaching out",
                "Thanks for taking the time to write",
                "I'll check my schedule and get back to you",
                "I'll check my availability and get back to you", // evasion variant: schedule→availability synonym (sets 48/50/52/54)
                "I'll check and let you know", // evasion variant: short-form of the "check + get back" template
                "I'll get back to you shortly"
            },
            [ReplyShapes.AcknowledgeAndAsk] = new[]
            {
                "Just to clarify a few things",
                "I had a couple of quick questions",
                "Could you kindly provide some additional information",
                "I was wondering if you might be able to share",
                "If it's not too much trouble, could you"
            },
            [ReplyShapes.ConfirmAndClose] = new[]
            {
                "Looking forward to hearing from you",
                "Thanks again for everything",
                "Please don't hesitate to reach out if you have any questions",
                "I look forward to our continued collaboration",
                "Wishing you all the best"
            },
            [ReplyShapes.ConfirmAndRequest] = new[]
            {
                "If you could kindly",
                "At your earliest convenience",
                "I would greatly appreciate it if you could",
                "When you have a moment",
                "Whenever it works for you",
                "Could you please provide more details about",
                "Could you clarify if there's a specific"
            },
            [ReplyShapes.Decline] = new[]
            {
                "Unfortunately at this time",
                "I regret to inform you",
                "I'm afraid I won't be able to",
                "While I appreciate the opportunity",
                "Thank you for thinking of me, but",
                "Let me know if there are other opportunities in the future",
                "Thanks for understanding"
            },
            [ReplyShapes.GeneralReply] = new[]
            {
                "I hope this email finds you well",
                "I trust this message finds you well",
                "Please don't hesitate to reach out",
                "Should you have any further questions",
                "Feel free to let me know if you need anything else",
                "Looking forward to hearing from you",
                "Thank you so much for your email"
            }
        };

    public static IReadOnlyList<string> ForShape(string replyShape)
    {
        if (string.IsNullOrWhiteSpace(replyShape))
        {
            return Array.Empty<string>();
        }

        return ByShape.TryGetValue(replyShape, out var phrases)
            ? phrases
            : Array.Empty<string>();
    }
}
