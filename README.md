# Mailbox Replier

Email reply copilot for Outlook/Hotmail mailboxes.

Current capabilities:
- read unread email over IMAP using Microsoft OAuth
- classify mail into skip vs draft
- learn reply style from sent mail
- generate one or more draft reply options
- persist drafts, skips, run records, and style profiles in SQLite

## How It Flows

The top-level runtime path is:

1. `Program.cs`
   - wires dependencies
   - loads config
   - starts `RunOnceWorker` when `Worker:Enabled=true`

2. `RunOnceWorker`
   - validates config
   - initializes SQLite stores
   - loads already-processed IMAP UIDs
   - runs `ScanInboxUseCase`
   - persists a `RunRecord`

3. `ScanInboxUseCase`
   - loads a window of unread candidate emails
   - runs classification
   - if classification says skip:
     - stores a `SkippedEmailRecord`
   - if classification says reply:
     - checks reply scope
     - if out of scope:
       - stores a draft strategy plus skip record
     - if in scope:
       - selects a style profile
       - runs draft eligibility assessment
       - if ineligible:
         - stores a draft strategy plus skip record
       - if eligible:
         - plans reply shapes
         - generates draft variant(s)
         - stores a `DraftSetRecord` with `DraftVariantRecord` children

## Decision Order

The current decision path is:

1. `ClassificationPipeline`
   - `BuiltInExclusionStage`
   - `ConfiguredExclusionStage`
   - if neither stage definitively skips, result is `DEFAULT_REPLY`

2. `ReplyScopeEvaluator`
   - optional safety gate
   - can restrict drafting to configured sender domains
   - if a message is outside scope, it is recorded as `DRAFT_INELIGIBLE`

3. `DraftEligibilityAssessor`
   - decides:
     - `INELIGIBLE`
     - `SINGLE_DRAFT`
     - `VARIANT_CANDIDATE`

4. `ReplyShapePlanner`
   - plans the reply shape(s), for example:
     - `DIRECT_ANSWER`
     - `ACKNOWLEDGE`
     - `ACKNOWLEDGE_AND_ASK`
     - `CONFIRM_AND_CLOSE`
     - `CONFIRM_AND_REQUEST`
     - `DECLINE`

5. `LlmDraftGenerator`
   - generates the draft text using the chosen style profile and reply shape

## Folder Guide

### `src/EmailCopilot.Worker/Models`

These are the core data shapes used by the app.

- `Models/Classification`
  - classification results, reason codes, decision trace
- `Models/Drafting`
  - draft sets, draft variants, reply shapes, draft strategy
- `Models/Email`
  - incoming email, sender identity, skipped email record, sent sample
- `Models/Runtime`
  - run summary models
- `Models/Style`
  - learned style profile models

### `src/EmailCopilot.Worker/Services`

These are the behaviors of the app, grouped by concern.

- `Services/Classification`
  - the skip/no-skip pipeline
- `Services/Classification/Rules`
  - built-in exclusion heuristics
- `Services/Classification/Stages`
  - pipeline stage contracts and stage results
- `Services/Decisioning`
  - post-classification decision logic
  - example:
    - draft eligibility
    - reply shape planning
- `Services/Drafting`
  - greeting logic and LLM prompt/draft generation
- `Services/Email`
  - IMAP reading and Microsoft OAuth integration
- `Services/Infrastructure`
  - persistence and external storage/config support
- `Services/Runtime`
  - top-level orchestration for one worker run
- `Services/Style`
  - style extraction, contamination filtering, and profile selection

## What `Infrastructure` Means Here

`Infrastructure` is the part of the codebase that talks to storage or supports the app operationally.

In this repo that mainly means:
- SQLite stores
  - `SqliteDraftStore`
  - `SqliteRunRecordStore`
  - `SqliteStyleProfileStore`
- store interfaces
  - `IDraftStore`
  - `IRunRecordStore`
- config/rules validation
  - `Phase1ConfigurationValidator`
  - `ExclusionRulesValidator`

What it is **not**:
- business/product decision logic
- classification rules
- reply-shape planning
- LLM prompting logic

So a simple rule of thumb is:
- if it decides product behavior, it probably does **not** belong in `Infrastructure`
- if it stores, loads, validates, or integrates with an external system, it probably **does**

## Where To Start Reading

If you want the fastest path through the system:

1. [Program.cs](src/EmailCopilot.Worker/Program.cs)
2. [RunOnceWorker.cs](src/EmailCopilot.Worker/Services/Runtime/RunOnceWorker.cs)
3. [ScanInboxUseCase.cs](src/EmailCopilot.Worker/Services/Runtime/ScanInboxUseCase.cs)
4. [ClassificationPipeline.cs](src/EmailCopilot.Worker/Services/Classification/ClassificationPipeline.cs)
5. [DraftEligibilityAssessor.cs](src/EmailCopilot.Worker/Services/Decisioning/DraftEligibilityAssessor.cs)
6. [ReplyShapePlanner.cs](src/EmailCopilot.Worker/Services/Decisioning/ReplyShapePlanner.cs)
7. [LlmDraftGenerator.cs](src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs)
8. [SqliteDraftStore.cs](src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs)

## Current Architectural Note

The project is currently in the middle of phase-2 evolution.

The major direction is:
- keep stage 1 responsible for skip/no-skip
- keep stage 2 responsible for draft eligibility
- keep stage 3 responsible for planning reply shape(s)

That boundary is still being refined through live-run feedback loops and regression tests.

## Tests

Tests live under `tests/EmailCopilot.Worker.Tests` and are grouped by concern:

- `Classification`
- `Drafting`
- `Infrastructure`
- `Runtime`
- `Style`

Run them with:

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false
```

## Secrets

Do not keep live secrets in `appsettings.json`.

This project already has a `.NET` `UserSecretsId`, so local secrets can be stored with user secrets instead of checked-in config.

Set the OpenAI API key locally with:

```powershell
dotnet user-secrets set "Llm:ApiKey" "YOUR_NEW_KEY" --project src/EmailCopilot.Worker/EmailCopilot.Worker.csproj
```

You can also override config with environment variables, for example:

```powershell
$env:Llm__ApiKey="YOUR_NEW_KEY"
```

Checked-in config should keep placeholder values like `replace-me`.

## Review UI Mode

Phase 3 introduces a local review dashboard served by the worker host at:

```text
http://127.0.0.1:5000/review
```

There are now two useful local modes:

1. Normal run
   - scans mail
   - classifies
   - writes new draft sets
   - keeps the web UI alive when a draft is created

2. Review-only mode
   - does not start `RunOnceWorker`
   - serves the existing draft/run/skip data already persisted in SQLite
   - useful when IMAP/OAuth is flaky but you still want to review pending drafts

Review-only mode can be started from the `EmailCopilot.Worker.ReviewOnly` launch profile, or by setting:

```json
"Worker": {
  "Enabled": false
}
```

while leaving:

```json
"WebUi": {
  "Enabled": true
}
```
