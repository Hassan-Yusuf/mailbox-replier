namespace EmailCopilot.Worker;

public interface IRunRecordStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<long> InsertAsync(RunRecord runRecord, CancellationToken cancellationToken);
}
