using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class RunOnceWorker : BackgroundService
{
    private readonly ILogger<RunOnceWorker> _logger;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly Phase1ConfigurationValidator _configurationValidator;
    private readonly SqliteDraftStore _sqliteDraftStore;
    private readonly IRunRecordStore _runRecordStore;
    private readonly SqliteStyleProfileStore _sqliteStyleProfileStore;
    private readonly IStyleExampleStore _styleExampleStore;
    private readonly ScanInboxUseCase _scanInboxUseCase;
    private readonly LlmOptions _llmOptions;
    private readonly WebUiOptions _webUiOptions;

    public RunOnceWorker(
        ILogger<RunOnceWorker> logger,
        IHostApplicationLifetime hostApplicationLifetime,
        Phase1ConfigurationValidator configurationValidator,
        SqliteDraftStore sqliteDraftStore,
        IRunRecordStore runRecordStore,
        SqliteStyleProfileStore sqliteStyleProfileStore,
        IStyleExampleStore styleExampleStore,
        ScanInboxUseCase scanInboxUseCase,
        IOptions<LlmOptions> llmOptions,
        IOptions<MicrosoftOAuthOptions> microsoftOAuthOptions,
        IOptions<StyleProfileOptions> styleProfileOptions,
        IOptions<WebUiOptions> webUiOptions)
    {
        _logger = logger;
        _hostApplicationLifetime = hostApplicationLifetime;
        _configurationValidator = configurationValidator;
        _sqliteDraftStore = sqliteDraftStore;
        _runRecordStore = runRecordStore;
        _sqliteStyleProfileStore = sqliteStyleProfileStore;
        _styleExampleStore = styleExampleStore;
        _scanInboxUseCase = scanInboxUseCase;
        _llmOptions = llmOptions.Value;
        _webUiOptions = webUiOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runStartedAtUtc = DateTimeOffset.UtcNow;
        InboxScanRunResult? result = null;
        Exception? failure = null;

        try
        {
            _logger.LogInformation("Starting Email Copilot Phase 1 worker in RunOnce mode.");
            _configurationValidator.Validate();
            _logger.LogInformation(
                "Configuration validation completed. LLM mode: {LlmMode}.",
                _llmOptions.UseMock ? "mock" : "remote");

            await _sqliteDraftStore.InitializeAsync(stoppingToken);
            _logger.LogInformation("SQLite database initialization completed successfully.");

            await _runRecordStore.InitializeAsync(stoppingToken);
            _logger.LogInformation("SQLite run-record initialization completed successfully.");

            await _sqliteStyleProfileStore.InitializeAsync(stoppingToken);
            _logger.LogInformation("SQLite style-profile initialization completed successfully.");

            await _styleExampleStore.InitializeAsync(stoppingToken);
            _logger.LogInformation("SQLite style-example initialization completed successfully.");

            var processedImapUids = await _sqliteDraftStore.GetProcessedSourceImapUidsAsync(stoppingToken);
            _logger.LogInformation(
                "Loaded {ProcessedCount} previously processed IMAP UID(s) from SQLite.",
                processedImapUids.Count);

            result = await _scanInboxUseCase.ExecuteAsync(processedImapUids, stoppingToken);
            Environment.ExitCode = 0;

            _logger.LogInformation(
                "Run summary: DurationMs={DurationMs}; CandidateWindowsScanned={CandidateWindowsScanned}; CandidatesEvaluated={CandidatesEvaluated}; SkippedCount={SkippedCount}; DraftCreated={DraftCreated}; DraftCount={DraftCount}; FirstDraftId={DraftId}; FirstDraftSourceImapUid={DraftSourceImapUid}.",
                result.Duration.TotalMilliseconds,
                result.CandidateWindowsScanned,
                result.CandidatesEvaluated,
                result.SkippedCount,
                result.DraftCreated,
                result.DraftCount,
                result.DraftId,
                result.DraftSourceImapUid);

            foreach (var skipCount in result.SkipsByReasonCode.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Run summary skip bucket: ReasonCode={ReasonCode}; Count={Count}.",
                    skipCount.Key,
                    skipCount.Value);
            }
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            failure = ex;
            _logger.LogError(ex, "Phase 1 pipeline failed.");
        }
        finally
        {
            await PersistRunRecordAsync(runStartedAtUtc, result, failure, stoppingToken);
            _logger.LogInformation("RunOnce worker exiting with code {ExitCode}.", Environment.ExitCode);

            var shouldStopApplication = !_webUiOptions.Enabled || result is null || !result.DraftCreated;
            if (shouldStopApplication)
            {
                _hostApplicationLifetime.StopApplication();
            }
            else
            {
                _logger.LogInformation(
                    "Draft ready for review -> http://localhost:5000/review");
            }
        }
    }

    private async Task PersistRunRecordAsync(
        DateTimeOffset runStartedAtUtc,
        InboxScanRunResult? result,
        Exception? failure,
        CancellationToken cancellationToken)
    {
        try
        {
            var finishedAtUtc = result?.FinishedAtUtc ?? DateTimeOffset.UtcNow;
            var runRecord = new RunRecord(
                runStartedAtUtc,
                finishedAtUtc,
                Environment.ExitCode,
                result?.CandidateWindowsScanned ?? 0,
                result?.CandidatesEvaluated ?? 0,
                result?.SkippedCount ?? 0,
                result?.SkipsByReasonCode ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                result?.DraftCreated ?? false,
                result?.DraftId,
                result?.DraftSourceImapUid,
                failure?.Message);

            var runRecordId = await _runRecordStore.InsertAsync(runRecord, cancellationToken);
            _logger.LogInformation(
                "Persisted run record {RunRecordId} with exit code {ExitCode}.",
                runRecordId,
                Environment.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist the run record.");
        }
    }
}
