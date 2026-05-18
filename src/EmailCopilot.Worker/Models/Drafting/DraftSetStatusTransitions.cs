namespace EmailCopilot.Worker;

public static class DraftSetStatusTransitions
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DraftSetStatuses.Pending] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DraftSetStatuses.Approved,
                DraftSetStatuses.Dismissed
            },
            [DraftSetStatuses.Approved] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DraftSetStatuses.PushedToOutlook,
                DraftSetStatuses.Dismissed
            },
            [DraftSetStatuses.PushedToOutlook] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                DraftSetStatuses.Sent
            },
            [DraftSetStatuses.Dismissed] = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [DraftSetStatuses.Sent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        };

    public static bool IsAllowed(string fromStatus, string toStatus)
    {
        if (string.IsNullOrWhiteSpace(fromStatus) || string.IsNullOrWhiteSpace(toStatus))
        {
            return false;
        }

        if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(fromStatus, out var allowed)
            && allowed.Contains(toStatus);
    }
}
