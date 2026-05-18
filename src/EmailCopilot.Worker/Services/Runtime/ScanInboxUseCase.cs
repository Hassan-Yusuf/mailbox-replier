using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker;

public sealed class ScanInboxUseCase
{
    private static readonly Regex DirectQuestionCuePattern = new(
        @"\b(can you|could you|would you|are you|do you|please (send|confirm|let me know|advise|provide))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HumanReplyCuePattern = new(
        @"\b(hi|hello|dear|thanks|thank you|please|let me know|are you available|can you|could you|would you)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IIncomingEmailReader _incomingEmailReader;
    private readonly IEmailClassifier _classifier;
    private readonly IReplyScopeEvaluator _replyScopeEvaluator;
    private readonly IEmailRequestAnalyzer _emailRequestAnalyzer;
    private readonly IDraftEligibilityAssessor _draftEligibilityAssessor;
    private readonly IReplyShapePlanner _replyShapePlanner;
    private readonly IStyleProfileSelector _styleProfileSelector;
    private readonly IStyleExampleStore _styleExampleStore;
    private readonly IReplyDraftGenerator _replyDraftGenerator;
    private readonly IDraftGroundingChecker _draftGroundingChecker;
    private readonly ICoverageVerifier _coverageVerifier;
    private readonly IDraftStore _draftStore;
    private readonly ILogger<ScanInboxUseCase> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly ImapOptions _imapOptions;
    private readonly LlmOptions _llmOptions;
    private readonly EmbeddingOptions _embeddingOptions;

    public ScanInboxUseCase(
        IIncomingEmailReader incomingEmailReader,
        IEmailClassifier classifier,
        IReplyScopeEvaluator replyScopeEvaluator,
        IEmailRequestAnalyzer emailRequestAnalyzer,
        IDraftEligibilityAssessor draftEligibilityAssessor,
        IReplyShapePlanner replyShapePlanner,
        IStyleProfileSelector styleProfileSelector,
        IStyleExampleStore styleExampleStore,
        IReplyDraftGenerator replyDraftGenerator,
        IDraftGroundingChecker draftGroundingChecker,
        ICoverageVerifier coverageVerifier,
        IDraftStore draftStore,
        IOptions<WorkerOptions> workerOptions,
        IOptions<ImapOptions> imapOptions,
        IOptions<LlmOptions> llmOptions,
        IOptions<EmbeddingOptions> embeddingOptions,
        ILogger<ScanInboxUseCase> logger)
    {
        _incomingEmailReader = incomingEmailReader;
        _classifier = classifier;
        _replyScopeEvaluator = replyScopeEvaluator;
        _emailRequestAnalyzer = emailRequestAnalyzer;
        _draftEligibilityAssessor = draftEligibilityAssessor;
        _replyShapePlanner = replyShapePlanner;
        _styleProfileSelector = styleProfileSelector;
        _styleExampleStore = styleExampleStore;
        _replyDraftGenerator = replyDraftGenerator;
        _draftGroundingChecker = draftGroundingChecker;
        _coverageVerifier = coverageVerifier;
        _draftStore = draftStore;
        _workerOptions = workerOptions.Value;
        _imapOptions = imapOptions.Value;
        _llmOptions = llmOptions.Value;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task<InboxScanRunResult> ExecuteAsync(
        HashSet<uint> processedImapUids,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var candidatesEvaluated = 0;
        var skippedCount = 0;
        var skipCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var draftCount = 0;
        long? firstDraftId = null;
        uint? firstDraftSourceImapUid = null;
        var maxDraftsPerRun = Math.Max(1, _workerOptions.MaxDraftsPerRun);

        var allUnreadUids = await _incomingEmailReader.GetUnreadUidsAsync(processedImapUids, cancellationToken);

        if (allUnreadUids.Count == 0)
        {
            _logger.LogInformation("No undrafted unread email found. Exiting without creating a draft.");
            return new InboxScanRunResult(
                startedAtUtc,
                DateTimeOffset.UtcNow,
                1,
                0,
                0,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                false,
                0,
                null,
                null);
        }

        var batchSize = Math.Max(1, _imapOptions.PreferredBatchSize);
        var batchUids = allUnreadUids.Take(batchSize).ToList();

        _logger.LogInformation(
            "Found {TotalUndraftedCount} undrafted unread email(s). Loading the newest {BatchSize} for evaluation.",
            allUnreadUids.Count,
            batchUids.Count);

        var candidates = await _incomingEmailReader.GetEmailsBatchAsync(batchUids, cancellationToken);

        _logger.LogInformation(
            "Evaluating {CandidateCount} unread candidate email(s).",
            candidates.Count);

        var prioritizedCandidates = candidates
            .OrderByDescending(ScoreLikelyReplyCandidate)
            .ThenByDescending(candidate => candidate.ReceivedAtUtc)
            .ThenByDescending(candidate => candidate.ImapUid)
            .ToArray();

        _logger.LogInformation(
            "Prioritized {CandidateCount} candidate email(s) so likely conversational mail is evaluated first.",
            prioritizedCandidates.Length);

        foreach (var candidate in prioritizedCandidates)
        {
            if (draftCount >= maxDraftsPerRun)
            {
                break;
            }

                candidatesEvaluated++;
                var classification = await _classifier.ClassifyAsync(candidate, cancellationToken);
                _logger.LogInformation(
                    "Classifier evaluated email UID {ImapUid}. RequiresReply={RequiresReply}; ReasonCode={ReasonCode}; DecisionSource={DecisionSource}; TraceCount={TraceCount}.",
                    candidate.ImapUid,
                    classification.RequiresReply,
                    classification.ReasonCode,
                    classification.DecisionSource,
                    classification.Trace.Evaluations.Count);

                if (classification.RequiresReply)
                {
                    var replyScopeDecision = _replyScopeEvaluator.Evaluate(candidate);
                    if (!replyScopeDecision.IsAllowed)
                    {
                        await StoreDraftIneligibleAsync(
                            candidate,
                            replyScopeDecision.Reason,
                            0,
                            processedImapUids,
                            skipCounts,
                            cancellationToken);
                        skippedCount++;
                        continue;
                    }

                    var styleProfileSelection = await _styleProfileSelector.GetOrBuildProfileSelectionAsync(candidate, cancellationToken);
                    var styleProfile = styleProfileSelection.Profile;
                    var styleExamples = await _styleExampleStore.GetBySegmentAsync(styleProfile.SegmentKey, cancellationToken);
                    _logger.LogInformation(
                        "Active style profile: Segment={SegmentKey}; Reason={SelectionReason}; Greeting={Greeting}; Closing={Closing}; Tone={Tone}; SignaturePresent={HasSignature}; CommonPhraseCount={CommonPhraseCount}; SampleSize={SampleSize}; StyleExampleCount={StyleExampleCount}.",
                        styleProfile.SegmentKey,
                        styleProfileSelection.SelectionReason,
                        styleProfile.Greeting,
                        styleProfile.Closing,
                        styleProfile.Tone,
                        !string.IsNullOrWhiteSpace(styleProfile.Signature),
                        styleProfile.CommonPhrases.Count,
                        styleProfile.SampleSize,
                        styleExamples.Count);

                    var analysis = await _emailRequestAnalyzer.AnalyzeAsync(candidate, cancellationToken);
                    _logger.LogInformation(
                        "Email analysis for UID {ImapUid}: Asks={AskCount}; DecisionBranches={DecisionBranchCount}; Deadlines={DeadlineCount}; Urgency={Urgency}.",
                        candidate.ImapUid,
                        analysis.Asks.Count,
                        analysis.DecisionBranches.Count,
                        analysis.StatedDeadlines.Count,
                        analysis.Urgency);

                    var draftEligibility = _draftEligibilityAssessor.Assess(candidate, styleProfile, analysis);
                    _logger.LogInformation(
                        "Draft eligibility for email UID {ImapUid}: Decision={Decision}; Reason={Reason}; AmbiguityScore={AmbiguityScore:0.00}.",
                        candidate.ImapUid,
                        draftEligibility.Decision,
                        draftEligibility.Reason,
                        draftEligibility.AmbiguityScore);

                    if (draftEligibility.IsIneligible)
                    {
                        await StoreDraftIneligibleAsync(
                            candidate,
                            draftEligibility.Reason,
                            draftEligibility.AmbiguityScore,
                            processedImapUids,
                            skipCounts,
                            cancellationToken);
                        skippedCount++;
                        continue;
                    }

                    var replyPlan = _replyShapePlanner.Plan(candidate, styleProfile, analysis);
                    var selectedReplyOptions = draftEligibility.IsVariantCandidate
                        ? replyPlan.Options.Take(3).ToArray()
                        : replyPlan.Options.Take(1).ToArray();

                    var effectiveEligibilityDecision = draftEligibility.IsVariantCandidate && selectedReplyOptions.Length > 1
                        ? DraftEligibilityDecisions.VariantCandidate
                        : DraftEligibilityDecisions.SingleDraft;

                    _logger.LogInformation(
                        "Reply shape planning for email UID {ImapUid}: MultipleOptions={MultipleOptions}; Options={ReplyOptions}.",
                        candidate.ImapUid,
                        effectiveEligibilityDecision == DraftEligibilityDecisions.VariantCandidate,
                        string.Join(", ", selectedReplyOptions.Select(option => $"{option.Label} ({option.Shape}, {option.ConfidenceScore:0.00})")));
                    var variants = new List<DraftVariantRecord>(selectedReplyOptions.Length);

                    foreach (var option in selectedReplyOptions)
                    {
                        var draftText = await _replyDraftGenerator.GenerateDraftAsync(
                            candidate,
                            styleProfile,
                            styleExamples,
                            analysis,
                            option.Shape,
                            option.Label,
                            option.MustAddressAsks ?? [],
                            cancellationToken);

                        var groundingWarning = _draftGroundingChecker.Check(
                            candidate,
                            draftText,
                            analysis,
                            option.MustAddressAsks ?? []);

                        var coverageWarning = (!_llmOptions.UseMock && _embeddingOptions.Enabled && analysis is not null)
                            ? await _coverageVerifier.VerifyAsync(draftText, option.Shape, analysis, cancellationToken)
                            : null;

                        variants.Add(new DraftVariantRecord
                        {
                            SortOrder = variants.Count,
                            ReplyShape = option.Shape,
                            ReplyShapeLabel = option.Label,
                            Body = draftText,
                            ConfidenceScore = option.ConfidenceScore,
                            StyleSegmentUsed = styleProfile.SegmentKey,
                            GroundingWarning = groundingWarning,
                            CoverageWarning = coverageWarning
                        });
                    }

                    _logger.LogInformation(
                        "Draft variants prepared for email UID {ImapUid}: {VariantSummaries}.",
                        candidate.ImapUid,
                        string.Join(
                            " | ",
                            variants.Select(variant =>
                                $"#{variant.SortOrder + 1} {variant.ReplyShapeLabel} ({variant.ReplyShape}, {variant.ConfidenceScore:0.00}): {BuildDraftPreview(variant.Body)}")));

                    var strategyId = await _draftStore.InsertStrategyAsync(
                        new DraftStrategyRecord
                        {
                            SourceImapUid = candidate.ImapUid,
                            SourceMessageId = candidate.MessageId,
                            FromAddress = candidate.From.Address,
                            Subject = candidate.Subject,
                            EligibilityDecision = effectiveEligibilityDecision,
                            EligibilityReason = draftEligibility.Reason,
                            AmbiguityScore = draftEligibility.AmbiguityScore,
                            PlannedReplyShapes = selectedReplyOptions.Select(option => option.Shape).ToArray(),
                            VariantCount = variants.Count,
                            CreatedAtUtc = DateTimeOffset.UtcNow
                        },
                        cancellationToken);

                    _logger.LogInformation(
                        "Stored draft strategy {StrategyId} for email UID {ImapUid}.",
                        strategyId,
                        candidate.ImapUid);

                    var aggregateConfidence = variants.Count > 0
                        ? variants.Average(variant => variant.ConfidenceScore)
                        : (double?)null;
                    var confidenceTier = aggregateConfidence.HasValue
                        ? ConfidenceTierClassifier.FromScore(aggregateConfidence.Value)
                        : (ConfidenceTier?)null;

                    var draftSet = new DraftSetRecord
                    {
                        SourceImapUid = candidate.ImapUid,
                        SourceMessageId = candidate.MessageId,
                        FromAddress = candidate.From.Address,
                        Subject = candidate.Subject,
                        OriginalBodyPreview = BuildPreview(candidate.BodyText),
                        OriginalEmailBody = BuildOriginalEmailBody(candidate.BodyText),
                        SourceReceivedAtUtc = candidate.ReceivedAtUtc,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        LlmMode = _replyDraftGenerator.UseMock ? "mock" : "remote",
                        IsAmbiguous = variants.Count > 1,
                        Status = DraftSetStatuses.Pending,
                        AnalysisJson = JsonSerializer.Serialize(analysis),
                        AggregateConfidenceScore = aggregateConfidence,
                        ConfidenceTier = confidenceTier,
                        Variants = variants
                    };

                    var insertedId = await _draftStore.InsertAsync(draftSet, cancellationToken);
                    _logger.LogInformation(
                        "SQLite insert succeeded. Draft set Id {DraftSetId} created for email UID {ImapUid}.",
                        insertedId,
                        candidate.ImapUid);

                    draftCount++;
                    firstDraftId ??= insertedId;
                    firstDraftSourceImapUid ??= candidate.ImapUid;
                    processedImapUids.Add(candidate.ImapUid);
                }
                else
                {
                    var skippedEmail = new SkippedEmailRecord
                    {
                        SourceImapUid = candidate.ImapUid,
                        SourceMessageId = candidate.MessageId,
                        FromAddress = candidate.From.Address,
                        Subject = candidate.Subject,
                        ReasonCode = classification.ReasonCode,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };

                    var skippedId = await _draftStore.InsertSkippedAsync(skippedEmail, cancellationToken);
                    processedImapUids.Add(candidate.ImapUid);
                    skippedCount++;
                    Increment(skipCounts, classification.ReasonCode);

                    _logger.LogInformation(
                        "Stored skipped-email record {SkippedId} for email UID {ImapUid} with reason {ReasonCode}.",
                        skippedId,
                        candidate.ImapUid,
                        classification.ReasonCode);
                }
        }

        if (draftCount >= maxDraftsPerRun)
        {
            _logger.LogInformation(
                "Reached MaxDraftsPerRun limit ({MaxDraftsPerRun}). Ending the run.",
                maxDraftsPerRun);
        }

        return new InboxScanRunResult(
            startedAtUtc,
            DateTimeOffset.UtcNow,
            1,
            candidatesEvaluated,
            skippedCount,
            new Dictionary<string, int>(skipCounts, StringComparer.OrdinalIgnoreCase),
            draftCount > 0,
            draftCount,
            firstDraftId,
            firstDraftSourceImapUid);
    }

    private static void Increment(IDictionary<string, int> counts, string reasonCode)
    {
        if (counts.TryGetValue(reasonCode, out var current))
        {
            counts[reasonCode] = current + 1;
            return;
        }

        counts[reasonCode] = 1;
    }

    private async Task StoreDraftIneligibleAsync(
        IncomingEmail candidate,
        string reason,
        double ambiguityScore,
        ISet<uint> processedImapUids,
        IDictionary<string, int> skipCounts,
        CancellationToken cancellationToken)
    {
        var ineligibleStrategyId = await _draftStore.InsertStrategyAsync(
            new DraftStrategyRecord
            {
                SourceImapUid = candidate.ImapUid,
                SourceMessageId = candidate.MessageId,
                FromAddress = candidate.From.Address,
                Subject = candidate.Subject,
                EligibilityDecision = DraftEligibilityDecisions.Ineligible,
                EligibilityReason = reason,
                AmbiguityScore = ambiguityScore,
                PlannedReplyShapes = [],
                VariantCount = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            cancellationToken);

        var ineligibleEmail = new SkippedEmailRecord
        {
            SourceImapUid = candidate.ImapUid,
            SourceMessageId = candidate.MessageId,
            FromAddress = candidate.From.Address,
            Subject = candidate.Subject,
            ReasonCode = ClassificationReasonCodes.DraftIneligible,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var ineligibleSkippedId = await _draftStore.InsertSkippedAsync(ineligibleEmail, cancellationToken);
        processedImapUids.Add(candidate.ImapUid);
        Increment(skipCounts, ClassificationReasonCodes.DraftIneligible);

        _logger.LogInformation(
            "Email UID {ImapUid} was draft-ineligible. Stored strategy {StrategyId} and skip record {SkippedId}. Reason={Reason}.",
            candidate.ImapUid,
            ineligibleStrategyId,
            ineligibleSkippedId,
            reason);
    }

    private static string BuildPreview(string bodyText)
    {
        const int maxLength = 280;
        if (bodyText.Length <= maxLength)
        {
            return bodyText;
        }

        return bodyText[..maxLength].TrimEnd() + "...";
    }

    private static string? BuildOriginalEmailBody(string? bodyText)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
            return null;

        const int maxLength = 2000;
        if (bodyText.Length <= maxLength)
            return bodyText;

        return bodyText[..maxLength] + "[…truncated]";
    }

    private static string BuildDraftPreview(string draftText)
    {
        const int maxLength = 120;
        var singleLine = draftText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return singleLine[..maxLength].TrimEnd() + "...";
    }

    private static int ScoreLikelyReplyCandidate(IncomingEmail candidate)
    {
        var score = 0;
        var subject = candidate.Subject.Trim();
        var body = candidate.BodyText.Trim();
        var fromAddress = candidate.FromAddress.Trim().ToLowerInvariant();
        var combined = $"{subject}\n{body}";

        if (candidate.HasListIdHeader || candidate.HasListUnsubscribeHeader || candidate.IsAutoSubmitted)
        {
            score -= 8;
        }

        if (candidate.Precedence.Contains("bulk", StringComparison.OrdinalIgnoreCase) ||
            candidate.Precedence.Contains("list", StringComparison.OrdinalIgnoreCase) ||
            candidate.Precedence.Contains("junk", StringComparison.OrdinalIgnoreCase))
        {
            score -= 6;
        }

        if (fromAddress.StartsWith("noreply@", StringComparison.OrdinalIgnoreCase) ||
            fromAddress.StartsWith("no-reply@", StringComparison.OrdinalIgnoreCase) ||
            fromAddress.StartsWith("donotreply@", StringComparison.OrdinalIgnoreCase) ||
            fromAddress.StartsWith("newsletter@", StringComparison.OrdinalIgnoreCase) ||
            fromAddress.StartsWith("updates@", StringComparison.OrdinalIgnoreCase) ||
            fromAddress.StartsWith("info@", StringComparison.OrdinalIgnoreCase))
        {
            score -= 5;
        }

        if (ContainsAny(subject,
                "weekly update",
                "monthly update",
                "newsletter",
                "statement",
                "privacy",
                "policy",
                "terms",
                "review",
                "survey",
                "account statement",
                "payment confirmation",
                "order confirmation"))
        {
            score -= 4;
        }

        if (DirectQuestionCuePattern.IsMatch(combined))
        {
            score += 7;
        }

        if (combined.Contains('?', StringComparison.Ordinal))
        {
            score += 3;
        }

        if (HumanReplyCuePattern.IsMatch(combined))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(candidate.FromDisplayName) &&
            !candidate.FromDisplayName.Equals(candidate.FromAddress, StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static bool ContainsAny(string value, params string[] snippets)
    {
        foreach (var snippet in snippets)
        {
            if (value.Contains(snippet, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
