# Static Register-Rule Softening v1

**Status:** Approved with conditions (architectural review complete, 2026-05-16). Implementing.
**Companion to:** [AVOID_PHRASES_V1.md](AVOID_PHRASES_V1.md), [FAVORED_PHRASES_V1.md](FAVORED_PHRASES_V1.md)

### Review conditions folded in
1. Exclamation counting computes **sentence-ending rate** (mirrors `questionEndingRate`), not raw `!` count.
2. Adaptive band wording: `Exclamation marks can be natural for this user.` (replaces `Brief enthusiasm with "!"…` — "enthusiasm" is fuzzy and can mislead).
3. Add test asserting **absence** of the literal `"Register rules - match how this user actually writes"` header from the prompt after deletion.
4. Verification §7 (prompt diff + live A/B) is mandatory pre-merge.
5. SQLite read path must handle NULL legacy rows as 0.0 (tested in `Legacy_row_without_column_reads_as_zero`).

---

## 1. Problem

[LlmDraftGenerator.cs:305-314](../src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs#L305-L314) emits an unconditional **"Register rules"** block that hardcodes one user's voice (the loc8me-style mailbox the system was tuned on):

```
Register rules - match how this user actually writes (these OVERRIDE default AI tone):
- Hard cap: 3 sentences. Aim for 1-2. A single sentence is often correct.
- Do NOT use exclamation marks. The user doesn't.
- Do NOT add a sign-off, "Thanks, [name]", or signature. The user almost never closes.
- Lowercase "i" mid-sentence is fine ... but NOT in formal rejection/decline contexts.
- Trailing/incomplete questions are natural ...
- Use contractions ("haven't", "I'm", "it's").
- "Got it" / "Got it, thanks" - no exclamation, no extra "for the info."
- The user often starts mid-thought ... Do not smooth this into formal English.
- Sound like a 2-second phone reply. NOT customer service. NOT "professional email tone".
```

These rules are presented as universal but every one of them is **user-specific**. For a formal-writer mailbox they actively pull drafts in the wrong direction. The header `(these OVERRIDE default AI tone)` makes them *stronger* than the data-driven blocks that should be carrying the load.

Meanwhile, the existing adaptive + voice-signals + FavoredPhrases stack **already covers every one of these dimensions data-driven from the actual user**:

| Static rule (305-314)                                | Data-driven equivalent already in prompt                                                              |
|------------------------------------------------------|-------------------------------------------------------------------------------------------------------|
| Hard cap 3 sentences / aim 1-2                       | Adaptive: `Typical reply length is around {Min}-{Max} sentences`  +  voice signal: `Median reply length: X words` |
| No exclamation marks                                 | *(no equivalent — gap, see §3.2)*                                                                     |
| No sign-off                                           | Adaptive: `Usually do not add a sign-off` when `SignoffUsageRate < 0.15`                              |
| Lowercase "i" mid-sentence                            | Voice signal: `The user writes lowercase "i" mid-sentence` when rate ≥ 0.35/0.50                      |
| Trailing/incomplete questions                         | Adaptive: `Ending with a direct question is often natural` (>0.5)  +  voice signal: `often ends with a short question` (≥0.4) |
| Use contractions                                       | Adaptive: `Contractions are natural for this user` when `ContractionUsageRate > 0.6`                  |
| "Got it" / "Got it, thanks"                            | FavoredPhrases v1 surfaces real per-shape user phrases                                                |
| Mid-thought fragments                                  | Adaptive: `Fragments or very short sentences can be natural` when `FragmentUsageRate > 0.3`           |
| 2-second phone reply / not customer service          | Adaptive formality bands: `A more conversational tone is natural` when `FormalityScore ≤ 0.35`        |

Every line in the static block has a conditional data-driven counterpart **except** the no-exclamation rule.

---

## 2. Goal (in scope)

Delete the unconditional static "Register rules" block at [LlmDraftGenerator.cs:305-314](../src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs#L305-L314) and rely on the existing data-driven stack (adaptive guidance + voice signals + FavoredPhrases + AvoidPhrases) to carry the same load conditionally.

**Plug the one gap** (no exclamation-rate metric) with a new `ExclamationUsageRate` field on `StyleProfile`, emitted into the adaptive block.

**In scope:**
1. Remove [LlmDraftGenerator.cs:305-314](../src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs#L305-L314) entirely.
2. Add `ExclamationUsageRate` (double, default 0.0) to `StyleProfile` and SQLite migration (mirrors how `GreetingUsageRate` etc. are stored).
3. Compute `ExclamationUsageRate` in `StyleExtractor` (fraction of sentences ending with `!`).
4. Emit conditional adaptive guidance: rate < 0.05 → `- The user almost never uses exclamation marks.`; rate > 0.30 → `- Brief enthusiasm with "!" is natural for this user.` (no line for the middle band, same pattern as the rest of `BuildAdaptiveStyleGuidance`).
5. Keep the upper hard-rule block at [lines 289-303](../src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs#L289-L303) intact — those rules (no `Thank you for reaching out`, no fabricating facts, no praising sender) are genuinely universal anti-AI hygiene, not register-prescription.
6. Tests at every layer.

**Explicitly out of scope:**
- Touching the `personalConfirmationDirective` block (orthogonal grounding logic).
- Replacing `AvoidPhrases` / `FavoredPhrases` / voice signals (those are the loadbearing replacements).
- Adding new universal hard rules to fill the gap — the **point** of this change is to remove unconditional prescription.

---

## 3. Design

### 3.1 What stays in the prompt

The upper hard-rule block (lines 289-303) is universal and stays unchanged:

- "Sound like a person quickly replying from their phone" — tone framing, universal.
- "Get to the point immediately." — universal.
- "Casual-professional is good. Corporate, polished, or 'helpful assistant' sounding is bad." — universal anti-AI.
- "Do not restate or summarize the sender's email back to them." — universal.
- The fillers list at line 296 (`Thank you for reaching out`, etc.) — universal AI clichés; also covered by static `AvoidPhrases`.
- "Do not praise the sender..." — universal.
- "Do not introduce dates, times, numbers..." — universal grounding guard.
- `{greetingRule}` — already conditional from `GreetingPolicy`.
- Availability/experience `[placeholder]` rule — universal grounding guard.

### 3.2 The exclamation gap

This is the only register dimension without a data-driven counterpart. Two options reviewed:

**Option A (chosen):** Add `ExclamationUsageRate` to `StyleProfile`. Same shape as the other usage rates already in the record. One new migration column. Conditional adaptive guidance line.

**Option B (rejected):** Keep a softened static line (`Match the user's punctuation register — do not amplify with exclamation marks they wouldn't use.`). Rejected because it's still prescriptive in the absence of evidence, and it lies on the rationale ("their punctuation register" implies the prompt *knows* it; it doesn't unless we measure it).

Implementation sketch for Option A:

```csharp
// StyleExtractor.cs — alongside QuestionEndingRate calculation
var exclamationEndingRate = sentences.Count == 0
    ? 0.0
    : (double)sentences.Count(s => s.TrimEnd().EndsWith("!")) / sentences.Count;
```

```csharp
// LlmDraftGenerator.cs BuildAdaptiveStyleGuidance — alongside QuestionEndingRate band
if (styleProfile.ExclamationUsageRate < 0.05)
{
    guidance.Add("- The user almost never uses exclamation marks.");
}
else if (styleProfile.ExclamationUsageRate > 0.30)
{
    guidance.Add("- Brief enthusiasm with \"!\" is natural for this user.");
}
```

### 3.3 Migration

Additive nullable double column on the SQLite store, default 0.0 for legacy rows (same pattern as `FragmentUsageRate` etc. were added). `StyleProfile:RebuildOnStartup` is `true` in `appsettings.json` so existing mailboxes repopulate naturally on next startup.

---

## 4. Implementation Steps

| # | File | Change |
|---|---|---|
| 1 | [src/EmailCopilot.Worker/Models/Style/StyleProfile.cs](../src/EmailCopilot.Worker/Models/Style/StyleProfile.cs) | Add `double ExclamationUsageRate = 0.0` to record. Place alongside `QuestionEndingRate`. |
| 2 | [src/EmailCopilot.Worker/Services/Style/StyleExtractor.cs](../src/EmailCopilot.Worker/Services/Style/StyleExtractor.cs) | Compute exclamation rate from sentence-ending punctuation. Pass through to `StyleProfile` ctor. |
| 3 | [src/EmailCopilot.Worker/Services/Infrastructure/SqliteStyleProfileStore.cs](../src/EmailCopilot.Worker/Services/Infrastructure/SqliteStyleProfileStore.cs) | Add `ExclamationUsageRate` column; migration helper adds it nullable with default 0.0; read/write paths. |
| 4 | [src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs](../src/EmailCopilot.Worker/Services/Drafting/LlmDraftGenerator.cs) | **Delete lines 305-314** (register block + the blank line preceding it). Add exclamation band to `BuildAdaptiveStyleGuidance`. |
| 5 | Tests (see §5) | New + updated. |

No new config sections. No new services. No new pipeline stages.

---

## 5. Tests

### 5.1 New tests

**File:** `tests/EmailCopilot.Worker.Tests/Drafting/LlmDraftGeneratorStaticRegisterTests.cs` (new)
- `Prompt_does_not_contain_unconditional_lowercase_i_directive` — formal-writer profile (FormalityScore=0.9, FragmentUsageRate=0.0, ContractionUsageRate=0.1, SignoffUsageRate=0.8) → assert prompt does **not** contain `lowercase "i" mid-sentence`, does **not** contain `2-second phone reply`, does **not** contain `mid-thought`, does **not** contain `Hard cap: 3 sentences`.
- `Prompt_adaptive_block_emits_formal_register_for_formal_profile` — same profile → assert prompt **does** contain `Lean more formal`.
- `Prompt_informal_profile_still_gets_equivalent_register_guidance` — loc8me-like profile (FormalityScore=0.3, FragmentUsageRate=0.4, ContractionUsageRate=0.7, SignoffUsageRate=0.05) → assert prompt contains `conversational tone is natural`, `Contractions are natural`, `Fragments or very short sentences`, `Usually do not add a sign-off`.

**File:** `tests/EmailCopilot.Worker.Tests/Drafting/LlmDraftGeneratorExclamationTests.cs` (new)
- `Exclamation_directive_emitted_when_rate_below_low_threshold` — `ExclamationUsageRate=0.02` → assert prompt contains `almost never uses exclamation marks`.
- `Exclamation_directive_emitted_when_rate_above_high_threshold` — `ExclamationUsageRate=0.40` → assert prompt contains `Brief enthusiasm`.
- `Exclamation_directive_silent_in_middle_band` — `ExclamationUsageRate=0.15` → assert prompt contains neither line.

**File:** `tests/EmailCopilot.Worker.Tests/Style/StyleExtractorExclamationRateTests.cs` (new)
- `Computes_zero_when_no_sentences_end_with_bang`
- `Computes_correct_rate_when_two_of_five_sentences_end_with_bang`
- `Treats_trailing_whitespace_correctly` — `"Yes!  "` counts.

**File:** `tests/EmailCopilot.Worker.Tests/Infrastructure/SqliteStyleProfileStoreExclamationRateTests.cs` (new)
- `Round_trips_ExclamationUsageRate`
- `Legacy_row_without_column_reads_as_zero`

### 5.2 Existing tests that may need updates

Likely none break because the deleted block doesn't drive any current assertions, but verify:
- `LlmDraftGeneratorTests` — any test that string-matches `Hard cap`, `2-second phone reply`, `mid-thought`, `Got it`, `lowercase "i"`, `exclamation marks` in the prompt body (not in adaptive output) — those assertions would need to move to the adaptive / voice-signals output.
- `LlmDraftGeneratorAvoidPhrasesTests` / `LlmDraftGeneratorFavoredPhrasesTests` — should be unaffected.
- `LlmDraftGeneratorTests.Voice_signals_*` — already covered by data-driven path; unaffected.

Run `dotnet test ... -v minimal` after deletion to flush out any string-match regressions.

---

## 6. Risk & Mitigation

| Risk | Mitigation |
|------|------------|
| Informal users get longer/wordier drafts now that the 3-sentence cap is gone | Adaptive guidance still emits `Typical reply length is around {Min}-{Max} sentences`. Voice signal still emits `Median reply length: X words.` Hard cap at line 306 was redundant for the loc8me profile. If drift is observed in live runs, add a tighter band to adaptive (e.g., `Typical reply length is around X-Y *very short* sentences` when `TypicalSentenceCountMax ≤ 2`). |
| Informal users get exclamation marks now that the unconditional rule is gone | New `ExclamationUsageRate` band emits `almost never uses exclamation marks` for the loc8me profile. |
| FavoredPhrases v1 doesn't actually surface "Got it" for every mailbox where the user uses it | Acceptable — FavoredPhrases v1 is the right home for surface-form phrase steering. If "Got it" doesn't surface, that's evidence the extraction heuristic needs tuning in FavoredPhrases, not that we should re-add the static line. |
| Cumulative effect of removing all overrides is that the model regresses for the loc8me mailbox | Verification step §7 includes a side-by-side prompt diff on a real loc8me UID and a live draft run, before merge. |

---

## 7. Verification

1. `dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false -v minimal` — all 308 pre-existing + new tests pass.
2. **Prompt diff:** capture the rendered prompt for a known loc8me UID before and after this change. Adaptive block should now carry the register guidance that the deleted static block was duplicating. Voice signals + FavoredPhrases should be visibly stronger contributors.
3. **Live draft A/B:** run the worker on the same UID before and after; eyeball the draft for register regression (would expect parity for the loc8me mailbox since adaptive bands are already firing).
4. **Cross-profile check:** build a one-off test mailbox or synthetic profile with FormalityScore=0.9 and confirm the prompt does **not** push the model toward informal voice.

---

## 8. Codex Review Checklist

CLAUDE.md §7 requires Codex review for `LlmDraftGenerator` prompt-structure changes. Specific questions for Codex:

- Is the deletion of the entire static register block too aggressive, or is the data-driven stack genuinely sufficient?
- Is `ExclamationUsageRate` the right new metric to plug the only gap, or should this be folded into an existing rate (e.g., a renamed `EnthusiasmRate`)?
- Should the adaptive band thresholds for exclamation be more conservative (0.05/0.30) or symmetric with other rates (0.15/0.5)?
- Any concern about the upper hard-rule block (289-303) silently picking up the slack — should any of those lines also move to adaptive?
- Does deleting `Hard cap: 3 sentences. Aim for 1-2.` risk regression on loc8me, given adaptive only emits a *range*, not a cap?

---

## 9. Files Modified Summary

| File | Change |
|---|---|
| `Models/Style/StyleProfile.cs` | Add `ExclamationUsageRate` |
| `Services/Style/StyleExtractor.cs` | Compute exclamation rate |
| `Services/Infrastructure/SqliteStyleProfileStore.cs` | Persist & migrate column |
| `Services/Drafting/LlmDraftGenerator.cs` | Delete static register block (305-314); add exclamation band to adaptive |
| `tests/.../Drafting/LlmDraftGeneratorStaticRegisterTests.cs` | New |
| `tests/.../Drafting/LlmDraftGeneratorExclamationTests.cs` | New |
| `tests/.../Style/StyleExtractorExclamationRateTests.cs` | New |
| `tests/.../Infrastructure/SqliteStyleProfileStoreExclamationRateTests.cs` | New |
