# Favored Phrases v1 — Positive Per-Shape Phrase Bank

**Status:** Codex-approved with conditions (see §10) — ready to implement on user go-ahead
**Companion to:** [AVOID_PHRASES_V1.md](AVOID_PHRASES_V1.md) (already shipped)

---

## 1. Problem

Live drafts still feel generic on the *positive* axis. AvoidPhrases v1 kills corporate stock phrases. It does **not** push the model toward the user's actual phrasings.

Concrete observation from the most recent A/B run (UID 228174, before and after the voice-prompt rewrite):

- ACKNOWLEDGE went from `"Hi Hayley, I'll check on the meter readings and get back to you."` → `"Got it. I'll check on the readings and get back to you."`. The `"Got it"` opener is a *real* user phrase; the model only used it because it happened to surface in style examples.
- DIRECT_ANSWER drafts still say `"I can get the readings… I'll send them over shortly."` — fluent but not distinctly *this* user. The user's actual replies use shorter, more specific patterns: `"send tomorrow morning"`, `"Will sort it"`, `"yes can do"`.

What's missing: a **concrete, persisted per-shape bank** of phrases the user actually writes, surfaced into the prompt the same way `StaticAvoidPhrases` is surfaced.

`StyleProfile.CommonPhrases` exists today but is:
- **Global** — no awareness of which reply shape a phrase belongs to.
- **Top-5 globally** — gets dominated by whichever shape the user replies with most often; rare-shape phrasings never make the list.
- **Not surfaced in the prompt** — `LlmDraftGenerator` does not currently read it (only voice signals + adaptive guidance + AvoidPhrases).

---

## 2. Goal (in scope)

Add a positive per-shape phrase bank, persisted on `StyleProfile`, surfaced in the LLM prompt as a sibling block to voice signals and AvoidPhrases.

**In scope:**
1. New `FavoredPhrasesByShape` field on `StyleProfile` (additive, nullable).
2. Per-shape extraction in `StyleExtractor` using line-position + surface-form heuristics (no per-sample shape classification).
3. Additive nullable SQLite column `FavoredPhrasesJson` (same migration pattern as `AvoidPhrasesJson`).
4. New `BuildFavoredPhrasesSection` block in `LlmDraftGenerator.BuildPrompt`, inserted between voice signals and AvoidPhrases.
5. Cap: ≤3 phrases per shape, ≤7 shapes = ≤21 phrases max (token budget). Mirrors AvoidPhrases cap.
6. Tests at every layer.

**Explicitly out of scope:**
- Embedding-based similarity (no model-side ML).
- Per-sample ReplyShape classifier (out — would add a heavy new pipeline stage).
- User-editable phrase bank UI (deferred — same path AvoidPhrases is on).
- Replacing `CommonPhrases` — keep it for backward compat and analysis; new field is additive.

---

## 3. Design — Extraction

Extraction runs inside `StyleExtractor.BuildProfile` on the same authored-body output already used for `CommonPhrases`. **No new LLM calls. No new I/O.**

### 3.1 Bucketing heuristic

For each sample's content lines (after greeting + signoff stripped):

1. **First non-empty content line** → candidate `Opener`
2. **Last non-empty content line** → candidate `Closer`
3. All other lines → candidate `Middle`

A phrase becomes a **shape candidate** by matching surface-form cues. Each cue maps to a single shape; ambiguous candidates are dropped:

| Surface cue (regex, case-insensitive)                                              | Shape                |
| ----------------------------------------------------------------------------------- | -------------------- |
| `^(got it|cool|sure|ok|okay|noted|thanks|cheers)\b`                                 | ACKNOWLEDGE          |
| `\?\s*$` AND `^(got it|cool|sure|ok|okay|noted|thanks)` (combined)                  | ACKNOWLEDGE_AND_ASK  |
| `^(yes|yep|yeah|can do|will do|i'll|i will|sounds good|sounds fine|happy to)\b`     | DIRECT_ANSWER        |
| `\?\s*$` only (interrogative ending, no acknowledgement opener)                     | DIRECT_ANSWER\*      |
| `^(no|sorry|i can't|i cant|won't|wont|unfortunately|not able|can't make)\b`         | DECLINE              |
| `\b(confirmed|confirm|all set|booked|sorted|sorted it|done)\b` AND NOT `\?\s*$`     | CONFIRM_AND_CLOSE    |
| `\b(can you|could you|would you|please send|please confirm|let me know)\b`          | CONFIRM_AND_REQUEST  |
| (no cue matched)                                                                    | GeneralReply         |

\* Interrogative-without-acknowledgement is ambiguous — Codex to advise: bucket as DIRECT_ANSWER, or drop?

### 3.2 Filtering (re-uses existing)

A candidate phrase is only emitted if it already passes the **existing `ExtractPhraseCandidates` filters** in `StyleExtractor` (length 18–120 chars, 4–14 words, not header/quoted/metadata/greeting/closing, `StyleSentenceFilterPipeline.ShouldKeep`).

**Additional filter for FavoredPhrases (Codex-simplified):**
- Must not start with a name token (regex: `^[A-Z][a-z]+,`) — prevents `"Hayley, that works for me"` leaking through.
- ~~Second-person name list filter~~ — dropped on Codex advice (fragile, requires maintained name list; the leading-capital-comma rule catches the dominant case).

### 3.3 Counting and selection

Per shape:
- Count occurrences using the existing `NormalizePhrase` key.
- Require `Count >= 2` for shapes with `SampleSize >= 10`. Allow `Count >= 1` for shapes with `SampleSize < 10` (rare-shape leniency).
- Top-3 by count desc, then original-string alphabetic as tie-break (matches existing `CommonPhrases` ordering).
- Emit original (un-normalized) string, same as `CommonPhrases` today.

### 3.4 Resolved extraction decisions (Codex)

- **Q1 — RESOLVED: DROP.** Interrogative-only phrases are dropped entirely. ACKNOWLEDGE_AND_ASK still captures phrases via the combined cue. Test case: `"When works for you?"` → not bucketed.
- **Q2 — RESOLVED: flat list for v1.** Single-list per shape; no `Opener/Closer/Middle` split. Position heuristic is used internally only as a filter.
- **Q3 — RESOLVED: defensive call required.** Add an explicit `!StyleProfileService.LooksLikePromotional(trimmed)` gate inside `ExtractPhraseCandidates` after `StyleSentenceFilterPipeline.ShouldKeep` — belt-and-braces. The existing `LooksLikePromotional` is already public on `StyleProfileService` (covered by `StyleProfilePromotionalFilterTests`).

---

## 4. Design — Persistence

### 4.1 Schema

Additive nullable column on `StyleProfileSegments`:

```sql
ALTER TABLE StyleProfileSegments ADD COLUMN FavoredPhrasesJson TEXT NULL;
```

Migration pattern matches AvoidPhrases — see [SqliteStyleProfileStore.cs:62](src/EmailCopilot.Worker/Services/Infrastructure/SqliteStyleProfileStore.cs#L62).

### 4.2 JSON blob shape

```json
{
  "phrases": {
    "ACKNOWLEDGE": ["Got it, will do.", "Cool, thanks."],
    "DIRECT_ANSWER": ["Yes can do tomorrow.", "I can send it over shortly."],
    "DECLINE": ["No sorry, won't make that one."]
  },
  "updatedAtUtc": "2026-05-15T20:00:00Z"
}
```

Shapes with zero phrases are omitted from the dict (not present as empty arrays). Empty/null `phrases` means "no learned favored phrases" → prompt falls back gracefully.

### 4.3 `StyleProfile` field

```csharp
public sealed record StyleProfile(
    /* existing fields */,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? AvoidPhrases = null,
    DateTimeOffset? AvoidPhrasesUpdatedAtUtc = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? FavoredPhrases = null,
    DateTimeOffset? FavoredPhrasesUpdatedAtUtc = null);
```

Keys = ReplyShape enum-name strings (`"ACKNOWLEDGE"`, etc.) — same convention as `AvoidPhrases`.

---

## 5. Design — Prompt wiring

New helper in `LlmDraftGenerator`, called from `BuildPrompt` between voice signals and AvoidPhrases.

```csharp
private static string BuildFavoredPhrasesSection(
    string replyShape,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? favored)
{
    if (favored is null || !favored.TryGetValue(replyShape, out var phrases) || phrases.Count == 0)
    {
        return string.Empty;
    }

    var capped = phrases.Take(3).ToArray();
    var lines = string.Join(Environment.NewLine, capped.Select(p => $"- \"{p}\""));

    return $"""
Phrases the user actually writes when replying in this shape — use one only if it fits naturally, do not force:
{lines}

""";
}
```

**Position in prompt** (between voice signals and avoid phrases — already a sibling-block area):
```
adaptiveBlock
voiceBlock
favoredPhrasesBlock   ← NEW
exampleGuidance
avoidPhrasesSection
analysisSection
asksSection
sender + email body
```

**Framing language deliberately soft** ("use one only if it fits naturally"). The opposite framing — "use these" — risks the model jamming a phrase in where it doesn't belong, which is the failure mode the user has explicitly flagged before.

### 5.1 Resolved prompt-wiring decisions (Codex)

- **Q4 — RESOLVED: always emit when learned phrases exist.** Independent of `selectedExamples`. Symmetric with AvoidPhrases. Soft framing keeps the model from jamming.
- **Q5 — RESOLVED: independent blocks.** No cross-reference to AvoidPhrases in framing. Avoids ambiguity if a phrase ever appears in both lists.

---

## 6. Tests

### 6.1 `StyleExtractor` tests (`tests/.../Style/StyleFavoredPhraseExtractionTests.cs`)

1. `Bucket_acknowledge_opener_when_sample_starts_with_got_it`
2. `Bucket_direct_answer_when_sample_starts_with_can_do`
3. `Bucket_decline_when_sample_starts_with_no_sorry`
4. `Bucket_acknowledge_and_ask_when_acknowledge_opener_and_trailing_question`
5. `Skip_phrase_with_recipient_name_prefix` (regression — `"Hayley, that works for me"` must not appear in any shape bucket)
6. `Skip_phrase_below_min_count_when_sample_size_large` (Count<2, SampleSize>=10 → dropped)
7. `Keep_phrase_with_count_one_when_sample_size_small` (rare-shape leniency)
8. `Cap_at_three_per_shape`
9. **(Codex-added)** `Combined_acknowledge_and_ask_cue_takes_precedence_over_plain_acknowledge_opener` — input like `"cool, when works for you?"` matches both the `^cool` ACK opener and the combined ACK_AND_ASK cue; assert it lands in ACKNOWLEDGE_AND_ASK only.
10. **(Codex-added)** `Skip_promotional_phrase_via_LooksLikePromotional_gate` — phrase like `"Check out our latest offer for you"` passes `ShouldKeep` but is dropped by the defensive promotional filter.

### 6.2 `SqliteStyleProfileStore` round-trip test

`tests/.../Infrastructure/SqliteStyleProfileStoreFavoredPhrasesTests.cs`:
- Write profile with FavoredPhrases populated → read back → assert dict structure preserved per-shape.
- Write profile with FavoredPhrases=null → read back → assert null (not empty dict).
- **(Codex condition)** Migration test: create a connection against an existing schema *before* the new column is added; run `InitializeAsync`; assert column added and pre-existing rows read back with `FavoredPhrases == null`. Mirrors the pattern used for AvoidPhrases migration testing.

### 6.3 `LlmDraftGenerator` prompt tests

`tests/.../Drafting/LlmDraftGeneratorTests.cs` — add cases:
- `Prompt_includes_favored_phrases_for_matching_shape`
- `Prompt_omits_favored_phrases_when_shape_has_none`
- `Prompt_omits_favored_phrases_section_when_profile_has_no_favored_phrases`
- `Favored_phrases_capped_at_three_in_prompt`
- `Favored_phrases_framed_as_soft_suggestion_not_directive`

### 6.4 Regression assertion

Run the full suite. Existing 274+ tests must continue to pass.

---

## 7. Rollout

1. Codex review of this doc → resolve open questions.
2. Implement in order: model field → store migration & round-trip → extractor → prompt → tests.
3. On first worker run after deploy, `StyleProfile.RebuildOnStartup=true` already rebuilds profiles → FavoredPhrases populated on next run, no manual migration needed.
4. Spot-check: dump `FavoredPhrasesJson` for the live mailbox after first rebuild and verify the surfaced phrases look like real user voice, not contaminated artefacts.
5. Manual A/B on one of the recent stuck UIDs (228174 or 218770) — reset gate, regenerate, compare voice match.

---

## 8. Risk surface & open questions for Codex

| Risk                                                              | Mitigation                                                                  |
| ----------------------------------------------------------------- | --------------------------------------------------------------------------- |
| Contamination — promotional phrases leak into bank                 | Reuse existing `StyleSentenceFilterPipeline` + `LooksLikePromotional`       |
| Name leakage — `"Hayley, that works"` in bank                      | New name-prefix filter in §3.2                                              |
| Token bloat                                                        | Cap 3/shape × 7 shapes = 21 lines max; soft framing keeps section compact  |
| Model jams a phrase in wrong context                               | Soft "use only if fits naturally" framing; do not list multiple shapes' phrases at once — only the current shape's |
| `CommonPhrases` becomes redundant                                  | Out of scope; keep as-is for backward compat                                |
| Per-shape bucketing miscategorizes (e.g., sarcastic "sure" → ACK)  | Accept — surface forms aren't perfect; 3-phrase cap limits damage          |

All five open questions are resolved — see §3.4 and §5.1.

---

## 9. Files modified summary

| File                                                                                          | Change                                                              |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| `src/EmailCopilot.Worker/Models/Style/StyleProfile.cs`                                          | Add `FavoredPhrases` + `FavoredPhrasesUpdatedAtUtc` fields          |
| `src/EmailCopilot.Worker/Services/Style/StyleExtractor.cs`                                      | Per-shape bucketing; new private `BucketByShape` + `IsShapeOpener` |
| `src/EmailCopilot.Worker/Services/Infrastructure/SqliteStyleProfileStore.cs`                    | Migration + read + write for `FavoredPhrasesJson`                  |
| `src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs`                                | New `BuildFavoredPhrasesSection` + wired into `BuildPrompt`        |
| `tests/EmailCopilot.Worker.Tests/Style/StyleFavoredPhraseExtractionTests.cs`                    | NEW                                                                 |
| `tests/EmailCopilot.Worker.Tests/Infrastructure/SqliteStyleProfileStoreFavoredPhrasesTests.cs`  | NEW                                                                 |
| `tests/EmailCopilot.Worker.Tests/Drafting/LlmDraftGeneratorTests.cs`                            | 5 new cases (see §6.3)                                              |

No config changes. No pipeline-order changes. No prompt-fragment removed; one added.

---

## 10. Codex review — verdict and conditions

**Verdict:** Approve with conditions. Confidence: 78% on clean implementation if conditions are met.

**Conditions folded into the scope above:**

1. ✅ **Q1–Q5 resolved** as documented in §3.4 and §5.1 — drop interrogatives, flat list, defensive promotional filter, always emit, independent blocks.
2. ✅ **Simplified name-leakage filter** (§3.2) — only the leading-capital-comma rule; dropped the maintained name-list rule as fragile.
3. ✅ **Two new test cases** (§6.1 cases 9 and 10) — combined-cue precedence + promotional-gate regression.
4. ✅ **Migration-safety test** added explicitly in §6.2.

**Codex red lines (would force re-review):**
- Changing the soft framing to directive ("use these").
- Adding a per-sample shape classifier to the pipeline (that's a new stage and would need a separate review).
- Collapsing `FavoredPhrases` and `CommonPhrases` into one field.

**Codex residual risk (accepted):**
- Bucketing regex will miscategorize edge cases like `"Sure, I can do that Friday"` (matches `^sure` ACK cue but is actually DIRECT_ANSWER). Acceptable because: 3-phrase cap limits damage, soft framing lets the model ignore wrong-context phrases, and we will tighten regexes after first real-data spot-check.
- `CommonPhrases` redundancy — acceptable, additive. Mark for review in Phase 4 cleanup if not used outside UI.

**Implementation order (Codex-recommended):**
1. `StyleProfile` field
2. Store migration + round-trip
3. Extractor (with defensive promotional filter)
4. Prompt wiring
5. Tests at every layer
