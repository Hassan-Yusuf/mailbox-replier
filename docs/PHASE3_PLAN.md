# Phase 3 Implementation Plan

Single source of truth for Phase 3 backend + frontend work. Anchored to current code (see [PHASE3_OUTLINE.md](../PHASE3_OUTLINE.md) for product intent).

Already done at start of Phase 3:
- Review workflow (frontend `emailcopilot-web/`) — variant inspection, selection, dismissal
- Approve/Dismiss API (`/api/drafts/{id}/approve|dismiss` in `Services/Runtime/ApiEndpoints.cs`)
- Variant feedback columns exist on `DraftVariantRecord`: `WasSelected`, `WasEdited`, `EditedBody`

---

## Scope

Six work areas, ordered by dependency:

1. **AvoidPhrases v1** — locked spec at [AVOID_PHRASES_V1.md](AVOID_PHRASES_V1.md). Independent.
2. **Built-in rule metadata + user toggles** — biggest deliverable. Settings/policy layer.
3. **Confidence aggregate + workflow mode** — DraftSet confidence rollup, `WorkflowMode` config.
4. **Feedback capture polish** — edit-distance calc on approve, dismiss reason capture.
5. **Shared-mailbox foundations** — nullable `AssignedToUserId`, audit-trail table.
6. **Phase 2 carryover** — `ReplyShapePlanner` tie-break, regression tests, doc/README cleanup.

Frontend updates land alongside each backend slice that needs UI.

### Explicitly deferred to Phase 4

- **User-configured allow rules** (whitelist senders/domains for drafting). Outline mentions "later allow rules"; the toggle layer alone is the largest UX surface in Phase 3, so allow-rules ship in a follow-up iteration once the policies frontend is established.

---

## 1. AvoidPhrases v1

Implement [AVOID_PHRASES_V1.md](AVOID_PHRASES_V1.md) as written. Insertion point in `LlmDraftGenerator.BuildPrompt()` is **after `analysisSection` block, lines ~233-241**, before the final template interpolation.

Files:
- `Models/Style/StyleProfile.cs` — add `AvoidPhrases` dict + `AvoidPhrasesUpdatedAtUtc`
- `Services/Infrastructure/SqliteStyleProfileStore.cs` — `EnsureColumnAsync` for `AvoidPhrasesJson TEXT NULL`
- `Services/Style/StaticAvoidPhrases.cs` — NEW, ~5 phrases per `ReplyShape`
- `Models/Configuration/LlmOptions.cs` — `AvoidPhrasesMode` enum
- `Services/Drafting/LlmDraftGenerator.cs` — section build + injection
- `Services/Verification/CoverageVerifier.cs` — NEW namespace
- `Services/Runtime/ScanInboxUseCase.cs` — wire verifier post-`InsertStrategyAsync` (line 251)
- 4 new test classes per spec

Mandatory Codex review: prompt structure change (per CLAUDE.md §7).

---

## 2. Built-in rule metadata + user toggles

### 2a. Metadata on built-in rules

Extend `IExclusionRule` (`Services/Classification/Stages/IExclusionRule.cs`) with metadata:

```csharp
public interface IExclusionRule
{
    string RuleName { get; }
    string ReasonCode { get; }
    string DisplayName { get; }
    string Description { get; }
    RuleCategory Category { get; }
    bool DefaultEnabled { get; }
    bool IsUserConfigurable { get; }
    Task<RuleEvaluation> EvaluateAsync(IncomingEmail email);
}

public enum RuleCategory
{
    Safety,        // locked: NoReplySender, SuspiciousSpam, AutomatedSender
    Hygiene,       // configurable defaults: BulkPromotional, SocialDigest, TransactionalNotification
    Reminders,     // configurable defaults: WorkflowAcknowledgement, CustomerFeedbackSurvey
    Broadcasts     // configurable defaults: BroadcastFormatted, ListOrBroadcast
}
```

`BuiltInClassificationRule` abstract class implements metadata as abstract properties so each of the 14 concrete rules supplies its own.

Rules classified `Safety` have `IsUserConfigurable = false` and cannot be disabled via config.

### 2b. Configurable toggles

New config section in `appsettings.json` after `ReplyScope`:

```json
"BuiltInRuleToggles": {
  "BulkPromotional": true,
  "SocialDigest": true,
  "WorkflowAcknowledgement": true,
  "CustomerFeedbackSurvey": false
}
```

New service `Services/Classification/RuleToggleEvaluator.cs` resolves: rule → effective enabled state. `BuiltInExclusionStage` consults the evaluator before running each rule and skips disabled non-Safety rules.

### 2c. Settings API + frontend

Backend endpoints in `Services/Runtime/ApiEndpoints.cs`:
- `GET /api/policies/rules` → `[{ ruleId, displayName, description, category, defaultEnabled, isUserConfigurable, currentlyEnabled }]`
- `POST /api/policies/rules/{ruleId}` body `{ enabled: bool }` — persist override

Toggle persistence: extend `appsettings.Development.json` write-through OR new `Services/Infrastructure/SqliteRuleToggleStore.cs` (preferred — config files shouldn't be hot-mutated). Decision in Codex review.

Frontend: new `emailcopilot-web/src/PoliciesPage.tsx` — list rules grouped by `Category`, show toggle for `IsUserConfigurable`, lock icon for Safety. Add nav entry in `Sidebar.tsx`.

Mandatory Codex review: refactor touches built-in classification stage + 4+ files (per §7).

---

## 3. Confidence aggregate + workflow mode

### 3a. DraftSet confidence rollup

Per-variant `ConfidenceScore` exists. Add aggregate to `DraftSetRecord`:

```csharp
public double? AggregateConfidenceScore { get; init; }   // mean of variants
public ConfidenceTier? ConfidenceTier { get; init; }      // Low | Medium | High

public enum ConfidenceTier { Low, Medium, High }
```

Compute in `ScanInboxUseCase` before `_draftStore.SaveDraftSetAsync(...)`. Tier thresholds: `≥0.75 High`, `0.55-0.75 Medium`, `<0.55 Low`. Persist as 2 new columns via `EnsureColumnAsync` (introduce that helper to `SqliteDraftStore.cs` — currently only `SqliteStyleProfileStore` has it).

API: include `aggregateConfidence` + `tier` in `DraftSetSummaryDto` and `DraftSetDto`.

### 3b. Workflow mode config

```json
"Workflow": {
  "Mode": "ReviewBeforeSend"   // SuggestOnly | ReviewBeforeSend
}
```

`SuggestOnly`: **soft-gate** — frontend hides the Approve button and surfaces a "suggest-only" badge. Backend accepts approve calls but logs a warning ("Workflow mode is SuggestOnly; approve accepted but not recommended"). Keeps the API contract stable if a future power-user mode wants to force-approve.
`ReviewBeforeSend` (default): current behavior unchanged.

No `AutoSend` mode — explicitly out of scope per outline §3.

### 3c. Frontend confidence display

`emailcopilot-web/src/DraftQueueItem.tsx` + `DraftDetail.tsx` — render confidence tier badge. Low-confidence drafts get a subtle warning hint.

Mandatory Codex review: persistence shape change (DraftSet new columns).

---

## 4. Feedback capture polish

### 4a. Edit distance on approve

When `/api/drafts/{id}/approve` is called with an `editedBody` field (currently absent), compute Levenshtein distance vs original variant `Body` and persist on the variant.

Schema additions to `DraftVariants` table via `EnsureColumnAsync`:
- `EditDistance INTEGER NULL`
- `EditedAtUtc TEXT NULL`

API change: `ApproveDraftRequest` accepts optional `editedBody`.

### 4b. Dismiss reason

Extend `POST /api/drafts/{id}/dismiss` to accept optional `{ reason: string }`. Persist on `DraftSets`:
- `DismissReason TEXT NULL`
- `DismissedAtUtc TEXT NULL`

Frontend: dismiss button opens a small reason picker with preset reasons + "Other".

### 4c. Audit trail (lightweight, sets up §5)

New table `DraftAuditEvents`. v1 scope: **status-transition events only** (`APPROVED | DISMISSED | PUSHED`). `CREATED` and other internal events deferred — they're worker-internal, not user actions.

```sql
CREATE TABLE IF NOT EXISTS DraftAuditEvents (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  DraftSetId INTEGER NOT NULL,
  EventType TEXT NOT NULL,   -- APPROVED | DISMISSED | PUSHED
  EventAtUtc TEXT NOT NULL,
  ActorUserId TEXT NULL,     -- nullable for now, single-user mode
  PayloadJson TEXT NULL,
  FOREIGN KEY (DraftSetId) REFERENCES DraftSets(Id)
);
```

`SqliteDraftStore` writes audit events on each status transition. New endpoint `GET /api/drafts/{id}/audit` returns events for the inspector.

Mandatory Codex review: persistence shape change.

---

## 5. Shared-mailbox foundations

Per outline §6 — stop blocking, don't implement.

Nullable additions only:

`DraftSets`:
- `AssignedToUserId TEXT NULL`
- `OwnerUserId TEXT NULL`

Single-user mode populates both as null. No API surface yet — fields just exist for a future shared-mailbox release.

Status transitions: validate via a small `DraftSetStatusTransitions` static class (e.g., `PENDING → APPROVED` ok, `DISMISSED → APPROVED` rejected). `SqliteDraftStore.UpdateStatusAsync` consults it.

Sense-check Codex review: persistence additions touching DraftSet.

---

## 6. Phase 2 carryover polish

### 6a. ReplyShapePlanner tie-break

Current bug surface: planner can emit two near-duplicate shapes (e.g., `ACKNOWLEDGE` + `ACKNOWLEDGE_AND_ASK`) when scores tie. Add `MinimumSpread` constant (0.10) — drop the lower-ranked shape if the gap to the next-ranked one is below threshold.

File: `Services/Decisioning/ReplyShapePlanner.cs`. Mandatory Codex review (per §7: planner scoring change).

### 6b. Regression tests

Add to `tests/EmailCopilot.Worker.Tests/Drafting/`:
- `single_intent_email_produces_one_variant_set`
- `multi_intent_email_produces_distinct_shapes`
- `out_of_scope_email_records_DRAFT_INELIGIBLE`

### 6c. Doc cleanup

- `PHASE2_SCOPE.md`: replace remaining "intent" wording with "reply shape"
- `README.md`: verify decision-order section matches current `ScanInboxUseCase` flow

---

## File list (approximate count)

Backend new files:
- `Services/Style/StaticAvoidPhrases.cs`
- `Services/Verification/CoverageVerifier.cs`
- `Services/Classification/RuleToggleEvaluator.cs`
- `Services/Infrastructure/SqliteRuleToggleStore.cs` (if Codex agrees on DB-backed toggles)
- `Models/Drafting/ConfidenceTier.cs`
- `Models/Configuration/WorkflowOptions.cs`
- `Models/Drafting/DraftAuditEventRecord.cs`
- `Services/Drafting/DraftSetStatusTransitions.cs`

Backend modified:
- `Models/Style/StyleProfile.cs`
- `Models/Configuration/LlmOptions.cs`
- `Models/Drafting/DraftSetRecord.cs`
- `Models/Drafting/DraftVariantRecord.cs`
- `Services/Drafting/LlmDraftGenerator.cs`
- `Services/Infrastructure/SqliteStyleProfileStore.cs`
- `Services/Infrastructure/SqliteDraftStore.cs`
- `Services/Classification/Stages/IExclusionRule.cs`
- `Services/Classification/Rules/BuiltInClassificationRule.cs`
- All 14 concrete rule classes (metadata properties)
- `Services/Classification/Stages/BuiltInExclusionStage.cs`
- `Services/Decisioning/ReplyShapePlanner.cs`
- `Services/Runtime/ScanInboxUseCase.cs`
- `Services/Runtime/ApiEndpoints.cs`
- `appsettings.json` + `appsettings.Development.json`
- `Program.cs` (DI registrations)

Frontend new:
- `emailcopilot-web/src/PoliciesPage.tsx`

Frontend modified:
- `emailcopilot-web/src/Sidebar.tsx`
- `emailcopilot-web/src/DraftQueueItem.tsx`
- `emailcopilot-web/src/DraftDetail.tsx`
- `emailcopilot-web/src/api.ts`
- `emailcopilot-web/src/App.tsx`

Tests new (~12 classes):
- `Style/StaticAvoidPhrasesTests`
- `Drafting/LlmDraftGeneratorAvoidPhrasesTests`
- `Verification/CoverageVerifierTests`
- `Infrastructure/SqliteStyleProfileStoreAvoidPhrasesTests`
- `Classification/BuiltInRuleMetadataTests`
- `Classification/RuleToggleEvaluatorTests`
- `Runtime/ApiEndpointsPoliciesTests`
- `Drafting/ConfidenceTierTests`
- `Runtime/ApiEndpointsAuditTests`
- `Drafting/ReplyShapePlannerSpreadTests`
- `Drafting/DraftSetRegressionTests`
- `Infrastructure/SqliteDraftStoreAuditTests`

Total: ~37 files modified or created.

---

## Codex review gates (CLAUDE.md §7)

Before implementing, send Codex this plan and get alignment on:

1. **Toggle persistence**: appsettings vs SqliteRuleToggleStore?
2. **Confidence tier thresholds**: 0.55 / 0.75 reasonable, or should they be config?
3. **AssignedToUserId placement**: DraftSet or DraftVariant or new DraftAssignments table?
4. **Workflow mode `SuggestOnly` semantics**: hard-block approve API (409) or just hide UI button?
5. **Audit table scope v1**: status transitions only, or also CREATED/PUSHED?
6. **EnsureColumnAsync hoist**: extract to a shared `SqliteMigrationHelpers` module since both stores will use it?

Plus mid-implementation reviews for:
- Built-in rule refactor (after metadata interface lands, before touching all 14 rules)
- DraftSet schema additions (before frontend integration)
- LlmDraftGenerator AvoidPhrases prompt section (before merging)
- ReplyShapePlanner spread logic (before merging)

---

## Implementation order

Bottom-up by dependency, frontend follows backend slice. EnsureColumnAsync hoist + SchemaVersion land before any schema work so subsequent migrations are idempotent and crash-safe.

1. **AvoidPhrases v1** (independent, locked spec)
2. **`SqliteMigrationHelpers` + `SchemaVersion` table** (enabler — extracts `EnsureColumnAsync`, adds version tracking so a half-applied migration on crash can be detected)
3. **Rule metadata + Safety/Hygiene categories** (no toggle behavior yet)
4. **`SqliteRuleToggleStore` + `RuleToggleEvaluator` + `BuiltInExclusionStage` hookup**
5. **Policies API + frontend `PoliciesPage`**
6. **Confidence aggregate + tier** (backend only first)
7. **Frontend confidence badges**
8. **Workflow mode config + `SuggestOnly` soft-gate** (frontend hide + backend warn)
9. **Edit distance + dismiss reason** (backend + frontend)
10. **Audit table + `GET /api/drafts/{id}/audit` endpoint** (backend + minimal inspector tab)
11. **Shared-mailbox nullable columns + status-transition validation**
12. **`ReplyShapePlanner` spread + regression tests**
13. **Doc cleanup (`PHASE2_SCOPE.md`, `README.md`)**

Stop and re-Codex after step 5 (settings layer is the largest user-visible change).

---

## Definition of done

Phase 3 is complete (per outline §"Definition of Done") when:
- Users can manage reply policy via the Policies page without touching code
- Variant review/select/dismiss is captured with edit distance + dismiss reasons
- Confidence is visible per draft set
- Built-in rules carry metadata and Safety vs configurable distinction is enforced
- DraftSet schema is ready for shared-mailbox layer (no further migration needed)
- All existing tests stay green; ~12 new test classes added
- Frontend has a Settings/Policies page and confidence display
