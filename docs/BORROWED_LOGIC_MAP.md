# Borrowed-Logic Map: What to Steal From Adjacent Products & Research

**Question:** Given our problem ("make drafts sound like the user, not like AI"), which external sources should we borrow logic from, in what combination?

**Status:** Draft pending Codex review.

---

## Candidate Sources

Five external bodies of work touch our problem. Each has a concrete idea we could lift; not all are equally useful for our stage and constraints.

### S1 — Stylometry / Authorship Attribution (academic, 60+ years old)

Mosteller & Wallace (1964, Federalist Papers); Stamatatos (2009 survey); Burrows's Delta (2002); JGAAP toolkit.

**Core idea:** A person's authorial fingerprint lives in **function-word and discourse-marker frequencies**, not content words. "I think", "anyway", "tbh", "yeah", "right?", "honestly", "fwiw" are stronger identity signals than topic vocabulary.

**Concrete borrowable mechanic:**
- Compute per-user marker rates from sent corpus.
- Inject as prompt facts: `The user uses: 'yeah' (28%), 'anyway' (12%), 'tbh' (8%) — fold these in where natural.`
- Or use Burrows's Delta as a *scoring function* on draft candidates: pick the variant whose marker distribution is closest to the user's.

**Fit for us:** Excellent. Cheap, no extra API calls, pulls from data we already have. Directly addresses voice fidelity.

---

### S2 — Gmail Smart Compose / Smart Reply (Google, 2016–2019)

Kannan et al. 2016 (Smart Reply seq2seq); Chen et al. 2019 (Smart Compose decoder + user-personalization).

**Core idea:** Condition generation on **the user's recently-sent corpus, retrieved by similarity to the current incoming email** — not generic style metrics. The strongest signal is "what did this specific user write in similar situations before."

**Concrete borrowable mechanic:**
- Index `StyleExamples` by subject + sender-type + intent.
- At draft time, retrieve the top 3-5 *most similar* sent emails (by subject embedding similarity or category match).
- Inject those into the prompt as `Past replies you've sent in similar situations: ...`.
- Replaces our current "random recent sent mail" sampling.

**Fit for us:** Strong. Solves a known gap (we already inject examples but pick them generically). Reuses infrastructure we'll have anyway if we adopt embeddings.

---

### S3 — Adversarial NLP / Embedding-Based Avoidance (paraphrase detection literature)

STS-B and Quora Question Pairs benchmarks; sentence-BERT (Reimers & Gurevych 2019).

**Core idea:** **Phrase blocking by semantic similarity, not literal string match.** Compute the embedding of "I'll check my schedule and get back to you" once, then reject any draft sentence within cosine 0.82 of it. Catches `schedule → availability`, word-drops, and arbitrary rephrasings in one shot.

**Concrete borrowable mechanic:**
- Build a small registry of AIism "centroids" (one per family, ~5 entries).
- Post-generation, embed each draft sentence and compare against shape-applicable centroids.
- If match: regenerate once with explicit "your previous sentence was a known AIism — rewrite in the user's voice" instruction.

**Fit for us:** Excellent. Directly kills the synonym-evasion class we keep patching by hand.

---

### S4 — Wordcraft / PAIR Co-Writer UX (Google, 2022)

Yuan et al. 2022 "Wordcraft: Story Writing With Large Language Models"; PAIR's writing-assistant research.

**Core idea:** Models default to a "polished register" the user doesn't have. The mitigation isn't better prompts — it's **capturing the user's reject/edit signals** and feeding them back as anti-examples. Variant selection + edit-tracking = the real teaching loop.

**Concrete borrowable mechanic:**
- Persist `selected_variant_id`, `dismissed_variant_ids`, `edited_to_text` per draft set.
- Build a `LearnedAvoidPhrases` store from dismissed variants over time.
- Compare selected-vs-edited bodies — diffs are "this is what the user *actually* wrote vs. what we drafted" — gold-standard training signal.

**Fit for us:** Highest long-term yield but requires UI + feedback table. Already in Phase 3 backlog as "Feedback capture (selected / dismissed / edited variants)." Not Phase 3 critical-path today.

---

### S5 — Persona Fine-Tuning (DialoGPT-Persona, LIGHT, LoRA personas)

Zhang et al. 2018 (Persona-Chat); Shuster et al. 2022 (LIGHT); Hu et al. 2021 (LoRA).

**Core idea:** "Sound like this specific person" is solved at SOTA by **training a per-user adapter** (LoRA, prefix-tuning, or full fine-tune) on their corpus. Prompt engineering is a cheap approximation.

**Concrete borrowable mechanic:**
- Periodically fine-tune a per-user LoRA adapter on `StyleExamples`.
- Swap the adapter in at draft time.

**Fit for us:** Out of scope until corpus is order-of-magnitude bigger and we have model-hosting infrastructure. Worth noting for the long roadmap — not actionable now.

---

## Borrow Recommendation

Given:
- where we are today (prompt-only, OpenAI-compatible backend, ~180 user-sent samples in `StyleExamples`, growing slowly),
- our current critical failure mode (synonym evasion + voice not matching),
- effort budget (single-engineer pass, no UI work in this iteration),

**borrow from S3 + S1, in that order.**

| | Borrow | From | Why this one | Cost | Risk |
|---|---|---|---|---|---|
| **Primary** | Embedding-similarity avoidance | S3 — paraphrase detection | Kills the entire class of bug we've been hand-patching (synonym evasion). One mechanic permanently retires hand-curating literal variants. | Embeddings API call per draft (~$0.0001/set). New post-generation filter + retry-once. | Threshold calibration — false rejects if too tight. Mitigated by 0.02 buffer over user-sample max cosine. |
| **Primary** | Discourse-marker rate signals | S1 — Stamatatos stylometry | Cheapest high-yield voice-fidelity win. Adds *concrete observable facts* about the user's vocabulary to the prompt — much stronger than rate-band heuristics. | Zero extra API. One regex pass over `StyleExamples` at profile-build time. | Low. Purely additive prompt content. |
| Defer to v1.5 | Similarity-retrieved examples | S2 — Smart Compose | Improves the example block already in our prompt, but smaller delta than S3 + S1. Requires same embedding infra as S3, so cheap to bolt on once S3 is in. | Shared infra with S3 — additional embedding call at draft time per sample. | Low. Replaces existing example sampling. |
| Defer to v2 | Reject/edit feedback loop | S4 — Wordcraft | Highest long-term yield, but blocked on UI + DB schema work that touches the frontend. Already in Phase 3 backlog. | UI changes + new feedback table + downstream consumer. | Higher integration risk; needs cross-stack work. |
| Out of scope | Per-user LoRA | S5 — DialoGPT-Persona | Not actionable until we have model hosting and ~5–10× current corpus size. | Training infra, model hosting, eval harness. | Way too much for current stage. |

**Combination rationale.** S3 and S1 are orthogonal and compounding:
- S3 removes what's wrong (post-generation filter).
- S1 adds what's right (pre-generation prompt facts).
- They share zero implementation surface — independent ship paths.
- Together, the model is *pulled toward* observed user markers and *pushed away from* AIism centroids in one round-trip — the bidirectional pressure is the whole point.

S2 stacks naturally on top of S3 (reuses embeddings infra) and is the obvious v1.5 follow-up.

S4 is where this problem actually gets solved long-term, but its dependency on UI/feedback persistence puts it on a different track. Worth queueing as a Phase 3 Codex-review-required item once frontend bandwidth opens up.

S5 is the SOTA endgame and irrelevant today.

---

## What to do next

If Codex agrees with the borrow choice:
1. Use the implementation-level spec already drafted at [docs/VOICE_FIDELITY_V1_SPEC.md](VOICE_FIDELITY_V1_SPEC.md) — it covers S3 (Part A) and S1 (Part B). Codex already reviewed that spec and approved both parts (B first, then A).
2. After S3 + S1 are live and producing data, draft a v1.5 spec for S2 (similarity-retrieved examples).
3. S4 (feedback loop) waits for a frontend pass; raise with the user when Phase 3 critical-path frees up.

If Codex disagrees with the borrow choice, surface the disagreement in the conversation so the user can decide.
