# Frontend Redesign — Split-Pane Inspector

**Date:** 2026-04-19  
**Status:** Approved for implementation  
**Reviewed by:** Claude + Codex

---

## Problem

The current `emailcopilot-web` frontend is a single-file React app with three issues that make it poor for demo and daily use:

1. **N+1 fetching**: List endpoint returns summaries, but the frontend ignores the summary data and fetches each draft's full detail individually on load.
2. **No triage flow**: All variants are always expanded. No keyboard navigation. Reviewing multiple drafts is slow.
3. **No original email**: You can't see the email you're replying to without going to SQLite or the raw mailbox.
4. **Generic aesthetics**: Segoe UI, heavy shadows, bubbly border-radius — no character.

---

## Design: Option B — Split-Pane Inspector

Three-column layout: nav rail + queue list + detail panel.

```
┌──────┬──────────────────────┬──────────────────────────────────┐
│  Nav │  Queue list          │  Detail panel (slides in)        │
│      │                      │                                  │
│  ●   │  ● HIGH  Subject A   │  Subject A                       │
│  ●   │    from@example.com  │  from@example.com · Apr 19       │
│  ●   │                      │                                  │
│      │  ○ MED   Subject B   │  ▼ Original email (open default) │
│      │    from2@example.com │  ▼ Analysis (asks, branches)     │
│      │                      │                                  │
│      │  ○ LOW   Subject C   │  [Direct Answer] [Acknowledge]   │
│      │                      │  ┌────────────────────────────┐  │
│      │                      │  │ draft body text here...    │  │
│      │                      │  └────────────────────────────┘  │
│      │                      │                                  │
│      │                      │  [Approve ↵]  [Dismiss D]  J/K  │
└──────┴──────────────────────┴──────────────────────────────────┘
```

---

## Section 1: Layout

| Column | Width | Content |
|--------|-------|---------|
| Nav rail | 56px | Icon links: Review Queue, Skip Audit, Run History |
| Queue list | 340px | Compact rows, always visible |
| Detail panel | fills remainder | Slides in from right when row selected |

- App shell: `display: grid; grid-template-columns: 56px 340px 1fr`
- Detail panel presence toggled by selected draft ID in state; panel slides in with `200ms ease` CSS translate
- On mobile (< 768px): stack to single column, queue list is primary view, detail panel is a full-screen overlay

---

## Section 2: Queue List

Each row shows:
- **Urgency dot** (color-coded: red=HIGH, amber=MEDIUM, slate=LOW, light=UNKNOWN)
- **Subject** (bold, truncated to 1 line)
- **From address** (subtle, truncated)
- **Variant count chip** (`2 options`)
- **Confidence mini-bar** (thin horizontal bar, 0–100%)

Fetched from `GET /api/drafts?status=PENDING&skip=0&take=20`. The list endpoint already returns `fromAddress`, `subject`, `variantCount`, `topConfidence`. **Urgency must be added** (see backend changes).

Hover state: faint background tint + quiet `×` dismiss button on right edge (dismisses without opening detail).

Selected row: `--selected-row` (#EBF0F7) background, persists while detail panel is open.

Sorted client-side: urgency HIGH first, then by `sourceReceivedAt` descending within each urgency tier. All summaries are fetched in one request so client-side sort is free.

---

## Section 3: Detail Panel

Opened by clicking a queue row or pressing `Enter`. Closed by `Escape`.

### Header
Subject line (large), from address + received date (secondary).

### Original Email (collapsible, **open by default**)
- Lazy-fetched via `GET /api/drafts/{id}/originalEmail` (new endpoint, see backend changes)
- Rendered in **Instrument Serif**, 16px, line-height 1.7
- Loading skeleton shown while fetching
- Graceful fallback: "Email body not available for this draft" if endpoint returns null/404

### Analysis (collapsible, open by default)
- Urgency badge
- Asks list (bullet points)
- Decision branches (secondary text)
- Stated deadlines if present

### Variant Tabs
- Tab bar: one tab per variant, labelled with `shapeLabel`
- Each tab shows confidence % as a tooltip on hover (with legend: "0.5 = baseline, 0.9+ = high confidence")
- Active tab content: draft body rendered in **JetBrains Mono**, 14px
- Grounding warning shown inline below draft body if present, in amber

### Action Bar (sticky bottom)
```
[  Approve this draft  ↵ ]    [ Dismiss  D ]      J ↓  K ↑  1 2 3
```
- Approve button: `--accent` (#C2620A), full-width left, keyboard hint inline
- Dismiss button: ghost style, right side
- Keyboard hints: subtle `kbd` chips in the far right

---

## Section 4: Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `J` / `↓` | Next item in queue |
| `K` / `↑` | Previous item |
| `Enter` | Open / focus detail panel |
| `1` `2` `3` | Select variant tab |
| `A` | Approve currently selected variant |
| `D` | Dismiss current draft |
| `Esc` | Close detail panel |
| `R` | Refresh queue |

Shortcuts active only when no input is focused. Implemented via `useEffect` keydown listener on `document` in the root component.

---

## Section 5: Micro-interactions

- **Item exit on action**: approved/dismissed item slides up + fades out of queue list; focus moves to next item automatically
- **Detail panel entry**: slides in from right, `translateX(100%) → translateX(0)`, 200ms ease
- **Collapsible sections**: smooth `max-height` transition, 150ms
- **Variant tab switch**: instant, no animation (content already loaded)
- **Hover quick-dismiss**: `×` button fades in at 150ms on row hover

---

## Section 6: Visual Language

### Typography

| Role | Font | Source |
|------|------|--------|
| UI chrome, labels, buttons | Plus Jakarta Sans | Google Fonts |
| Original email body | Instrument Serif | Google Fonts |
| Draft body | JetBrains Mono | Google Fonts |

Loaded via `<link>` in `index.html`.

### Colour System

```css
--bg-ground:       #FAFAF8   /* warm off-white, main surface */
--bg-queue:        #F2F2EE   /* queue list column */
--bg-panel:        #FFFFFF   /* detail panel */
--bg-sidebar:      #0D1117   /* nav rail */
--text-primary:    #111218
--text-secondary:  #6C7280
--border:          #E4E4DC   /* warm gray */
--accent:          #C2620A   /* burnished copper — approve CTA only */
--accent-hover:    #A3510A
--urgency-high:    #DC2626
--urgency-medium:  #D97706
--urgency-low:     #9CA3AF
--urgency-unknown: #D1D5DB
--selected-row:    #EBF0F7
```

### Aesthetic Rules
- No card shadows — depth from column background contrast
- Border radius: 6px maximum
- Nav rail: deep ink with amber left-border on active link
- Action bar: sticky, thin top border, no shadow
- `kbd` elements: small dark chips for keyboard hint labels

---

## Section 7: Skip Audit & Run History

These pages get polish but not a full rethink.

**Skip Audit**: Same table, cleaned up with the new type system. Add grouping by reason code (collapsible groups). Uses Plus Jakarta Sans throughout.

**Run History**: Timeline layout instead of grid cards. Each run is a horizontal row: timestamp + run ID on the left, evaluated/skipped/drafted stats in the middle, skip bucket pills on the right. More scannable than the current 2×2 grid.

---

## Backend Changes Required

### 1. Add `urgency` to `DraftSetSummaryDto`
- Nullable `string?` field populated from `analysis.Urgency` at list time
- Backward-compatible (nullable)
- File: `ApiEndpoints.cs` (list endpoint) + `DraftSetSummaryDto` record

### 2. New endpoint: `GET /api/drafts/{id}/originalEmail`
- Returns `{ body: string | null }`
- Reads `OriginalEmailBody` from `DraftSetRecord` (new nullable column) if available
- Graceful null response for existing rows
- File: `ApiEndpoints.cs`, `SqliteDraftStore.cs`, `DraftSetRecord`

### 3. Store `OriginalEmailBody` in `DraftSetRecord`
- New nullable `TEXT` column in `DraftSets` SQLite table
- Populated in `ScanInboxUseCase` when calling `StoreDraftSetAsync` — `IncomingEmail.Body` is in memory at this point
- Do **not** expose on the list summary endpoint (keep list fast)
- **Note**: Codex flagged concern about storing full body in DraftSets (bloat risk). Implementation should store the first 2000 characters of plaintext body, appending `[…truncated]` if the original was longer. This respects the "DraftSets = decision artifacts" principle while still giving useful context in the UI.
- Requires Codex sense-check before implementing (ScanInboxUseCase change per CLAUDE.md)

### 4. No schema migration needed for existing rows
- New columns are nullable; existing rows return null body, frontend shows graceful fallback

---

## Future: Real-time Email Triggering

Not in scope for this implementation but the natural next step.

**Local (near-term):** IMAP IDLE — MailKit supports `IdleAsync()`. Refactor `RunOnceWorker` from a one-shot into a long-running `BackgroundService` that holds a persistent IMAP connection and triggers the scan pipeline when the server pushes a new-mail notification. Sub-second latency, no cloud infrastructure needed.

**Cloud/production:** Microsoft Graph change notifications — register a webhook with Outlook's API; Microsoft calls your endpoint when new mail arrives; endpoint triggers the scan. The correct path when this becomes a hosted or multi-user product.

**SQS/Lambda:** Only warranted if the pipeline needs to scale out across mailboxes. Overkill for a single-mailbox copilot today.

---

## Out of Scope

- Undo/revert approved draft (no API endpoint; defer to Phase 3)
- Variant body editing in UI (documented gap; defer)
- Scope-blocked filter on skip audit (Phase 3)

---

## Files Modified

| File | Change |
|------|--------|
| `emailcopilot-web/index.html` | Add Google Fonts link tags |
| `emailcopilot-web/src/App.tsx` | Full rewrite — split into components |
| `emailcopilot-web/src/styles.css` | Full rewrite — new design system |
| `src/.../ApiEndpoints.cs` | Add urgency to summary DTO; add originalEmail endpoint |
| `src/.../DraftSetRecord.cs` | Add OriginalEmailBody property |
| `src/.../SqliteDraftStore.cs` | Schema column + read/write |
| `src/.../ScanInboxUseCase.cs` | Pass body through to StoreDraftSetAsync |

The frontend rewrite is large but self-contained to `emailcopilot-web/`. Backend changes span 4 files but are additive/nullable — no breaking changes.
