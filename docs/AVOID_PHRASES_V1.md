# AvoidPhrases v1 — Negative Style Constraints

Locked design spec, agreed with Codex (mandatory-review per CLAUDE.md §7: prompt-structure change).
v1 is intentionally narrow. v2 items are listed at the bottom; do not pull them forward without re-review.

---

## Goal

Suppress generic LLM phrasing the user demonstrably does not use, without hand-tuning the prompt for every regression. v1 ships **static phrase lists per `ReplyShape`** plus a **read-only post-generation coverage check**. No live extraction, no regeneration, no embedding cache.

---

## Scope (in)

1. Static, hand-curated avoid phrases per `ReplyShape`
2. Feature flag (`AvoidPhrasesMode` enum) so the section can be turned off cleanly
3. Schema + persistence shape ready for v2 extraction without another migration
4. Read-only `CoverageVerifier` that warns when a draft fails to address a required ask

## Scope (out — v2)

- Counterfactual+diff extraction from `SentSample` corpus
- Embedding cache, `EffectiveSampleScore` weighting
- `AvoidPhraseEntry` rich record (origin, support, confidence)
- `DraftRefinementPipeline` regeneration loop
- Frontend regen button / phrase management UI

---

## Schema

### `StyleProfile` (Models/Style/StyleProfile.cs)

Add two nullable fields:

```csharp
public IReadOnlyDictionary<ReplyShape, IReadOnlyList<string>>? AvoidPhrases { get; init; }
public DateTimeOffset? AvoidPhrasesUpdatedAtUtc { get; init; }
```

Both nullable so existing profiles are valid; absence ≡ "no avoid phrases known".

### Persistence (Services/Infrastructure/SqliteStyleProfileStore.cs)

Additive nullable column following the existing `EnsureColumnExistsAsync` migration pattern:

```sql
AvoidPhrasesJson TEXT NULL
```

Serialize `AvoidPhrases` + `AvoidPhrasesUpdatedAtUtc` as one JSON blob. Read-back path tolerates NULL.

---

## Static phrase source

New file: `src/EmailCopilot.Worker/Services/Style/StaticAvoidPhrases.cs`

Hand-curated, ~5 phrases per `ReplyShape`. Examples (illustrative — finalize during implementation):

| ReplyShape | Sample avoid phrases |
|---|---|
| `DIRECT_ANSWER` | "I hope this email finds you well", "Please don't hesitate to reach out" |
| `ACKNOWLEDGE` | "Thank you so much for your email", "I really appreciate you reaching out" |
| `ACKNOWLEDGE_AND_ASK` | "Just to clarify a few things", "I had a couple of quick questions" |
| `CONFIRM_AND_CLOSE` | "Looking forward to hearing from you", "Thanks again for everything" |
| `CONFIRM_AND_REQUEST` | "If you could kindly", "At your earliest convenience" |
| `DECLINE` | "Unfortunately at this time", "I regret to inform you" |

Used as the source for `StyleProfile.AvoidPhrases` when `AvoidPhrasesMode = StaticFallback` and the profile has no learned phrases.

---

## Feature flag

`Models/Configuration/LlmOptions.cs`:

```csharp
public AvoidPhrasesMode AvoidPhrasesMode { get; init; } = AvoidPhrasesMode.StaticFallback;

public enum AvoidPhrasesMode
{
    Disabled,         // no avoid section in prompt
    StaticFallback,   // v1 default — hand-curated list per shape
    WithExtraction    // v2 — learned phrases override static
}
```

Default ships as `StaticFallback`. `Disabled` is the kill switch.

---

## Prompt wiring

**File:** `src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs`
**Insertion point:** after the `analysisSection` block (currently ~lines 227-237), before the sender metadata section.

Build a section only when `AvoidPhrasesMode != Disabled`, the resolved shape has phrases, and the list is non-empty after filtering. Cap at **7 phrases** to keep prompt size bounded — pick deterministically (top 7 in source order so prompts are stable across runs).

Prompt fragment shape:

```
The user's writing style avoids phrases like:
- "<phrase 1>"
- "<phrase 2>"
...
Avoid these and equivalent generic constructions. Match the user's voice from the style examples above.
```

Hard requirement: section is only present for the *selected* shape of the variant being generated, not for every shape.

---

## CoverageVerifier (read-only)

**New namespace:** `src/EmailCopilot.Worker/Services/Verification/`
**New file:** `CoverageVerifier.cs`

Behavior:
1. After draft generation, embed `EmailRequestAnalysis.Asks` and the draft's sentences using `text-embedding-3-small`.
2. Cosine-similarity match each ask against the closest draft sentence.
3. Threshold bands:
   - `≥0.82` → covered (no warning)
   - `0.72–0.82` → borderline; emit `GENERATION_BORDERLINE_COVERAGE`
   - `<0.72` → emit `GENERATION_MISSING_REQUIRED_RESPONSE`
4. **No regeneration.** Warnings attach to the draft variant record only.
5. Wired in `ScanInboxUseCase` **after** `SqliteDraftStore` persistence — verifier is observability, not gating.

Cost: ~10 embedding calls per draft (asks + sentences). Negligible against the existing chat-completion call.

`DetectDraftIssues()` is **untouched** — coverage verification is a parallel check, not folded into the existing rule-based detector. Codex agreed in Round 2.

---

## Tests

NUnit (tests/EmailCopilot.Worker.Tests/):

- `Style/StaticAvoidPhrasesTests` — every `ReplyShape` has ≥1 phrase; no duplicates within a shape.
- `Drafting/LlmDraftGeneratorAvoidPhrasesTests` — prompt contains avoid section when mode is `StaticFallback`; absent when `Disabled`; capped at 7; only the selected shape's phrases appear.
- `Infrastructure/SqliteStyleProfileStoreAvoidPhrasesTests` — round-trip serialize/deserialize; existing rows without column read as `null`.
- `Verification/CoverageVerifierTests` — three threshold bands produce expected warning codes; missing analysis ⇒ no-op.

Existing 130-test baseline must stay green.

---

## v2 deferred items (do not implement in v1)

- Counterfactual generation (generic LLM reply per shape) + n-gram diff vs. user's `SentSample` reply
- Embedding cache keyed by `(domain, relationship, shape)`
- `AvoidPhraseEntry { Phrase, Origin, SupportCount, Confidence }` rich record
- `EffectiveSampleScore` weighting (recency × relevance)
- `DraftRefinementPipeline` — regenerate when verifier flags missing-coverage
- UI surface for managing extracted phrases / triggering regen

When v2 lands, the schema added in v1 (`AvoidPhrasesJson` column, nullable `AvoidPhrases` dict) holds the new data without migration.

---

## Cost story

- v1: zero new chat-completion calls. ~10 embedding calls per draft for `CoverageVerifier`.
- `text-embedding-3-small` chosen for OpenAI consistency with existing LLM client; embedding cost is negligible vs. the chat call.
