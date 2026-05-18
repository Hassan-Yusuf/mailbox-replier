namespace EmailCopilot.Worker;

public static class ConfidenceTierClassifier
{
    public const double HighThreshold = 0.75;
    public const double MediumThreshold = 0.55;

    public static ConfidenceTier FromScore(double score)
    {
        if (score >= HighThreshold)
        {
            return ConfidenceTier.High;
        }

        if (score >= MediumThreshold)
        {
            return ConfidenceTier.Medium;
        }

        return ConfidenceTier.Low;
    }
}
