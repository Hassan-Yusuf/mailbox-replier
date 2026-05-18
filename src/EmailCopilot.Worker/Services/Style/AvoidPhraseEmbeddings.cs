namespace EmailCopilot.Worker;

public sealed record AvoidPhraseFamily(
    string FamilyId,
    IReadOnlyList<string> SeedTexts,
    IReadOnlyList<string> ApplicableShapes);

public sealed record AvoidPhraseCentroid(
    AvoidPhraseFamily Family,
    IReadOnlyList<float[]> Vectors);

public sealed class AvoidPhraseEmbeddings
{
    // Each family has multiple seed phrases — the filter checks max cosine across all seeds
    // before deciding to reject. Additional seeds are observed evasion variants from live mailbox
    // diagnostic runs (set 57 V1, set 58 V2, etc.). text-embedding-3-small clusters synonyms
    // further apart than a single seed handles, so we seed each known evasion variant.
    public static IReadOnlyList<AvoidPhraseFamily> Families { get; } = new[]
    {
        new AvoidPhraseFamily(
            "schedule-check-stall",
            new[]
            {
                "I'll check my schedule and get back to you",
                "I'll check my availability and get back to you",     // observed sets 48/50/52/54
                "I'll check on that and get back to you",             // observed set 59 V2
                "I'll check if I'm available and get back to you"     // observed set 60 V2
            },
            new[] { ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk }),
        new AvoidPhraseFamily(
            "polite-confirmation-closer",
            new[]
            {
                "Please let me know if that works for you",
                "Let me know if that works"                            // observed Please-drop, set 52 V0
            },
            new[] { ReplyShapes.DirectAnswer, ReplyShapes.ConfirmAndClose, ReplyShapes.ConfirmAndRequest }),
        new AvoidPhraseFamily(
            "corporate-clarification-request",
            new[] { "Could you please provide more details about this" },
            new[] { ReplyShapes.AcknowledgeAndAsk, ReplyShapes.ConfirmAndRequest }),
        new AvoidPhraseFamily(
            "generic-opener",
            new[] { "I hope this email finds you well" },
            new[]
            {
                ReplyShapes.DirectAnswer,
                ReplyShapes.Acknowledge,
                ReplyShapes.AcknowledgeAndAsk,
                ReplyShapes.ConfirmAndClose,
                ReplyShapes.ConfirmAndRequest,
                ReplyShapes.Decline,
                ReplyShapes.GeneralReply
            }),
        new AvoidPhraseFamily(
            "generic-warm-closer",
            new[] { "Looking forward to hearing from you" },
            new[]
            {
                ReplyShapes.DirectAnswer,
                ReplyShapes.Acknowledge,
                ReplyShapes.ConfirmAndClose,
                ReplyShapes.ConfirmAndRequest,
                ReplyShapes.GeneralReply
            })
    };

    private readonly IReadOnlyList<AvoidPhraseCentroid> _centroids;

    public AvoidPhraseEmbeddings(IReadOnlyList<AvoidPhraseCentroid> centroids)
    {
        _centroids = centroids;
    }

    public IReadOnlyList<AvoidPhraseCentroid> ForShape(string replyShape) =>
        _centroids
            .Where(c => c.Family.ApplicableShapes.Contains(replyShape, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    public static async Task<AvoidPhraseEmbeddings> BuildAsync(
        IEmbeddingClient embeddingClient,
        CancellationToken cancellationToken)
    {
        // Flatten all seeds across families for a single batch embed call, then re-slice per family.
        var allSeeds = Families.SelectMany(f => f.SeedTexts).ToArray();
        var allVectors = await embeddingClient.EmbedAsync(allSeeds, cancellationToken);

        if (allVectors.Count != allSeeds.Length)
        {
            throw new InvalidOperationException(
                $"Expected {allSeeds.Length} seed vectors, got {allVectors.Count}.");
        }

        var centroids = new List<AvoidPhraseCentroid>(Families.Count);
        var cursor = 0;
        foreach (var family in Families)
        {
            var familyVectors = new float[family.SeedTexts.Count][];
            for (var i = 0; i < family.SeedTexts.Count; i++)
            {
                familyVectors[i] = allVectors[cursor + i];
            }
            cursor += family.SeedTexts.Count;
            centroids.Add(new AvoidPhraseCentroid(family, familyVectors));
        }

        return new AvoidPhraseEmbeddings(centroids);
    }
}
