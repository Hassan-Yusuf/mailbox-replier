# CLAUDE.md — Mailbox Replier

This file is the primary context file for Claude Code sessions on this project.
Read it before making any non-trivial change.

---

## 1. Project Goal

Build a general email-reply copilot for Outlook/Hotmail mailboxes:

- Read unread mail over IMAP using Microsoft OAuth
- Decide skip vs draft with a multi-stage pipeline
- Learn the user's reply style from sent mail
- Generate draft replies with an LLM (OpenAI-compatible)
- Persist drafts, skips, strategies, style profiles, and run records in SQLite

This is a product, not a personal script. Rules should model generic classes of mail, not specific domains. Mailbox-specific exceptions belong in user-configurable config, not the product core.

---

## 2. Architecture

### Decision Order (in code today)

```
ClassificationPipeline
  └── BuiltInExclusionStage
  └── ConfiguredExclusionStage
       ↓ (if RequiresReply)
ReplyScopeEvaluator        ← optional domain-allow gate
       ↓ (if allowed)
DraftEligibilityAssessor   → INELIGIBLE | SINGLE_DRAFT | VARIANT_CANDIDATE
       ↓ (if eligible)
ReplyShapePlanner          → ReplyPlan (1–3 ReplyShapeOptions)
       ↓
LlmDraftGenerator          → one draft per chosen shape
       ↓
SqliteDraftStore           → DraftSetRecord + DraftVariantRecord children
                             + DraftStrategyRecord (decision audit)
```

### Key Naming Decisions (do not rename casually)

- Term in code is **reply shape**, not "intent." `ReplyShape`, `ReplyShapeOption`, `ReplyPlan`, `ReplyShapePlanner`.
- If a higher-level "intent" layer is ever added, it must sit *above* shapes, not replace them.
- `DraftSet` → `DraftVariant` is the stable persistence contract. Do not collapse back to a single `DraftReply` row.

### Supported Reply Shapes

`DIRECT_ANSWER` | `ACKNOWLEDGE` | `ACKNOWLEDGE_AND_ASK` | `CONFIRM_AND_CLOSE` | `CONFIRM_AND_REQUEST` | `DECLINE`

### Folder Guide

```
src/EmailCopilot.Worker/
  Models/Classification    classification results, reason codes, trace
  Models/Drafting          DraftSet, DraftVariant, DraftStrategy, ReplyPlan, ReplyShapes
  Models/Email             IncomingEmail, EmailAddress, SkippedEmailRecord, SentSample
  Models/Runtime           run summary models
  Models/Style             style profile models

  Services/Classification        pipeline + stages
  Services/Classification/Rules  built-in exclusion rules (do not casually add hacks here)
  Services/Classification/Stages stage contracts
  Services/Decisioning           DraftEligibilityAssessor, ReplyShapePlanner, ReplyScopeEvaluator
  Services/Drafting              LlmDraftGenerator, GreetingPolicy
  Services/Email                 IMAP reader, Microsoft OAuth
  Services/Infrastructure        SqliteDraftStore, SqliteRunRecordStore, SqliteStyleProfileStore
  Services/Runtime               RunOnceWorker, ScanInboxUseCase
  Services/Style                 style extraction, contamination filter, profile selector

tests/EmailCopilot.Worker.Tests/
  Classification / Drafting / Infrastructure / Runtime / Style
```

---

## 3. Config Concepts

Main config: `src/EmailCopilot.Worker/appsettings.json`
Dev overrides: `src/EmailCopilot.Worker/appsettings.Development.json`

Important sections: `Imap`, `MicrosoftOAuth`, `Database`, `Llm`, `StyleProfile`, `ReplyScope`

```json
"ReplyScope": {
  "Mode": "All",
  "AllowedDomains": []
}
```

Supported `ReplyScope` modes: `All` | `OnlyAllowedDomains`

---

## 4. Commands

**Run tests:**
```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false
```
Use `-p:UseAppHost=false` — this machine hits Windows/Vanguard `.exe` lock issues otherwise.

**Run worker (Development):**
```powershell
$env:DOTNET_ENVIRONMENT='Development'
dotnet src/EmailCopilot.Worker/bin/Debug/net8.0/EmailCopilot.Worker.dll
```

**Inspect SQLite (recent runs + draft sets):**
```powershell
$env:PYTHONIOENCODING='utf-8'
@'
import sqlite3
con = sqlite3.connect(r"c:\\Users\\Owner\\Downloads\\mailbox_replier\\mailbox-replier\\src\\EmailCopilot.Worker\\email-copilot.db")
cur = con.cursor()
for row in cur.execute("select Id, ExitCode, CandidateWindowsScanned, CandidatesEvaluated, SkippedCount, DraftId, DraftSourceImapUid from RunRecords order by Id desc limit 5"):
    print(row)
for row in cur.execute("select Id, SourceImapUid, FromAddress, Subject, IsAmbiguous, Status from DraftSets order by Id desc limit 5"):
    print(row)
con.close()
'@ | python -
```

---

## 5. What Not To Break

These parts are load-bearing and were tuned from real live mailbox runs:

- Built-in classification rules (`Services/Classification/Rules`)
- Stage-1 skip behavior
- Live feedback-loop regressions (78 NUnit tests as of phase-2 checkpoint)
- `DraftSet` / `DraftVariant` / `DraftStrategy` persistence shape

Do not casually collapse:
- a single `DraftReply` row
- one giant classifier method
- mailbox-specific hacks into product classification rules

---

## 6. Phase Status

### Phase 1 — Done

`EmailAddress` value object, async classification, `DecisionTrace`, named rules, classification pipeline, style contamination hardening, `ScanInboxUseCase`, run records, NUnit suite.

### Phase 2 — Structurally done; polish remaining

Done:
- `DraftSet` / `DraftVariant` / `DraftStrategy` persistence
- One-draft and multi-variant paths share the same model
- `ReplyShapePlanner` with shape-aware `LlmDraftGenerator`
- `ReplyScopeEvaluator` gate
- Docs and README

Remaining before calling phase 2 a clean checkpoint:
1. Keep `PHASE2_SCOPE.md` aligned with code naming (code uses "reply shape"; doc still says "intent" in places)
2. Add a thin CLI presenter (`DraftSetConsoleWriter` or similar) so variant sets surface as a user-facing contract, not just `ILogger` lines
3. Add tie-break / minimum-spread logic in `ReplyShapePlanner` so near-duplicate shapes are not emitted
4. Add regression tests: single-draft → 1-variant set, multi-variant → distinct shapes, scope-blocked → `DRAFT_INELIGIBLE`
5. Verify `README.md` decision-order section matches code's actual stage ordering

### Phase 3 — Not started

Built-in rule metadata/policy catalog, user allow rules, richer reply-scope modes, UI-facing decision transparency, team/CRM workflow layers.

---

## 7. Codex Collaboration Protocol

Codex is a senior architect/developer peer on this project. Claude and Codex may agree or disagree — disagreement is useful signal, not a problem.

### When Claude must get Codex review before proceeding

- Any new pipeline stage or change to stage ordering
- Changes to the `DraftSet` / `DraftVariant` / `DraftStrategy` persistence shape
- Changes to the `ReplyShapePlanner` scoring/tie-break logic
- New classification rules that touch built-in (not configured) exclusions
- Any refactor that moves code between `Services/Decisioning`, `Services/Classification`, or `Services/Infrastructure`
- Anything that changes the `LlmDraftGenerator` prompt structure
- Before closing a phase as a checkpoint

### When Claude should get Codex review as a sense-check

- Non-trivial additions to `ScanInboxUseCase` orchestration
- New config sections or changes to existing config shape
- Any change that touches 4+ files at once
- Architecture questions with real trade-offs where a second opinion is worth having

### When Claude can proceed without Codex

- Adding or updating NUnit tests
- Fixing a specific named regression
- Documentation/comment edits
- Small bug fixes that are clearly isolated (single method, no interface changes)

### How to invoke Codex

Use the Agent tool with a brief that includes:
- what changed or is being proposed
- what specifically needs review (correctness, architecture fit, naming, test coverage)
- any disagreements or uncertainties Claude already has
- relevant file paths and line numbers

Summarize Codex's response in the conversation so the user can see both opinions.
If Claude and Codex disagree, surface both positions and let the user decide.

---

## 8. Regression History (classes of mail fixed from live runs)

These were tuned from real mailbox runs — do not regress them:

- Policy / T&C / privacy update mail
- Suspicious / spammy mail
- Workflow acknowledgements
- Signed-document confirmations (DocuSign-type)
- Order / delivery / carrier check-in mail
- Self-service account-state mail
- App-state / ride-state mail (Airbnb/Uber-type)
- Review reminders
- Survey / feedback reminders
- Social / digest notifications
- Job-match / recruitment-broadcast mail

Specific examples used to build the class rules:
TOTUM policy update, Curve/Lloyds "we've got news," MeetDex "You've Got Matches," DocuSign signed-document, Airbnb review reminder, donation feedback survey.

---

## 9. Reading Order (fastest path through the codebase)

1. [Program.cs](src/EmailCopilot.Worker/Program.cs)
2. [RunOnceWorker.cs](src/EmailCopilot.Worker/Services/Runtime/RunOnceWorker.cs)
3. [ScanInboxUseCase.cs](src/EmailCopilot.Worker/Services/Runtime/ScanInboxUseCase.cs)
4. [ClassificationPipeline.cs](src/EmailCopilot.Worker/Services/Classification/ClassificationPipeline.cs)
5. [DraftEligibilityAssessor.cs](src/EmailCopilot.Worker/Services/Decisioning/DraftEligibilityAssessor.cs)
6. [ReplyShapePlanner.cs](src/EmailCopilot.Worker/Services/Decisioning/ReplyShapePlanner.cs)
7. [LlmDraftGenerator.cs](src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs)
8. [SqliteDraftStore.cs](src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs)

---

## 10. Tooling & Context Protocol

Claude must treat the following sources as ordered context layers:

### 1. Local Docs (required for non-trivial work)
Before analysing code or proposing changes, Claude MUST:

- Read relevant `.md` files (e.g. PHASE docs, design notes)
- Treat them as intent/spec, not just commentary
- Flag when code contradicts docs

If docs are not read, reasoning is considered incomplete.

---

### 2. Memory (claude-mem)

Claude SHOULD query memory when:

- Similar bugs or refactors have occurred before
- Architectural decisions may already exist
- The task involves patterns (e.g. batching, pipelines, IMAP behavior)

After resolving a non-trivial issue, Claude SHOULD persist:

- the problem pattern
- the chosen solution
- any rejected alternatives

Memory is the long-term architectural brain of the project.

---

### 3. Graphify (code graph)

Claude MUST use graphify when:

- A change touches multiple services or layers
- Interfaces are involved
- The change spans 3+ files
- There is uncertainty about boundaries or dependencies

Graphify is the source of truth for structural relationships.

---

### 4. Conflict Resolution

If sources disagree:

- Code shows current behavior
- Docs show intended behavior
- Memory shows past decisions

Claude must explicitly call out the conflict and not silently choose one.

---

### 5. Default Order of Operations

For non-trivial changes:

1. Read relevant `.md` docs
2. Query memory (if pattern/history likely exists)
3. Query graphify (if structural impact exists)
4. Read code
5. Propose change

Skipping steps must be justified.