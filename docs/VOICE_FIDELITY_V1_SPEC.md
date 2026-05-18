# Spec: Voice Fidelity v1 — Embedding-Based Avoid Filter + Discourse-Marker Signals

**Status:** Codex-reviewed; ready to implement. **Sequencing: S3 (Part A) ships before S1 (Part B)** per Codex's borrow-doc review — shipping markers first risks the model learning to better paraphrase the avoid list.
**Scope:** Two independent additions to the draft-generation pipeline.

---

## Context

After persona-framing and synonym-aware static avoid phrases (sets 52-54, May 2026), the model still produces drafts that read AI-ish. Hand-curated avoid lists are whack-a-mole — every new synonym slips through. The user's actual voice signal in the prompt is also thin: we send rate-band facts (greeting %, contraction %) but not the **discourse markers** ("anyway", "tbh", "yeah", "right?") that authorship-attribution literature treats as the strongest authorial fingerprint.

This spec adds two orthogonal mechanisms:

- **Part A — Embedding-based avoid filter**: post-generation reject any draft sentence whose embedding is within a cosine threshold of a known AIism centroid. Closes the synonym-evasion class permanently.
- **Part B — Discourse-marker signals**: extract observed marker usage from the user's sent corpus and inject as concrete prompt facts. Lifts voice fidelity by giving the model real markers to imitate.

Files touched by this spec are listed in [§4](#4--files-modified).

---

## 1 — Part A: Embedding-Based Avoid Filter

### Failure mode this fixes

Static-list whack-a-mole. Today:
- `"I'll check my schedule and get back to you"` is blocked. Model writes `"I'll check my availability and get back to you"` (sets 48/50/52/54).
- `"Please let me know if that works"` is blocked. Model writes `"Let me know if that works"` (set 52 V0).

Adding literal variants to the static list scales linearly with evasion creativity. Embedding similarity catches the *class*.

### Architecture

```
LlmDraftGenerator
  └── generate draft (existing)
  └── new: AvoidPhraseEmbeddingFilter.EvaluateAsync(draftBody)
       ↓ for each sentence in draft:
       ↓   embed sentence → cosine against each AIism centroid
       ↓   if cosine >= 0.82 with any centroid: SOFT_REJECT
       ↓ if rejected: regenerate with explicit "your previous draft contained '<sentence>'
                       which is a known AIism — rewrite this sentence in the user's voice" instruction
       ↓ retry once. If second attempt also rejected: emit anyway but mark DraftStrategy with avoid_filter_bypassed = true
```

### Components

**`Services/Style/AvoidPhraseEmbeddings.cs`** (new)
- Static list of canonical AIism centroids — one entry per *family*, not per literal phrase:
  - "Schedule-check stall" family: seed = `"I'll check my schedule and get back to you"`
  - "Polite-confirmation closer" family: seed = `"Please let me know if that works for you"`
  - "Corporate clarification request" family: seed = `"Could you please provide more details about this"`
  - "Generic opener" family: seed = `"I hope this email finds you well"`
  - "Generic warm closer" family: seed = `"Looking forward to hearing from you"`
- Each entry: `(FamilyId, SeedText, ApplicableShapes[], EmbeddingVector)`. Embeddings computed once at startup (or cached to disk at `style-cache/avoid-embeddings.json` keyed by seed-text hash).

**`Services/Style/AvoidPhraseEmbeddingFilter.cs`** (new)
- Constructor injection: `IEmbeddingClient`, `AvoidPhraseEmbeddings` registry, `IOptions<AvoidFilterOptions>`.
- `Task<AvoidFilterResult> EvaluateAsync(string draftBody, string replyShape, CancellationToken ct)`
  - Sentence-split (existing `SentenceSplitter` helper if available, else `Regex.Split` on `[\.\?\!]+`).
  - For each sentence ≥ 4 words: embed, compare against shape-applicable centroids.
  - Return `{ Status: Clean | Rejected, OffendingSentence?, MatchedFamily?, Cosine? }`.

**`Services/Drafting/IEmbeddingClient.cs` + `OpenAIEmbeddingClient.cs`** (new)
- Thin wrapper over the existing OpenAI-compatible HTTP client (reuse the same base URL + key from `LlmOptions`).
- Default model: `text-embedding-3-small` (1536-dim, ~$0.02/1M tokens — cheap).
- `Task<float[]> EmbedAsync(string text, CancellationToken ct)`. Cosine helper inline.

**`Services/Drafting/LlmDraftGenerator.cs`** (modify)
- After existing `GenerateDraftAsync` produces a body, call `AvoidPhraseEmbeddingFilter.EvaluateAsync`.
- If `Rejected`: prepend a `"Your previous attempt said: '<offending>'. That phrase is a known AIism (family: <id>). Rewrite that sentence in the user's voice — direct, natural, no template."` line above the existing prompt and regenerate **once**. Single retry only — guards against infinite loops.
- Surface the filter outcome in the `DraftStrategy` audit record (new field: `AvoidFilterMatchedFamily TEXT NULL`).

### Config

```json
"Llm": {
  "AvoidFilter": {
    "Enabled": false,
    "CosineThreshold": 0.82,
    "EmbeddingModel": "text-embedding-3-small",
    "MaxRetries": 1
  }
}
```

**Disabled by default** per Codex — enabled-by-default risks silent rejection without UI visibility. Operator opts in via `appsettings.Development.json` or `appsettings.Production.json`.

### Threshold calibration — delta-vector approach (Codex addition)

0.82 is the *starting threshold*, but the actual mechanic is **delta-vector calibration**, not a universal cosine.

The naive approach treats AIism centroids as universal, but OpenAI's RLHF-tuned models cluster phrases like `"I'll check my schedule and get back to you"` at specific embedding locations that may overlap with the user's own legitimately-formal writing. Subtracting the user's "templated-but-legitimate" centroid from each AIism centroid gives a vector that's *both* model-aware and user-aware.

Calibration process:
1. Compute embedding for each of the ~30 known AIism literals across all shapes → seed centroids per family.
2. From the user's `StyleExamples`, identify the most "templated-looking" sentences (heuristic: no discourse markers, all-passive-voice, >25 words). Compute their centroid → `userTemplatedCentroid`.
3. For each AIism family: `deltaCentroid[i] = aiismCentroid[i] - userTemplatedCentroid`. Store the delta vector, not the raw centroid.
4. At filter time: cosine similarity is computed against `deltaCentroid[i]`, not the raw AIism centroid. This catches "this sentence is in the AIism cluster *and not* in the user's natural register."
5. Set threshold = max(user-sample-cosine-to-delta-centroid) + 0.02 buffer.
6. Save the calibration script to `scripts/calibrate-avoid-threshold.csx` for future re-runs.

If calibration shows a user-sample is still close to any delta-centroid (would create false rejects), drop that centroid or split the family.

### Anti-centroid rule (Codex addition — S4-lite insurance)

One line of code with massive leverage. **If a discourse marker observed for the user (from Part B) appears as a content word inside any AIism centroid's seed text, skip that centroid for this user.**

Example: if the user uses `"yeah"` at rate ≥ 0.05, and the (hypothetical) centroid `"Yeah I'll get back to you"` exists, we skip applying that centroid for this user. They legitimately write that way.

Implementation:
```csharp
private bool ShouldApplyCentroid(AvoidPhraseFamily family, IReadOnlyList<DiscourseMarkerObservation> userMarkers)
{
    foreach (var marker in userMarkers.Where(m => m.Rate >= 0.05))
    {
        if (Regex.IsMatch(family.SeedText, $@"\b{Regex.Escape(marker.Marker)}\b", RegexOptions.IgnoreCase))
        {
            return false; // user legitimately writes this way
        }
    }
    return true;
}
```

This gives ~60% of the S4 feedback-loop signal with zero UI work. Filter is *per-user* without requiring rejection tracking.

### Testing

- `AvoidPhraseEmbeddingFilterTests` — fake `IEmbeddingClient` returns fixed vectors; assert known-AIism sentence is rejected, user-voice sentence is not, threshold edge cases.
- `LlmDraftGeneratorRetryTests` — fake filter rejects first attempt; assert second call uses the rewrite-instruction prompt; assert second-attempt result is what gets persisted.
- One integration test wired to `text-embedding-3-small` (skippable via `[Category("RequiresLlm")]`).

---

## 2 — Part B: Discourse-Marker Signals

### Failure mode this fixes

Voice fidelity. The user writes "yeah, anyway, what time?" and the model writes "Thank you for the information regarding the scheduling. Could you please let me know what time?" — even with persona framing, the model has no concrete signal of *what specific words* the user uses to glue sentences and signal stance.

Authorship-attribution literature (Stamatatos 2009 survey, Burrows's Delta) treats function-word and discourse-marker frequencies as the strongest fingerprint. We currently extract zero discourse markers.

### Architecture

```
StyleExtractor (existing)
  └── new: DiscourseMarkerExtractor.ExtractAsync(sentSamples)
       ↓ scans each sent body for occurrences of the canonical marker list
       ↓ emits rate per marker (occurrences ÷ total samples)
       ↓ filters to markers with rate >= 0.05 (≥5% of replies)
       ↓ caps at top 6 by rate (prompt budget)
StyleProfile (existing)
  └── new column: ObservedDiscourseMarkersJson TEXT NULL
LlmDraftGenerator (existing)
  └── BuildVoiceSignals
       ↓ new line if markers present: "The user often uses: 'yeah', 'anyway', 'tbh', ..."
```

### Components

**`Services/Style/DiscourseMarkers.cs`** (new)
- Canonical *candidate* list (case-insensitive, word-boundary matched):
  - Stance: `tbh`, `honestly`, `i mean`, `i reckon`, `i think`, `i guess`
  - Discourse: `anyway`, `btw`, `fwiw`, `tbf`, `so`, `right`, `well`
  - Acknowledgement: `yeah`, `yep`, `nah`, `ok`, `got it`, `sure`, `cool`
  - Closing: `thanks`, `cheers`, `ta`, `much thanks`
- This list is the *search space* — the actual emitted markers are filtered per-user by observed rate. Mix is British-English leaning given the current user; US/AU equivalents (`yo`, `mate`, `cool beans`) can be added as the user base grows without changing the extractor contract.
- **Adaptation note (Codex addition):** the *emitted* marker set is recomputed per profile rebuild (`StyleProfile:RebuildOnStartup` is already `true`), which means as the user's voice shifts (e.g. they write to a US business partner for 3 months and drop British markers), the per-segment observed marker list re-derives from the freshest corpus on next rebuild. No staleness work needed beyond existing rebuild cadence.

**`Services/Style/DiscourseMarkerExtractor.cs`** (new)
- `IReadOnlyList<DiscourseMarkerObservation> Extract(IReadOnlyList<SentSample> samples)`
- For each marker, count samples that contain it (count by sample, not by occurrence — avoids one chatty email dominating).
- Rate = count ÷ totalSamples. Drop rate < 0.05. Keep top 6 by rate.
- Returns ordered list of `(Marker, Rate)`.

**`Models/Style/StyleProfile.cs`** (modify)
- Add `IReadOnlyList<DiscourseMarkerObservation> ObservedDiscourseMarkers { get; init; } = Array.Empty<DiscourseMarkerObservation>();`
- Default to empty for backwards compat. Existing `StyleProfile` constructor stays valid via init-defaulted property.

**`SqliteStyleProfileStore.cs`** (modify)
- Add column `ObservedDiscourseMarkersJson TEXT NULL` to `StyleProfileSegments`.
- Migration: `ALTER TABLE StyleProfileSegments ADD COLUMN ObservedDiscourseMarkersJson TEXT NULL` (column-add migration pattern already used for `AvoidPhrasesJson`).
- Serialize/deserialize JSON list of `{ "marker": "yeah", "rate": 0.34 }`.

**`Services/Drafting/LlmDraftGenerator.cs`** (modify `BuildVoiceSignals`)
- After existing signals (lowercase-i, trailing-question, emoji, median-word-count), emit:
  - `"- The user often uses: 'yeah', 'anyway', 'tbh', 'cheers' — fold these in where they fit naturally."`
- Only emit when ≥ 2 markers observed (single marker is noise).
- Quoted markers, comma-separated, max 6.

### Testing

- `DiscourseMarkerExtractorTests` — corpus with `"yeah, anyway, what time?"` emits both markers at rate 1.0; corpus without markers emits nothing; rate < 0.05 dropped; cap at 6 respected.
- `LlmDraftGeneratorVoiceSignalsTests` (extend existing) — assert prompt contains `"The user often uses:"` line when markers present; assert it's absent when fewer than 2 markers.
- `SqliteStyleProfileStoreDiscourseMarkersTests` — round-trip persistence + legacy-row defaults to empty list.

---

## 3 — Why this combination

| Aspect | Part A (Embedding Filter) | Part B (Discourse Markers) |
|---|---|---|
| Kills the static-list whack-a-mole | ✅ | — |
| Lifts positive voice fidelity | — | ✅ |
| Depends on the other | No | No |
| External API cost | Yes (embedding call per draft) | No |
| Implementation surface | New service + DI + retry path | Extractor + profile field + signal line |
| Risk of regression | Moderate (retry loop) | Low (additive prompt content) |

They attack the problem from opposite sides: A removes what's wrong, B adds what's right. Implementing both together is more valuable than either alone because the model will *both* avoid the corporate centroid *and* be pulled toward observed user-specific vocabulary.

---

## 4 — Files Modified

| File | Part | Change |
|---|---|---|
| `Services/Style/AvoidPhraseEmbeddings.cs` | A | NEW — centroid registry |
| `Services/Style/AvoidPhraseEmbeddingFilter.cs` | A | NEW — filter service |
| `Services/Drafting/IEmbeddingClient.cs` | A | NEW — interface |
| `Services/Drafting/OpenAIEmbeddingClient.cs` | A | NEW — impl |
| `Services/Drafting/LlmDraftGenerator.cs` | A | MODIFY — wire filter + single retry |
| `Models/Drafting/DraftStrategyRecord.cs` | A | MODIFY — add `AvoidFilterMatchedFamily` |
| `Services/Infrastructure/SqliteDraftStore.cs` | A | MODIFY — persist new column |
| `Options/LlmOptions.cs` | A | MODIFY — add `AvoidFilterOptions` |
| `appsettings.json` | A | MODIFY — defaults |
| `scripts/calibrate-avoid-threshold.csx` | A | NEW — one-time calibration |
| `Services/Style/DiscourseMarkers.cs` | B | NEW — canonical list |
| `Services/Style/DiscourseMarkerExtractor.cs` | B | NEW — extractor |
| `Models/Style/StyleProfile.cs` | B | MODIFY — add observed markers field |
| `Services/Style/StyleExtractor.cs` | B | MODIFY — wire extractor |
| `Services/Infrastructure/SqliteStyleProfileStore.cs` | B | MODIFY — column + migration |
| `Services/Drafting/LlmDraftGenerator.cs` | B | MODIFY — emit signal line in `BuildVoiceSignals` |

Tests: ~10 new test files / extensions across `tests/EmailCopilot.Worker.Tests/Style` and `Drafting`.

---

## 5 — Open Questions (for Codex review)

1. **Threshold 0.82** — is the calibration plan sufficient, or should we ship behind a feature flag and only enable after seeing N consecutive false-reject-free days?
2. **Single retry vs. multi-retry** — bounded at 1 retry to prevent infinite loops, but if the second attempt also fails we emit anyway. Is "emit anyway + flag" the right failure mode, or should we fall back to one of the *other* variants in the set?
3. **Embedding cost** — text-embedding-3-small is ~$0.00002 / 1K tokens. Roughly $0.0001 per draft set assuming 3 variants × ~3 sentences. Acceptable, but worth confirming.
4. **Discourse marker list curation** — British-English leaning is correct for the current user; should the list become per-user via observation only, or stay as a canonical reference list with rate-based filtering?
5. **Cross-cutting**: should Part B's observed markers also feed Part A as *anti-centroids* — i.e. if the user uses "yeah" frequently, "yeah" should never match an AIism centroid? Probably not necessary at threshold 0.82, but worth flagging.

---

## 6 — Out of Scope (deferred to Voice Fidelity v2)

- **Rejection-feedback capture** (Phase 3 backlog item). The strongest signal would be the user's selected/dismissed/edited variants flowing back into the avoid centroid registry as *learned anti-examples*. Requires UI + feedback table changes — separate spec.
- **Per-user LoRA / adapter fine-tuning**. Out of scope until corpus is order-of-magnitude bigger.
- **Subject/sender-conditioned example selection**. Cheap follow-up; can fold into Part B's PR if time permits, but separately specced as v1.5.
