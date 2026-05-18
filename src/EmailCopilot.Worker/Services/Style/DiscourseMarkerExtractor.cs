using System.Text.RegularExpressions;

namespace EmailCopilot.Worker;

public sealed record DiscourseMarkerObservation(string Marker, double Rate);

public static class DiscourseMarkerExtractor
{
    private const double MinimumRate = 0.05;
    private const int MaxMarkersEmitted = 6;

    public static IReadOnlyList<DiscourseMarkerObservation> Extract(IReadOnlyList<string> sampleBodies)
    {
        if (sampleBodies.Count == 0)
        {
            return Array.Empty<DiscourseMarkerObservation>();
        }

        var totalSamples = sampleBodies.Count;
        var observations = new List<DiscourseMarkerObservation>(DiscourseMarkers.Candidates.Count);

        foreach (var marker in DiscourseMarkers.Candidates)
        {
            var pattern = $@"\b{Regex.Escape(marker)}\b";
            var samplesContaining = sampleBodies.Count(body =>
                !string.IsNullOrWhiteSpace(body) &&
                Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase));

            var rate = (double)samplesContaining / totalSamples;
            if (rate >= MinimumRate)
            {
                observations.Add(new DiscourseMarkerObservation(marker, rate));
            }
        }

        return observations
            .OrderByDescending(o => o.Rate)
            .Take(MaxMarkersEmitted)
            .ToArray();
    }
}
