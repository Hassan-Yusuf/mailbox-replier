namespace EmailCopilot.Worker;

public interface IIncomingEmailReader
{
    Task<IReadOnlyList<uint>> GetUnreadUidsAsync(
        IReadOnlySet<uint> excludedImapUids,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IncomingEmail>> GetEmailsBatchAsync(
        IReadOnlyList<uint> imapUids,
        CancellationToken cancellationToken);
}
