namespace EmailCopilot.Worker;

public sealed class DraftVariantRecord
{
    public long Id { get; init; }

    public int SortOrder { get; init; }

    public string ReplyShape { get; init; } = ReplyShapes.GeneralReply;

    public string ReplyShapeLabel { get; init; } = "General reply";

    public string Body { get; init; } = string.Empty;

    public double ConfidenceScore { get; init; }

    public string StyleSegmentUsed { get; init; } = string.Empty;

    public string? GroundingWarning { get; init; }

    public string? CoverageWarning { get; init; }

    public bool WasSelected { get; init; }

    public bool WasEdited { get; init; }

    public string? EditedBody { get; init; }
}
