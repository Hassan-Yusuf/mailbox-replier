namespace EmailCopilot.Worker;

public interface IOutlookDraftPusher
{
    Task PushAsync(long draftSetId, long selectedVariantId, CancellationToken cancellationToken);
}
