# Claude Visual Studio Handoff

This file is the current project handoff for continuing the email-reply copilot in Claude from Visual Studio or Claude Code.

## 1. Product Goal

Build a general email-reply copilot for Outlook/Hotmail mailboxes that:

- reads unread mail over IMAP using Microsoft OAuth
- decides whether to skip or draft
- learns the user's reply style from sent mail
- generates draft replies with an LLM
- stores drafts, skips, strategies, style profiles, and run records in SQLite

This is intended to become a general product, not a one-off personal script.

Important product principles:

- classification and drafting should be general, not mailbox-specific hacks
- built-in rules should model generic classes of mail, not just specific domains
- mailbox-specific exceptions belong in user-configurable rules/settings
- behavior should be discovered through tests and live feedback loops, not only through bad live drafts

## 2. Original Architecture / Refactor Intent

The original plan was:

### Phase 1

- create a real sender value object (`EmailAddress`)
- make classification async
- add decision tracing (`DecisionTrace`, `RuleEvaluation`)
- extract `GreetingPolicy`
- move built-in classification out of heuristic soup into named rule objects
- add regression tests
- introduce classification stages/pipeline
- harden style contamination handling
- extract the runtime/use-case orchestration from the worker

### Original Phase 2

Before multi-variant drafting, phase 2 meant:

- move from a decent CLI prototype to a cleaner product core
- make preferences/exclusions more user-manageable
- improve transparency and confidence
- keep the system clean enough for a later UI

### Updated Phase 2

Phase 2 was later refined into:

- `DraftSet` / `DraftVariant` persistence
- one draft for clear cases
- 2-3 shape-distinct variants for ambiguous cases
- keep product foundations clean for later UI / team / CRM modes

Important constraint:

- variants must differ by strategy/reply shape, not just paraphrasing

## 3. Current Architecture

Top-level flow:

1. `Program.cs`
2. `RunOnceWorker`
3. `ScanInboxUseCase`

Decision order in code today:

1. `ClassificationPipeline`
   - built-in exclusions
   - configured exclusions
2. `ReplyScopeEvaluator`
   - optional safety gate
   - can restrict drafting to allowed domains
3. `DraftEligibilityAssessor`
   - `INELIGIBLE`
   - `SINGLE_DRAFT`
   - `VARIANT_CANDIDATE`
4. `ReplyShapePlanner`
   - plans reply shapes like:
     - `DIRECT_ANSWER`
     - `ACKNOWLEDGE`
     - `ACKNOWLEDGE_AND_ASK`
     - `CONFIRM_AND_CLOSE`
     - `CONFIRM_AND_REQUEST`
     - `DECLINE`
5. `LlmDraftGenerator`

Persistence:

- `DraftSetRecord`
- `DraftVariantRecord`
- `DraftStrategyRecord`
- `SkippedEmailRecord`
- `RunRecord`
- style profiles in SQLite

Current stable architectural boundary:

- stage 1 owns skip/no-skip
- stage 2 owns whether drafting is eligible
- stage 3 owns how many reply shapes and which shapes to generate

## 4. Folder Structure

Models:

- `src/EmailCopilot.Worker/Models/Classification`
- `src/EmailCopilot.Worker/Models/Drafting`
- `src/EmailCopilot.Worker/Models/Email`
- `src/EmailCopilot.Worker/Models/Runtime`
- `src/EmailCopilot.Worker/Models/Style`

Services:

- `Services/Classification`
- `Services/Classification/Rules`
- `Services/Classification/Stages`
- `Services/Decisioning`
- `Services/Drafting`
- `Services/Email`
- `Services/Infrastructure`
- `Services/Runtime`
- `Services/Style`

Tests:

- `tests/EmailCopilot.Worker.Tests/Classification`
- `tests/EmailCopilot.Worker.Tests/Drafting`
- `tests/EmailCopilot.Worker.Tests/Infrastructure`
- `tests/EmailCopilot.Worker.Tests/Runtime`
- `tests/EmailCopilot.Worker.Tests/Style`

## 5. Major Work Already Completed

### Phase 1 foundations

- `EmailAddress` value object introduced
- greeting logic extracted into `GreetingPolicy`
- `IEmailClassifier` is async
- `DecisionTrace` and `RuleEvaluation` added
- named built-in rule classes added
- classification pipeline introduced
- style contamination moved into explicit pipelines
- orchestration moved into `ScanInboxUseCase`
- persisted run records added
- NUnit suite added and expanded

### Phase 2 foundations

- `DraftSet` / `DraftVariant` persistence implemented
- legacy `DraftReplies` backfilled into `DraftSets` / `DraftVariants`
- `DraftStrategyRecord` added
- reply-shape terminology adopted in code
- reply-shape-aware generation implemented
- optional `ReplyScope` gate added

## 6. Recent Live Feedback-Loop Fixes

This repo has been tuned heavily from real mailbox runs.

Classes of mail that were patched and regression-covered:

- policy / T&C / privacy update mail
- suspicious / spammy mail
- workflow acknowledgements
- signed-document confirmations
- order / delivery / carrier check-in mail
- self-service account-state mail
- app-state / ride-state mail
- review reminders
- survey / feedback reminders
- social / digest notifications
- job-match / recruitment-broadcast mail

Examples of recently fixed live misses:

- TOTUM policy update
- Curve / Lloyds “we’ve got news”
- MeetDex “You’ve Got Matches”
- DocuSign signed-document mail
- Airbnb review reminder
- donation feedback survey

The principle used was:

- prefer generic class-based fixes
- add a regression for the class
- only use mailbox-specific rules for real user preference, not product behavior

## 7. Current Settings / Config Concepts

Main app config file:

- `src/EmailCopilot.Worker/appsettings.json`

Local/development overrides:

- `src/EmailCopilot.Worker/appsettings.Development.json`

Important config sections:

- `Imap`
- `MicrosoftOAuth`
- `Database`
- `Llm`
- `StyleProfile`
- `ReplyScope`

Current `ReplyScope` shape:

```json
"ReplyScope": {
  "Mode": "All",
  "AllowedDomains": []
}
```

Supported modes today:

- `All`
- `OnlyAllowedDomains`

If `OnlyAllowedDomains` is enabled, messages from other domains are recorded as `DRAFT_INELIGIBLE`.

This is intended as an optional safety gate, not a replacement for classification.

## 8. Current Test / Run State

Latest known test status:

- `78` passing NUnit tests

Run command:

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false
```

Build/test uses `UseAppHost=false` because this machine can hit Windows/Vanguard `.exe` lock issues.

Recommended live run command:

```powershell
$env:DOTNET_ENVIRONMENT='Development'
dotnet src/EmailCopilot.Worker/bin/Debug/net8.0/EmailCopilot.Worker.dll
```

SQLite DB path:

- `src/EmailCopilot.Worker/email-copilot.db`

Latest successful live runs observed:

- Run `33`: `CandidateWindowsScanned=3`, `CandidatesEvaluated=295`, `SkippedCount=294`, `DraftId=108`
- Latest good actionable draft was a real staffing email from `sophie@cateringelite.co.uk`

That indicates recent false positives were successfully skipped and the worker moved on to genuine human/work mail.

## 9. Current Phase-2 Status

Phase 2 is largely in place structurally.

What is effectively done:

- DraftSet / DraftVariant / DraftStrategy shape exists
- one-draft and multi-variant paths share the same persistence model
- reply-shape planning exists
- generation is shape-aware
- reply scope exists
- docs and README exist

What still feels not fully “phase-2 closed”:

1. `PHASE2_SCOPE.md` and docs must stay aligned with code as terminology evolves
2. CLI presentation is still mostly logs, not a thin user-facing presentation contract
3. ambiguity / variant spread is still heuristic and lightly tuned
4. runtime tests could still be extended around variant distinctness and shape guarantees

## 10. Latest Claude Opus Checkpoint

An Opus checkpoint review was run after the recent reply-scope and phase-2 work.

High-level result:

- phase 2’s structural goals are effectively in the code
- stage boundaries are good
- single-variant-as-a-set is the right persistence shape
- shape-aware generation is the right direction

Shortest remaining finish list suggested by Opus:

1. keep `PHASE2_SCOPE.md` aligned with actual code/naming
2. add a thin CLI presenter so one-vs-many variants are surfaced more cleanly than logs
3. add tie-break / minimum-spread logic so multi-variant sets do not drift toward near-duplicates
4. ensure runtime tests cover:
   - single-draft -> one variant set
   - multi-variant -> distinct shapes
   - scope-blocked -> `DRAFT_INELIGIBLE`
5. keep `README.md` decision order aligned with code

## 11. Important Naming Decisions

The code now uses:

- `ReplyShape`
- `ReplyShapeOption`
- `ReplyPlan`
- `ReplyShapePlanner`

This replaced older “intent” terminology in the core code path.

Important guidance:

- keep “reply shape” as the current product term in code unless there is a very good reason to change it
- if a future higher-level “intent” concept is introduced, it should sit above reply shapes, not just rename them again

## 12. What Not To Break

These parts are load-bearing and were tuned from real live mailbox failures:

- built-in classification rules
- stage-1 skip behavior
- live feedback-loop regressions
- persistence shape for `DraftSet` / `DraftVariant` / `DraftStrategy`

Do not casually collapse back to:

- a single `DraftReply` row
- one giant classifier method
- mailbox-specific hacks in product rules

## 13. Good Next Steps

If continuing phase 2 before phase 3:

1. add a thin CLI presenter for draft sets / variants
2. strengthen multi-variant distinctness / tie-break logic
3. expand runtime tests around reply-scope and variant guarantees
4. keep live feedback loop going for generic classifier misses

If moving toward later product phases:

1. built-in rule metadata / policy catalog
   - locked defaults vs user-configurable defaults
2. user allow rules in addition to exclusion rules
3. richer reply-scope modes
   - `OnlyKnownDomains`
   - later maybe sender-level allowlists
4. UI-facing decision transparency
5. future team/shared mailbox/CRM workflow layers

## 14. Commands Claude Can Use

Read project docs:

```powershell
Get-Content README.md
Get-Content PHASE2_SCOPE.md
Get-Content CLAUDE_VISUAL_STUDIO_HANDOFF.md
```

Run tests:

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false
```

Run worker:

```powershell
$env:DOTNET_ENVIRONMENT='Development'
dotnet src/EmailCopilot.Worker/bin/Debug/net8.0/EmailCopilot.Worker.dll
```

Inspect latest run records quickly:

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

## 15. Short Human Summary

The project is no longer a shaky prototype.

It now has:

- a real skip pipeline
- a real eligibility layer
- a real reply-shape planning layer
- adaptive style
- draft-set persistence
- run records
- regression tests
- live feedback-loop hardening

Phase 2 is close to a clean checkpoint, but not quite “done forever.”

The remaining work is mostly polish and contract-hardening, not architectural rescue.
