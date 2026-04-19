namespace EmailCopilot.Worker;

public sealed class StyleAuthoredBodyPipeline
{
    private readonly IReadOnlyList<IStyleLineStage> _stages =
    [
        new StopAtQuoteBoundaryStage(),
        new StopAtHeaderLineStage(),
        new SkipQuotedLineStage(),
        new SkipBoilerplateLineStage()
    ];

    public string Extract(string bodyText)
    {
        var lines = bodyText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var keptLines = new List<string>(lines.Length);

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            var action = StyleLineAction.Keep;

            foreach (var stage in _stages)
            {
                var result = stage.Evaluate(trimmed, keptLines.Count > 0);
                if (result.Action == StyleLineAction.Keep)
                {
                    continue;
                }

                action = result.Action;
                break;
            }

            if (action == StyleLineAction.Stop)
            {
                break;
            }

            if (action == StyleLineAction.Skip)
            {
                continue;
            }

            keptLines.Add(trimmed);
        }

        return StyleExtractor.NormalizeBodySectionForTesting(keptLines);
    }
}
