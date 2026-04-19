using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmailCopilot.Worker.Tests;

[TestFixture]
public sealed class ScanInboxUseCaseTests
{
    [Test]
    public async Task Should_return_without_creating_a_draft_when_no_candidates_exist()
    {
        var reader = new FakeIncomingEmailReader([]);
        var classifier = new FakeEmailClassifier();
        var replyScopeEvaluator = new FakeReplyScopeEvaluator();
        var analyzer = new FakeEmailRequestAnalyzer();
        var draftEligibilityAssessor = new FakeDraftEligibilityAssessor();
        var replyShapePlanner = new FakeReplyShapePlanner();
        var styleSelector = new FakeStyleProfileSelector();
        var draftGenerator = new FakeReplyDraftGenerator();
        var groundingChecker = new FakeDraftGroundingChecker();
        var draftStore = new FakeDraftStore();
        var useCase = CreateUseCase(reader, classifier, replyScopeEvaluator, analyzer, draftEligibilityAssessor, replyShapePlanner, styleSelector, draftGenerator, groundingChecker, draftStore);

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        Assert.That(result.DraftCreated, Is.False);
        Assert.That(result.DraftCount, Is.EqualTo(0));
        Assert.That(result.CandidateWindowsScanned, Is.EqualTo(1));
        Assert.That(result.CandidatesEvaluated, Is.EqualTo(0));
        Assert.That(result.SkippedCount, Is.EqualTo(0));
        Assert.That(draftStore.InsertedDraftSets, Is.Empty);
        Assert.That(draftStore.InsertedSkips, Is.Empty);
    }

    [Test]
    public async Task Should_skip_then_create_draft_when_actionable_email_is_found()
    {
        var skippedEmail = CreateEmail(100, "newsletter@example.com", "Newsletter", "Weekly update");
        var actionableEmail = CreateEmail(101, "carmel.dennison@code3research.co.uk", "Project spaces available", "Can you confirm if you are still interested?");
        var reader = new FakeIncomingEmailReader([skippedEmail, actionableEmail]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [100] = new ClassificationResult(
                    false,
                    ClassificationReasonCodes.ListOrBroadcastMail,
                    ClassificationDecisionSources.BuiltIn,
                    new DecisionTrace(
                    [
                        new RuleEvaluation("BUILT_IN:LIST_OR_BROADCAST", true, ClassificationReasonCodes.ListOrBroadcastMail, ClassificationDecisionSources.BuiltIn, "test")
                    ])),
                [101] = new ClassificationResult(
                    true,
                    ClassificationReasonCodes.DefaultReply,
                    ClassificationDecisionSources.BuiltIn,
                    new DecisionTrace(
                    [
                        new RuleEvaluation("BUILT_IN:DEFAULT_REPLY", true, ClassificationReasonCodes.DefaultReply, ClassificationDecisionSources.BuiltIn, "test")
                    ]))
            }
        };

        var styleSelector = new FakeStyleProfileSelector();
        var replyScopeEvaluator = new FakeReplyScopeEvaluator();
        var analyzer = new FakeEmailRequestAnalyzer();
        var draftEligibilityAssessor = new FakeDraftEligibilityAssessor();
        var replyShapePlanner = new FakeReplyShapePlanner();
        var draftGenerator = new FakeReplyDraftGenerator { DraftText = "Hi,\n\nI'm interested. Are there still spaces?" };
        var groundingChecker = new FakeDraftGroundingChecker();
        var draftStore = new FakeDraftStore { NextDraftId = 42 };
        var useCase = CreateUseCase(reader, classifier, replyScopeEvaluator, analyzer, draftEligibilityAssessor, replyShapePlanner, styleSelector, draftGenerator, groundingChecker, draftStore);
        var processed = new HashSet<uint>();

        var result = await useCase.ExecuteAsync(processed, CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(1));
        Assert.That(result.DraftId, Is.EqualTo(42));
        Assert.That(result.DraftSourceImapUid, Is.EqualTo(101));
        Assert.That(result.CandidateWindowsScanned, Is.EqualTo(1));
        Assert.That(result.CandidatesEvaluated, Is.EqualTo(1));
        Assert.That(result.SkippedCount, Is.EqualTo(0));
        Assert.That(processed, Does.Not.Contain(100));
        Assert.That(draftStore.InsertedSkips, Is.Empty);
        Assert.That(draftStore.InsertedDraftSets, Has.Count.EqualTo(1));
        Assert.That(draftStore.InsertedDraftSets[0].OriginalBodyPreview, Does.Contain("Can you confirm"));
        Assert.That(draftStore.InsertedDraftSets[0].Variants, Has.Count.EqualTo(1));
        Assert.That(draftStore.InsertedDraftSets[0].Variants[0].ReplyShape, Is.EqualTo(ReplyShapes.Acknowledge));
        Assert.That(draftStore.InsertedDraftSets[0].Variants[0].Body, Does.Contain("I'm interested"));
    }

    [Test]
    public async Task Should_draft_prioritized_actionable_email_when_skippable_emails_exist_in_batch()
    {
        var skippedFirst = CreateEmail(200, "newsletter@example.com", "Weekly update", "View in browser");
        var skippedSecond = CreateEmail(201, "payments@example.com", "Payment confirmation", "Paid");
        var actionableLater = CreateEmail(202, "ross@loc8me.co.uk", "Broken chair", "Can you send photos?");

        var reader = new FakeIncomingEmailReader([skippedFirst, skippedSecond, actionableLater]);

        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [200] = SkipResult(ClassificationReasonCodes.ListOrBroadcastMail),
                [201] = SkipResult(ClassificationReasonCodes.TransactionalNotification),
                [202] = DefaultReplyResult()
            }
        };

        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer(),
            new FakeDraftEligibilityAssessor(),
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi,\n\nI'll send the images shortly." },
            new FakeDraftGroundingChecker(),
            new FakeDraftStore { NextDraftId = 77 });

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        // Prioritization scores actionableLater highest (has "Can you" + "?"), so it is
        // evaluated first and drafted; the skippable emails are never reached.
        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(1));
        Assert.That(result.CandidateWindowsScanned, Is.EqualTo(1));
        Assert.That(result.CandidatesEvaluated, Is.EqualTo(1));
        Assert.That(result.SkippedCount, Is.EqualTo(0));
        Assert.That(result.DraftSourceImapUid, Is.EqualTo(202));
    }

    [Test]
    public async Task Should_persist_multiple_variants_when_intent_resolution_is_ambiguous()
    {
        var actionableEmail = CreateEmail(301, "court@example.com", "Hearing update", "The hearing is no longer going ahead.");
        var reader = new FakeIncomingEmailReader([actionableEmail]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [301] = DefaultReplyResult()
            }
        };

        var replyShapePlanner = new FakeReplyShapePlanner
        {
            Result = new ReplyPlan(
                [
                    new ReplyShapeOption(ReplyShapes.Acknowledge, "Acknowledge only", 0.55),
                    new ReplyShapeOption(ReplyShapes.AcknowledgeAndAsk, "Acknowledge and ask", 0.52)
                ])
        };

        var draftGenerator = new FakeReplyDraftGenerator
        {
            DraftTextsByIntent =
            {
                [ReplyShapes.Acknowledge] = "Hi,\n\nUnderstood.",
                [ReplyShapes.AcknowledgeAndAsk] = "Hi,\n\nUnderstood. Can you confirm the reason for the cancellation?"
            }
        };

        var draftStore = new FakeDraftStore();
        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer
            {
                ResultsByUid =
                {
                    [301] = new EmailRequestAnalysis(
                        [],
                        [new DecisionBranch("Hearing cancelled", [ReplyShapes.Acknowledge, ReplyShapes.AcknowledgeAndAsk])],
                        [],
                        UrgencyLevels.Low,
                        false)
                }
            },
            new FakeDraftEligibilityAssessor
            {
                Result = new DraftEligibilityResult(DraftEligibilityDecisions.VariantCandidate, "ambiguous", 0.72)
            },
            replyShapePlanner,
            new FakeStyleProfileSelector(),
            draftGenerator,
            new FakeDraftGroundingChecker(),
            draftStore);

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(1));
        Assert.That(draftStore.InsertedDraftSets, Has.Count.EqualTo(1));
        Assert.That(draftStore.InsertedDraftSets[0].IsAmbiguous, Is.True);
        Assert.That(draftStore.InsertedDraftSets[0].Variants, Has.Count.EqualTo(2));
        Assert.That(draftStore.InsertedDraftSets[0].Variants.Select(v => v.ReplyShape), Is.EquivalentTo(new[]
        {
            ReplyShapes.Acknowledge,
            ReplyShapes.AcknowledgeAndAsk
        }));
        Assert.That(draftStore.InsertedStrategies, Has.Count.EqualTo(1));
        Assert.That(draftStore.InsertedStrategies[0].EligibilityDecision, Is.EqualTo(DraftEligibilityDecisions.VariantCandidate));
    }

    [Test]
    public async Task Should_mark_email_ineligible_and_continue_when_draft_eligibility_rejects_it()
    {
        var ineligibleEmail = CreateEmail(401, "member@updates.example.com", "Policy question", "Hi, can you help me with this policy issue?");
        var actionableEmail = CreateEmail(402, "ross@loc8me.co.uk", "Broken chair update", "Broken chair in room three.");
        var reader = new FakeIncomingEmailReader([ineligibleEmail, actionableEmail]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [401] = DefaultReplyResult(),
                [402] = DefaultReplyResult()
            }
        };

        var assessor = new FakeDraftEligibilityAssessor();
        assessor.ResultsByUid[401] = new DraftEligibilityResult(DraftEligibilityDecisions.Ineligible, "no conversational signal", 0);
        assessor.ResultsByUid[402] = new DraftEligibilityResult(DraftEligibilityDecisions.SingleDraft, "one best-fit reply is likely", 0.15);

        var draftStore = new FakeDraftStore { NextDraftId = 88 };
        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer(),
            assessor,
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi,\n\nI'll send photos shortly." },
            new FakeDraftGroundingChecker(),
            draftStore);

        var processed = new HashSet<uint>();
        var result = await useCase.ExecuteAsync(processed, CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(1));
        Assert.That(result.DraftId, Is.EqualTo(88));
        Assert.That(result.CandidatesEvaluated, Is.EqualTo(2));
        Assert.That(result.SkippedCount, Is.EqualTo(1));
        Assert.That(result.SkipsByReasonCode[ClassificationReasonCodes.DraftIneligible], Is.EqualTo(1));
        Assert.That(draftStore.InsertedStrategies, Has.Count.EqualTo(2));
        Assert.That(draftStore.InsertedStrategies[0].EligibilityDecision, Is.EqualTo(DraftEligibilityDecisions.Ineligible));
        Assert.That(draftStore.InsertedSkips[0].ReasonCode, Is.EqualTo(ClassificationReasonCodes.DraftIneligible));
        Assert.That(processed, Does.Contain(401));
    }

    [Test]
    public async Task Should_mark_email_ineligible_when_reply_scope_excludes_sender_domain()
    {
        var outOfScopeEmail = CreateEmail(501, "person@outside.example.com", "Urgent question", "Hi, can you help with this today?");
        var actionableEmail = CreateEmail(502, "ross@loc8me.co.uk", "Broken chair update", "Broken chair in room three.");
        var reader = new FakeIncomingEmailReader([outOfScopeEmail, actionableEmail]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [501] = DefaultReplyResult(),
                [502] = DefaultReplyResult()
            }
        };

        var scopeEvaluator = new FakeReplyScopeEvaluator();
        scopeEvaluator.ResultsByUid[501] = new ReplyScopeDecision(false, "reply scope excludes sender domain outside.example.com");
        scopeEvaluator.ResultsByUid[502] = new ReplyScopeDecision(true, "reply scope allows sender domain loc8me.co.uk");

        var draftStore = new FakeDraftStore { NextDraftId = 99 };
        var useCase = CreateUseCase(
            reader,
            classifier,
            scopeEvaluator,
            new FakeEmailRequestAnalyzer(),
            new FakeDraftEligibilityAssessor(),
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi,\n\nI'll send photos shortly." },
            new FakeDraftGroundingChecker(),
            draftStore);

        var processed = new HashSet<uint>();
        var result = await useCase.ExecuteAsync(processed, CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(1));
        Assert.That(result.SkippedCount, Is.EqualTo(1));
        Assert.That(result.SkipsByReasonCode[ClassificationReasonCodes.DraftIneligible], Is.EqualTo(1));
        Assert.That(draftStore.InsertedStrategies, Has.Count.EqualTo(2));
        Assert.That(draftStore.InsertedStrategies[0].EligibilityDecision, Is.EqualTo(DraftEligibilityDecisions.Ineligible));
        Assert.That(draftStore.InsertedStrategies[0].EligibilityReason, Does.Contain("reply scope excludes sender domain"));
        Assert.That(draftStore.InsertedSkips[0].ReasonCode, Is.EqualTo(ClassificationReasonCodes.DraftIneligible));
        Assert.That(processed, Does.Contain(501));
    }

    [Test]
    public async Task Should_create_multiple_drafts_in_one_run_when_worker_option_allows_it()
    {
        var first = CreateEmail(601, "first@example.com", "Question one", "Can you send the first update?");
        var second = CreateEmail(602, "second@example.com", "Question two", "Can you send the second update?");
        var reader = new FakeIncomingEmailReader([first, second]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [601] = DefaultReplyResult(),
                [602] = DefaultReplyResult()
            }
        };

        var draftStore = new FakeDraftStore();
        draftStore.NextDraftIds.Enqueue(701L);
        draftStore.NextDraftIds.Enqueue(702L);
        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer(),
            new FakeDraftEligibilityAssessor(),
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi,\n\nTest draft." },
            new FakeDraftGroundingChecker(),
            draftStore,
            maxDraftsPerRun: 2);

        var processed = new HashSet<uint>();
        var result = await useCase.ExecuteAsync(processed, CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(2));
        Assert.That(result.DraftId, Is.EqualTo(701));
        Assert.That(result.DraftSourceImapUid, Is.AnyOf(601u, 602u));
        Assert.That(draftStore.InsertedDraftSets, Has.Count.EqualTo(2));
        Assert.That(processed, Does.Contain(601));
        Assert.That(processed, Does.Contain(602));
    }

    [Test]
    public async Task Should_prioritize_likely_conversational_candidates_before_broadcast_like_mail()
    {
        var newsletter = CreateEmail(650, "newsletter@example.com", "Weekly update", "View in browser and manage preferences here.");
        var actionable = CreateEmail(651, "sophie@cateringelite.co.uk", "Shift available", "Hi Hassan, are you available to cover the shift on Saturday?");
        var reader = new FakeIncomingEmailReader([newsletter, actionable]);
        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [650] = SkipResult(ClassificationReasonCodes.ListOrBroadcastMail),
                [651] = DefaultReplyResult()
            }
        };

        var draftStore = new FakeDraftStore { NextDraftId = 900 };
        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer(),
            new FakeDraftEligibilityAssessor(),
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi Sophie,\n\nYes, I can cover the shift." },
            new FakeDraftGroundingChecker(),
            draftStore);

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.CandidatesEvaluated, Is.EqualTo(1));
        Assert.That(result.SkippedCount, Is.EqualTo(0));
        Assert.That(result.DraftSourceImapUid, Is.EqualTo(651));
    }

    [Test]
    public async Task Should_not_reload_another_candidate_window_after_creating_drafts_from_current_batch()
    {
        var first = CreateEmail(701, "first@example.com", "Question one", "Can you send the first update?");
        var second = CreateEmail(702, "second@example.com", "Question two", "Can you send the second update?");
        var third = CreateEmail(703, "third@example.com", "Question three", "Can you send the third update?");

        var reader = new FakeIncomingEmailReader([first, second, third]);

        var classifier = new FakeEmailClassifier
        {
            ResultsByUid =
            {
                [701] = DefaultReplyResult(),
                [702] = DefaultReplyResult(),
                [703] = DefaultReplyResult(),
                [704] = DefaultReplyResult()
            }
        };

        var draftStore = new FakeDraftStore();
        draftStore.NextDraftIds.Enqueue(801L);
        draftStore.NextDraftIds.Enqueue(802L);

        var useCase = CreateUseCase(
            reader,
            classifier,
            new FakeReplyScopeEvaluator(),
            new FakeEmailRequestAnalyzer(),
            new FakeDraftEligibilityAssessor(),
            new FakeReplyShapePlanner(),
            new FakeStyleProfileSelector(),
            new FakeReplyDraftGenerator { DraftText = "Hi,\n\nTest draft." },
            new FakeDraftGroundingChecker(),
            draftStore,
            maxDraftsPerRun: 2);

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        Assert.That(result.DraftCreated, Is.True);
        Assert.That(result.DraftCount, Is.EqualTo(2));
        Assert.That(reader.CallCount, Is.EqualTo(1));
        Assert.That(draftStore.InsertedDraftSets, Has.Count.EqualTo(2));
    }

    private static ScanInboxUseCase CreateUseCase(
        IIncomingEmailReader reader,
        IEmailClassifier classifier,
        IReplyScopeEvaluator replyScopeEvaluator,
        IEmailRequestAnalyzer emailRequestAnalyzer,
        IDraftEligibilityAssessor draftEligibilityAssessor,
        IReplyShapePlanner replyShapePlanner,
        IStyleProfileSelector styleSelector,
        IReplyDraftGenerator draftGenerator,
        IDraftGroundingChecker draftGroundingChecker,
        IDraftStore draftStore,
        IStyleExampleStore? styleExampleStore = null,
        int maxDraftsPerRun = 1) =>
        new(
            reader,
            classifier,
            replyScopeEvaluator,
            emailRequestAnalyzer,
            draftEligibilityAssessor,
            replyShapePlanner,
            styleSelector,
            styleExampleStore ?? new FakeStyleExampleStore(),
            draftGenerator,
            draftGroundingChecker,
            draftStore,
            Options.Create(new WorkerOptions { MaxDraftsPerRun = maxDraftsPerRun }),
            Options.Create(new ImapOptions()),
            NullLogger<ScanInboxUseCase>.Instance);

    private static ClassificationResult SkipResult(string reasonCode) =>
        new(
            false,
            reasonCode,
            ClassificationDecisionSources.BuiltIn,
            new DecisionTrace(
            [
                new RuleEvaluation($"BUILT_IN:{reasonCode}", true, reasonCode, ClassificationDecisionSources.BuiltIn, "test")
            ]));

    private static ClassificationResult DefaultReplyResult() =>
        new(
            true,
            ClassificationReasonCodes.DefaultReply,
            ClassificationDecisionSources.BuiltIn,
            new DecisionTrace(
            [
                new RuleEvaluation("BUILT_IN:DEFAULT_REPLY", true, ClassificationReasonCodes.DefaultReply, ClassificationDecisionSources.BuiltIn, "test")
            ]));

    private static IncomingEmail CreateEmail(uint uid, string address, string subject, string body) =>
        new(
            uid,
            Guid.NewGuid().ToString("N"),
            EmailAddress.FromParts(address, string.Empty),
            subject,
            body,
            DateTimeOffset.UtcNow,
            false,
            false,
            string.Empty,
            false);

    private sealed class FakeIncomingEmailReader : IIncomingEmailReader
    {
        private readonly IReadOnlyList<IncomingEmail> _emails;

        public FakeIncomingEmailReader(IReadOnlyList<IncomingEmail> emails)
        {
            _emails = emails;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<uint>> GetUnreadUidsAsync(IReadOnlySet<uint> excludedImapUids, CancellationToken cancellationToken)
        {
            var uids = _emails
                .Select(e => e.ImapUid)
                .Where(uid => !excludedImapUids.Contains(uid))
                .ToList();
            return Task.FromResult<IReadOnlyList<uint>>(uids);
        }

        public Task<IReadOnlyList<IncomingEmail>> GetEmailsBatchAsync(IReadOnlyList<uint> imapUids, CancellationToken cancellationToken)
        {
            CallCount++;
            var uidSet = new HashSet<uint>(imapUids);
            var result = _emails.Where(e => uidSet.Contains(e.ImapUid)).ToList();
            return Task.FromResult<IReadOnlyList<IncomingEmail>>(result);
        }
    }

    private sealed class FakeEmailClassifier : IEmailClassifier
    {
        public Dictionary<uint, ClassificationResult> ResultsByUid { get; } = new();

        public Task<ClassificationResult> ClassifyAsync(IncomingEmail email, CancellationToken cancellationToken = default)
        {
            if (!ResultsByUid.TryGetValue(email.ImapUid, out var result))
            {
                throw new InvalidOperationException($"No classification result configured for UID {email.ImapUid}.");
            }

            return Task.FromResult(result);
        }
    }

    private sealed class FakeStyleProfileSelector : IStyleProfileSelector
    {
        public Task<StyleProfileSelection> GetOrBuildProfileSelectionAsync(IncomingEmail email, CancellationToken cancellationToken)
        {
            return Task.FromResult(new StyleProfileSelection(
                new StyleProfile(
                    "test",
                    "Hi",
                    "Thanks",
                    "concise",
                    string.Empty,
                    [],
                    8,
                    0.7,
                    0.2,
                    0.3,
                    0.1,
                    0.5,
                    0.05,
                    0.2,
                    1,
                    2,
                    0.45,
                    10,
                    DateTimeOffset.UtcNow),
                "test"));
        }
    }

    private sealed class FakeReplyDraftGenerator : IReplyDraftGenerator
    {
        public bool UseMock { get; init; } = true;

        public string DraftText { get; init; } = "Hi,\n\nTest draft.";

        public Dictionary<string, string> DraftTextsByIntent { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> GenerateDraftAsync(IncomingEmail email, StyleProfile styleProfile, IReadOnlyList<StyleExample> styleExamples, EmailRequestAnalysis analysis, string replyShape, string replyShapeLabel, IReadOnlyList<string> mustAddressAsks, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                DraftTextsByIntent.TryGetValue(replyShape, out var draftText)
                    ? draftText
                    : DraftText);
        }
    }

    private sealed class FakeStyleExampleStore : IStyleExampleStore
    {
        public Dictionary<string, IReadOnlyList<StyleExample>> ExamplesBySegment { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReplaceBySegmentAsync(string segmentKey, IReadOnlyList<StyleExample> examples, CancellationToken cancellationToken)
        {
            ExamplesBySegment[segmentKey] = examples;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StyleExample>> GetBySegmentAsync(string segmentKey, CancellationToken cancellationToken) =>
            Task.FromResult(
                ExamplesBySegment.TryGetValue(segmentKey, out var examples)
                    ? examples
                    : (IReadOnlyList<StyleExample>)[]);
    }

    private sealed class FakeReplyShapePlanner : IReplyShapePlanner
    {
        public ReplyPlan Result { get; init; } = new([new ReplyShapeOption(ReplyShapes.Acknowledge, "Acknowledge only", 0.5)]);

        public ReplyPlan Plan(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis) => Result;
    }

    private sealed class FakeReplyScopeEvaluator : IReplyScopeEvaluator
    {
        public ReplyScopeDecision Result { get; init; } = new(true, "reply scope allows all domains");

        public Dictionary<uint, ReplyScopeDecision> ResultsByUid { get; } = new();

        public ReplyScopeDecision Evaluate(IncomingEmail email) =>
            ResultsByUid.TryGetValue(email.ImapUid, out var result)
                ? result
                : Result;
    }

    private sealed class FakeDraftEligibilityAssessor : IDraftEligibilityAssessor
    {
        public DraftEligibilityResult Result { get; init; } = new(DraftEligibilityDecisions.SingleDraft, "one best-fit reply is likely", 0.1);

        public Dictionary<uint, DraftEligibilityResult> ResultsByUid { get; } = new();

        public DraftEligibilityResult Assess(IncomingEmail email, StyleProfile styleProfile, EmailRequestAnalysis analysis) =>
            ResultsByUid.TryGetValue(email.ImapUid, out var result)
                ? result
                : Result;
    }

    private sealed class FakeEmailRequestAnalyzer : IEmailRequestAnalyzer
    {
        public EmailRequestAnalysis Result { get; init; } = EmailRequestAnalysis.Empty;

        public Dictionary<uint, EmailRequestAnalysis> ResultsByUid { get; } = new();

        public Task<EmailRequestAnalysis> AnalyzeAsync(IncomingEmail email, CancellationToken cancellationToken) =>
            Task.FromResult(
                ResultsByUid.TryGetValue(email.ImapUid, out var result)
                    ? result
                    : Result);
    }

    private sealed class FakeDraftGroundingChecker : IDraftGroundingChecker
    {
        public string? Warning { get; init; }

        public string? Check(IncomingEmail email, string draftText, EmailRequestAnalysis analysis, IReadOnlyList<string> mustAddressAsks) => Warning;
    }

    private sealed class FakeDraftStore : IDraftStore
    {
        public long NextDraftId { get; init; } = 1;

        public Queue<long> NextDraftIds { get; } = new();

        public List<DraftSetRecord> InsertedDraftSets { get; } = [];

        public List<DraftStrategyRecord> InsertedStrategies { get; } = [];

        public List<SkippedEmailRecord> InsertedSkips { get; } = [];

        public Task<long> InsertAsync(DraftSetRecord draftSet, CancellationToken cancellationToken)
        {
            InsertedDraftSets.Add(draftSet);
            return Task.FromResult(NextDraftIds.Count > 0 ? NextDraftIds.Dequeue() : NextDraftId);
        }

        public Task<long> InsertStrategyAsync(DraftStrategyRecord draftStrategy, CancellationToken cancellationToken)
        {
            InsertedStrategies.Add(draftStrategy);
            return Task.FromResult((long)InsertedStrategies.Count);
        }

        public Task<long> InsertSkippedAsync(SkippedEmailRecord skippedEmail, CancellationToken cancellationToken)
        {
            InsertedSkips.Add(skippedEmail);
            return Task.FromResult((long)InsertedSkips.Count);
        }

        public Task<IReadOnlyList<DraftSetSummary>> GetDraftSetsAsync(string? status, int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DraftSetSummary>>([]);

        public Task<DraftSetDetail?> GetDraftSetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<DraftSetDetail?>(null);

        public Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SkippedEmailRecord>> GetSkippedEmailsAsync(int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SkippedEmailRecord>>([]);

        public Task<IReadOnlyList<RunRecordListItem>> GetRunRecordsAsync(int skip, int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunRecordListItem>>([]);

        public Task UpdateDraftSetStatusAsync(long id, string status, long? selectedVariantId, DateTimeOffset? reviewedAt, DateTimeOffset? pushedAt, bool clearSelectedVariantId, bool clearReviewedAt, bool clearPushedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
