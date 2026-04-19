# Frontend Redesign — Split-Pane Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-file React UI with a three-column split-pane inspector (nav rail + queue list + detail panel), fix N+1 fetching, add keyboard navigation, and expose original email body — all backed by three additive backend changes.

**Architecture:** Backend adds one nullable column to `DraftSets`, one field to the summary DTO (with urgency from the cached `AnalysisJson`), and one new endpoint for lazy email body fetch. Frontend is split into focused component files; all state lives in `App.tsx`; CSS is a full rewrite using the new design-system variables.

**Tech Stack:** C# / ASP.NET Core Minimal APIs (backend), React 18 + TypeScript 5.6 + Vite 5 + react-router-dom 6 + Tailwind 3 (frontend), CSS custom properties + CSS animations (no Motion library), Google Fonts via `<link>` tags.

---

## File Map

### Backend (additive changes only — no breaking changes)

| File | Change |
|------|--------|
| `src/EmailCopilot.Worker/Models/Drafting/DraftSetRecord.cs` | Add `OriginalEmailBody` nullable property |
| `src/EmailCopilot.Worker/Models/Drafting/DraftSetQueries.cs` | Add `Urgency` to `DraftSetSummary`; add `OriginalEmailBody` to `DraftSetDetail` |
| `src/EmailCopilot.Worker/Models/Web/ApiDtos.cs` | Add `Urgency` to `DraftSetSummaryDto` |
| `src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs` | Column migration, read/write `OriginalEmailBody`, parse urgency from `AnalysisJson` in list query |
| `src/EmailCopilot.Worker/Services/Runtime/ApiEndpoints.cs` | Pass urgency through summary; add `GET /api/drafts/{id}/originalEmail` |
| `src/EmailCopilot.Worker/Services/Runtime/ScanInboxUseCase.cs` | Populate `OriginalEmailBody` (2000-char cap) when building `DraftSetRecord` |

### Frontend (rewrite — self-contained to `emailcopilot-web/`)

| File | Change |
|------|--------|
| `emailcopilot-web/index.html` | Add Google Fonts `<link>` tags |
| `emailcopilot-web/src/types.ts` | Create — all shared TypeScript types |
| `emailcopilot-web/src/api.ts` | Create — typed API client functions |
| `emailcopilot-web/src/hooks/useKeyboard.ts` | Create — document keydown listener hook |
| `emailcopilot-web/src/components/Sidebar.tsx` | Create — 56px nav rail |
| `emailcopilot-web/src/components/DraftQueue.tsx` | Create — 340px scrollable queue list |
| `emailcopilot-web/src/components/DraftQueueItem.tsx` | Create — single queue row |
| `emailcopilot-web/src/components/DraftDetail.tsx` | Create — detail panel shell + action bar |
| `emailcopilot-web/src/components/VariantTabs.tsx` | Create — tab bar + active draft body |
| `emailcopilot-web/src/components/OriginalEmailSection.tsx` | Create — collapsible, lazy-fetched |
| `emailcopilot-web/src/components/AnalysisSection.tsx` | Create — collapsible asks/branches |
| `emailcopilot-web/src/pages/SkippedPage.tsx` | Create — extracted from App.tsx |
| `emailcopilot-web/src/pages/RunsPage.tsx` | Create — extracted from App.tsx |
| `emailcopilot-web/src/App.tsx` | Full rewrite — layout shell + queue state |
| `emailcopilot-web/src/styles.css` | Full rewrite — design-system variables + layout |

---

## Task 1: Add `OriginalEmailBody` column to backend model and store

**Files:**
- Modify: `src/EmailCopilot.Worker/Models/Drafting/DraftSetRecord.cs`
- Modify: `src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs`

- [ ] **Step 1: Add property to `DraftSetRecord`**

Open `src/EmailCopilot.Worker/Models/Drafting/DraftSetRecord.cs`. The file currently ends at:
```csharp
    public string? AnalysisJson { get; init; }
    public IReadOnlyList<DraftVariantRecord> Variants { get; init; } = [];
```
Add one line after `AnalysisJson`:
```csharp
    public string? OriginalEmailBody { get; init; }
```

- [ ] **Step 2: Add additive column migration in `SqliteDraftStore.InitializeAsync`**

In `SqliteDraftStore.cs`, after the existing `EnsureDraftSetColumnExistsAsync` calls (around line 170), add:
```csharp
await EnsureDraftSetColumnExistsAsync(connection, "OriginalEmailBody", "TEXT NULL", cancellationToken);
```
This uses the existing migration helper — no schema breakage for old rows.

- [ ] **Step 3: Persist `OriginalEmailBody` in `InsertAsync`**

In the `INSERT INTO DraftSets` command text (around line 237), add the column and placeholder:
```sql
INSERT INTO DraftSets (
    SourceImapUid,
    SourceMessageId,
    FromAddress,
    Subject,
    OriginalBodyPreview,
    SourceReceivedAtUtc,
    CreatedAtUtc,
    LlmMode,
    IsAmbiguous,
    Status,
    AnalysisJson,
    OriginalEmailBody        -- add this line
)
VALUES (
    $sourceImapUid,
    $sourceMessageId,
    $fromAddress,
    $subject,
    $originalBodyPreview,
    $sourceReceivedAtUtc,
    $createdAtUtc,
    $llmMode,
    $isAmbiguous,
    $status,
    $analysisJson,
    $originalEmailBody        -- add this line
);
```
And add the parameter after the existing `$analysisJson` line:
```csharp
command.Parameters.AddWithValue("$originalEmailBody", (object?)draftSet.OriginalEmailBody ?? DBNull.Value);
```

- [ ] **Step 4: Run tests to confirm no regressions**

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false -v minimal
```
Expected: all existing tests pass (insert path adds an optional column, old rows unaffected).

- [ ] **Step 5: Commit**

```bash
git add src/EmailCopilot.Worker/Models/Drafting/DraftSetRecord.cs
git add src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs
git commit -m "feat: add OriginalEmailBody nullable column to DraftSets"
```

---

## Task 2: Populate `OriginalEmailBody` in `ScanInboxUseCase`

**Files:**
- Modify: `src/EmailCopilot.Worker/Services/Runtime/ScanInboxUseCase.cs`

- [ ] **Step 1: Add `BuildOriginalEmailBody` helper method**

In `ScanInboxUseCase.cs`, find the existing `BuildPreview` static helper (grep for `BuildPreview`). Add a new static helper immediately after it:
```csharp
private static string? BuildOriginalEmailBody(string? bodyText)
{
    if (string.IsNullOrWhiteSpace(bodyText))
        return null;

    const int maxLength = 2000;
    if (bodyText.Length <= maxLength)
        return bodyText;

    return bodyText[..maxLength] + "[…truncated]";
}
```

- [ ] **Step 2: Set `OriginalEmailBody` when creating `DraftSetRecord`**

Around line 272 where `new DraftSetRecord` is constructed, add the new property:
```csharp
var draftSet = new DraftSetRecord
{
    SourceImapUid = candidate.ImapUid,
    SourceMessageId = candidate.MessageId,
    FromAddress = candidate.From.Address,
    Subject = candidate.Subject,
    OriginalBodyPreview = BuildPreview(candidate.BodyText),
    OriginalEmailBody = BuildOriginalEmailBody(candidate.BodyText),  // add this line
    SourceReceivedAtUtc = candidate.ReceivedAtUtc,
    CreatedAtUtc = DateTimeOffset.UtcNow,
    LlmMode = _replyDraftGenerator.UseMock ? "mock" : "remote",
    IsAmbiguous = variants.Count > 1,
    Status = DraftSetStatuses.Pending,
    AnalysisJson = JsonSerializer.Serialize(analysis),
    Variants = variants
};
```

- [ ] **Step 3: Run tests**

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false -v minimal
```

- [ ] **Step 4: Commit**

```bash
git add src/EmailCopilot.Worker/Services/Runtime/ScanInboxUseCase.cs
git commit -m "feat: populate OriginalEmailBody (2000-char cap) in ScanInboxUseCase"
```

---

## Task 3: Add `Urgency` to list summary DTO

**Files:**
- Modify: `src/EmailCopilot.Worker/Models/Drafting/DraftSetQueries.cs`
- Modify: `src/EmailCopilot.Worker/Models/Web/ApiDtos.cs`
- Modify: `src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs`
- Modify: `src/EmailCopilot.Worker/Services/Runtime/ApiEndpoints.cs`

- [ ] **Step 1: Add `Urgency` to `DraftSetSummary`**

In `DraftSetQueries.cs`, the `DraftSetSummary` record currently has 8 positional params. Add `string? Urgency` at the end:
```csharp
public sealed record DraftSetSummary(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    int VariantCount,
    double TopConfidence,
    string? Urgency);   // add this
```

- [ ] **Step 2: Update the SQL query in `GetDraftSetsAsync` to extract urgency from `AnalysisJson`**

The current `SELECT` in `GetDraftSetsAsync` (around line 432) returns 8 columns. Extend it to extract `urgency` from the JSON stored in `AnalysisJson` using SQLite's `json_extract`:
```sql
SELECT
    ds.Id,
    ds.FromAddress,
    ds.Subject,
    ds.SourceReceivedAtUtc,
    ds.CreatedAtUtc,
    ds.Status,
    COUNT(dv.Id) AS VariantCount,
    COALESCE(MAX(dv.ConfidenceScore), 0),
    json_extract(ds.AnalysisJson, '$.urgency') AS Urgency
FROM DraftSets ds
LEFT JOIN DraftVariants dv ON dv.DraftSetId = ds.Id
WHERE ($status IS NULL OR ds.Status = $status)
GROUP BY ds.Id, ds.FromAddress, ds.Subject, ds.SourceReceivedAtUtc, ds.CreatedAtUtc, ds.Status, ds.AnalysisJson
ORDER BY ds.SourceReceivedAtUtc DESC, ds.CreatedAtUtc DESC
LIMIT $take OFFSET $skip;
```

Note: `ds.AnalysisJson` must be added to `GROUP BY` because it is referenced by `json_extract`.

- [ ] **Step 3: Map the new column in the reader**

In the `while (await reader.ReadAsync)` block, update the `new DraftSetSummary(...)` call to pass the 9th column:
```csharp
results.Add(new DraftSetSummary(
    reader.GetInt64(0),
    reader.GetString(1),
    reader.GetString(2),
    DateTimeOffset.Parse(reader.GetString(3)),
    DateTimeOffset.Parse(reader.GetString(4)),
    reader.GetString(5),
    reader.GetInt32(6),
    reader.GetDouble(7),
    reader.IsDBNull(8) ? null : reader.GetString(8)));  // urgency
```

- [ ] **Step 4: Add `Urgency` to `DraftSetSummaryDto` in `ApiDtos.cs`**

```csharp
public sealed record DraftSetSummaryDto(
    long Id,
    string FromAddress,
    string Subject,
    DateTimeOffset SourceReceivedAt,
    DateTimeOffset DraftCreatedAt,
    string Status,
    int VariantCount,
    double TopConfidence,
    string? Urgency);   // add this
```

- [ ] **Step 5: Pass urgency through in `ApiEndpoints.cs`**

In the `GET /drafts` handler, update the `new DraftSetSummaryDto(...)` projection:
```csharp
return Results.Ok(draftSets.Select(draft => new DraftSetSummaryDto(
    draft.Id,
    draft.FromAddress,
    draft.Subject,
    draft.SourceReceivedAt,
    draft.DraftCreatedAt,
    draft.Status,
    draft.VariantCount,
    draft.TopConfidence,
    draft.Urgency)));   // add this
```

- [ ] **Step 6: Run tests**

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false -v minimal
```

- [ ] **Step 7: Commit**

```bash
git add src/EmailCopilot.Worker/Models/Drafting/DraftSetQueries.cs
git add src/EmailCopilot.Worker/Models/Web/ApiDtos.cs
git add src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs
git add src/EmailCopilot.Worker/Services/Runtime/ApiEndpoints.cs
git commit -m "feat: add urgency to DraftSetSummary list endpoint (extracted from AnalysisJson)"
```

---

## Task 4: Add `GET /api/drafts/{id}/originalEmail` endpoint

**Files:**
- Modify: `src/EmailCopilot.Worker/Models/Drafting/DraftSetQueries.cs`
- Modify: `src/EmailCopilot.Worker/Services/Infrastructure/IDraftStore.cs`
- Modify: `src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs`
- Modify: `src/EmailCopilot.Worker/Services/Runtime/ApiEndpoints.cs`

- [ ] **Step 1: Add `GetOriginalEmailBodyAsync` to `IDraftStore`**

In `IDraftStore.cs`, add:
```csharp
Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken);
```

- [ ] **Step 2: Implement in `SqliteDraftStore`**

After `GetDraftSetByIdAsync`, add:
```csharp
public async Task<string?> GetOriginalEmailBodyAsync(long id, CancellationToken cancellationToken)
{
    await using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT OriginalEmailBody FROM DraftSets WHERE Id = $id;";
    command.Parameters.AddWithValue("$id", id);

    var result = await command.ExecuteScalarAsync(cancellationToken);
    return result is DBNull or null ? null : (string)result;
}
```

- [ ] **Step 3: Register endpoint in `ApiEndpoints.cs`**

After the existing `GET /drafts/{id:long}` endpoint registration, add:
```csharp
api.MapGet("/drafts/{id:long}/originalEmail", async (long id, IDraftStore draftStore, CancellationToken cancellationToken) =>
{
    var body = await draftStore.GetOriginalEmailBodyAsync(id, cancellationToken);
    return Results.Ok(new { body });
});
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests/EmailCopilot.Worker.Tests/EmailCopilot.Worker.Tests.csproj -p:UseAppHost=false -v minimal
```

- [ ] **Step 5: Commit**

```bash
git add src/EmailCopilot.Worker/Models/Drafting/DraftSetQueries.cs
git add src/EmailCopilot.Worker/Services/Infrastructure/IDraftStore.cs
git add src/EmailCopilot.Worker/Services/Infrastructure/SqliteDraftStore.cs
git add src/EmailCopilot.Worker/Services/Runtime/ApiEndpoints.cs
git commit -m "feat: add GET /api/drafts/{id}/originalEmail endpoint"
```

---

## Task 5: Google Fonts + CSS design system

**Files:**
- Modify: `emailcopilot-web/index.html`
- Modify: `emailcopilot-web/src/styles.css`

- [ ] **Step 1: Add Google Fonts to `index.html`**

Replace the current `<head>` block:
```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Email Copilot</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600&family=Instrument+Serif:ital@0;1&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet" />
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 2: Rewrite `styles.css`**

Replace the entire file content with:
```css
@tailwind base;
@tailwind components;
@tailwind utilities;

:root {
  --bg-ground:       #FAFAF8;
  --bg-queue:        #F2F2EE;
  --bg-panel:        #FFFFFF;
  --bg-sidebar:      #0D1117;
  --text-primary:    #111218;
  --text-secondary:  #6C7280;
  --border:          #E4E4DC;
  --accent:          #C2620A;
  --accent-hover:    #A3510A;
  --urgency-high:    #DC2626;
  --urgency-medium:  #D97706;
  --urgency-low:     #9CA3AF;
  --urgency-unknown: #D1D5DB;
  --selected-row:    #EBF0F7;

  font-family: 'Plus Jakarta Sans', sans-serif;
  color: var(--text-primary);
  background: var(--bg-ground);
}

*, *::before, *::after { box-sizing: border-box; }

html, body, #root {
  height: 100%;
  margin: 0;
}

button, input, textarea, select { font: inherit; }

/* ── App Shell ────────────────────────────────── */
.app-shell {
  display: grid;
  grid-template-columns: 56px 340px 1fr;
  height: 100vh;
  overflow: hidden;
}

/* ── Nav Rail ─────────────────────────────────── */
.nav-rail {
  background: var(--bg-sidebar);
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 16px 0;
  gap: 4px;
  border-right: 1px solid rgba(255,255,255,0.06);
}

.nav-rail a {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: 8px;
  color: rgba(255,255,255,0.5);
  text-decoration: none;
  font-size: 18px;
  transition: color 120ms, background 120ms;
  border-left: 2px solid transparent;
}

.nav-rail a:hover {
  color: rgba(255,255,255,0.85);
  background: rgba(255,255,255,0.06);
}

.nav-rail a.active {
  color: #fff;
  border-left-color: var(--accent);
  background: rgba(255,255,255,0.08);
}

/* ── Queue List ───────────────────────────────── */
.queue-list {
  background: var(--bg-queue);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.queue-header {
  padding: 14px 16px 10px;
  border-bottom: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
}

.queue-header h2 {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--text-secondary);
}

.queue-items {
  flex: 1;
  overflow-y: auto;
}

/* Queue row */
.queue-row {
  position: relative;
  padding: 10px 16px;
  cursor: pointer;
  border-bottom: 1px solid var(--border);
  display: grid;
  grid-template-columns: 10px 1fr auto;
  gap: 10px;
  align-items: start;
  transition: background 80ms;
}

.queue-row:hover { background: rgba(0,0,0,0.04); }
.queue-row.selected { background: var(--selected-row); }

.urgency-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-top: 5px;
  flex-shrink: 0;
}

.urgency-dot[data-urgency="HIGH"]    { background: var(--urgency-high); }
.urgency-dot[data-urgency="MEDIUM"]  { background: var(--urgency-medium); }
.urgency-dot[data-urgency="LOW"]     { background: var(--urgency-low); }
.urgency-dot[data-urgency="UNKNOWN"] { background: var(--urgency-unknown); }
.urgency-dot:not([data-urgency])     { background: var(--urgency-unknown); }

.queue-row-body {
  min-width: 0;
}

.queue-row-subject {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--text-primary);
}

.queue-row-from {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  margin-top: 2px;
}

.queue-row-meta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  flex-shrink: 0;
}

.variant-chip {
  font-size: 11px;
  color: var(--text-secondary);
  background: rgba(0,0,0,0.06);
  border-radius: 4px;
  padding: 1px 5px;
}

.confidence-bar {
  width: 40px;
  height: 3px;
  background: var(--border);
  border-radius: 2px;
  overflow: hidden;
}

.confidence-bar-fill {
  height: 100%;
  background: var(--accent);
  border-radius: 2px;
  transition: width 200ms;
}

/* Hover dismiss button */
.queue-dismiss {
  display: none;
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  background: transparent;
  border: none;
  color: var(--text-secondary);
  padding: 4px 6px;
  cursor: pointer;
  border-radius: 4px;
  font-size: 14px;
  line-height: 1;
}

.queue-row:hover .queue-dismiss {
  display: block;
  animation: fade-in 150ms ease;
}

@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }

/* Item exit animation */
.queue-row-exiting {
  animation: slide-up-out 200ms ease forwards;
}

@keyframes slide-up-out {
  from { opacity: 1; transform: translateY(0); max-height: 80px; }
  to   { opacity: 0; transform: translateY(-8px); max-height: 0; padding: 0; border: none; }
}

/* ── Detail Panel ─────────────────────────────── */
.detail-panel {
  background: var(--bg-panel);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transform: translateX(100%);
  transition: transform 200ms ease;
}

.detail-panel.open {
  transform: translateX(0);
}

.detail-content {
  flex: 1;
  overflow-y: auto;
  padding: 24px;
}

.detail-header {
  margin-bottom: 20px;
}

.detail-subject {
  font-size: 20px;
  font-weight: 600;
  margin: 0 0 4px;
  color: var(--text-primary);
}

.detail-from {
  font-size: 13px;
  color: var(--text-secondary);
}

/* Collapsible sections */
.collapsible {
  border: 1px solid var(--border);
  border-radius: 6px;
  margin-bottom: 12px;
  overflow: hidden;
}

.collapsible-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  cursor: pointer;
  background: transparent;
  border: none;
  width: 100%;
  text-align: left;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}

.collapsible-header:hover { background: rgba(0,0,0,0.02); }

.collapsible-chevron {
  font-size: 11px;
  color: var(--text-secondary);
  transition: transform 150ms;
}

.collapsible-chevron.open { transform: rotate(180deg); }

.collapsible-body {
  overflow: hidden;
  max-height: 0;
  transition: max-height 150ms ease;
}

.collapsible-body.open {
  max-height: 2000px;
}

.collapsible-inner {
  padding: 12px 14px;
  border-top: 1px solid var(--border);
}

/* Original email */
.original-email-body {
  font-family: 'Instrument Serif', serif;
  font-size: 16px;
  line-height: 1.7;
  color: var(--text-primary);
  white-space: pre-wrap;
}

.original-email-skeleton {
  background: linear-gradient(90deg, var(--border) 25%, var(--bg-queue) 50%, var(--border) 75%);
  background-size: 200% 100%;
  animation: shimmer 1.2s infinite;
  border-radius: 4px;
  height: 14px;
  margin-bottom: 8px;
}

@keyframes shimmer {
  from { background-position: 200% 0; }
  to   { background-position: -200% 0; }
}

.original-email-fallback {
  font-size: 13px;
  color: var(--text-secondary);
  font-style: italic;
}

/* Analysis section */
.analysis-asks {
  margin: 0;
  padding-left: 18px;
  font-size: 14px;
  line-height: 1.6;
}

.analysis-asks li + li { margin-top: 4px; }

.analysis-ask-type {
  font-size: 12px;
  color: var(--text-secondary);
}

.analysis-branches {
  margin-top: 8px;
  display: grid;
  gap: 4px;
}

.analysis-branch {
  font-size: 13px;
  color: var(--text-secondary);
}

.urgency-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 4px;
  margin-bottom: 10px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.urgency-badge[data-urgency="HIGH"]   { background: #FEE2E2; color: var(--urgency-high); }
.urgency-badge[data-urgency="MEDIUM"] { background: #FEF3C7; color: var(--urgency-medium); }
.urgency-badge[data-urgency="LOW"]    { background: #F3F4F6; color: var(--urgency-low); }

/* Variant tabs */
.variant-tabs {
  margin-bottom: 12px;
}

.variant-tab-bar {
  display: flex;
  gap: 4px;
  margin-bottom: 8px;
  border-bottom: 1px solid var(--border);
  padding-bottom: 0;
}

.variant-tab-btn {
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  padding: 6px 12px;
  font-size: 13px;
  font-weight: 500;
  color: var(--text-secondary);
  cursor: pointer;
  border-radius: 0;
  margin-bottom: -1px;
  transition: color 100ms, border-color 100ms;
}

.variant-tab-btn:hover { color: var(--text-primary); }

.variant-tab-btn.active {
  color: var(--text-primary);
  border-bottom-color: var(--accent);
}

.variant-body {
  font-family: 'JetBrains Mono', monospace;
  font-size: 14px;
  line-height: 1.6;
  background: #F8F8F6;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 14px;
  white-space: pre-wrap;
  color: var(--text-primary);
}

.grounding-warning {
  margin-top: 8px;
  padding: 8px 12px;
  background: #FFFBEB;
  border: 1px solid #FDE68A;
  border-radius: 6px;
  font-size: 13px;
  color: #92400E;
}

/* Action bar */
.action-bar {
  flex-shrink: 0;
  border-top: 1px solid var(--border);
  padding: 12px 24px;
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--bg-panel);
}

.btn-approve {
  flex: 1;
  background: var(--accent);
  color: #fff;
  border: none;
  border-radius: 6px;
  padding: 10px 16px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  transition: background 120ms;
}

.btn-approve:hover:not(:disabled) { background: var(--accent-hover); }
.btn-approve:disabled { opacity: 0.6; cursor: wait; }

.btn-dismiss {
  background: transparent;
  border: 1px solid var(--border);
  color: var(--text-secondary);
  border-radius: 6px;
  padding: 10px 14px;
  font-size: 14px;
  cursor: pointer;
  transition: border-color 120ms, color 120ms;
}

.btn-dismiss:hover:not(:disabled) {
  border-color: var(--text-secondary);
  color: var(--text-primary);
}

.kbd-hints {
  margin-left: auto;
  display: flex;
  gap: 4px;
  align-items: center;
}

kbd {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: #1A1A1A;
  color: rgba(255,255,255,0.8);
  border-radius: 4px;
  padding: 2px 5px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 11px;
  min-width: 18px;
}

/* ── Empty / Loading states ───────────────────── */
.detail-empty {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 14px;
  padding: 40px;
  text-align: center;
}

.queue-empty {
  padding: 32px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--text-secondary);
}

/* ── Status banners ───────────────────────────── */
.status-banner {
  padding: 10px 14px;
  font-size: 13px;
  border-bottom: 1px solid var(--border);
}

.status-banner.info  { background: #EAF4F5; color: #17324a; }
.status-banner.error { background: #FEE2E2; color: #7F1D1D; }

/* ── Skipped / Runs pages ─────────────────────── */
.page-content {
  background: var(--bg-panel);
  flex: 1;
  overflow-y: auto;
  padding: 28px 32px;
}

.page-title {
  font-size: 22px;
  font-weight: 600;
  margin: 0 0 20px;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.data-table th,
.data-table td {
  text-align: left;
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
  vertical-align: top;
}

.data-table th {
  font-weight: 600;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-secondary);
  background: var(--bg-queue);
}

.reason-pill {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  background: rgba(0,0,0,0.06);
  font-size: 12px;
  color: var(--text-primary);
}

.run-row {
  border-bottom: 1px solid var(--border);
  padding: 14px 0;
}

.run-row-header {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  gap: 24px;
  align-items: center;
}

.run-id { font-weight: 600; font-size: 14px; }
.run-meta { font-size: 12px; color: var(--text-secondary); margin-top: 2px; }
.run-stat { font-size: 13px; color: var(--text-secondary); text-align: right; }

.bucket-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 10px;
}

/* ── Mobile ───────────────────────────────────── */
@media (max-width: 767px) {
  .app-shell {
    grid-template-columns: 1fr;
    grid-template-rows: auto 1fr;
  }

  .nav-rail {
    flex-direction: row;
    height: 48px;
    width: 100%;
    justify-content: center;
    border-right: none;
    border-bottom: 1px solid rgba(255,255,255,0.06);
  }

  .queue-list {
    border-right: none;
  }

  .detail-panel {
    position: fixed;
    inset: 0;
    z-index: 10;
    transform: translateX(100%);
  }

  .detail-panel.open {
    transform: translateX(0);
  }
}
```

- [ ] **Step 3: Verify the dev server loads without errors**

```powershell
cd emailcopilot-web
npm run dev
```
Open [http://localhost:5173](http://localhost:5173) and confirm fonts load (Plus Jakarta Sans in UI, no console errors).

- [ ] **Step 4: Commit**

```bash
git add emailcopilot-web/index.html emailcopilot-web/src/styles.css
git commit -m "feat: add Google Fonts and rewrite CSS design system"
```

---

## Task 6: Create TypeScript types and API client

**Files:**
- Create: `emailcopilot-web/src/types.ts`
- Create: `emailcopilot-web/src/api.ts`

- [ ] **Step 1: Create `types.ts`**

```typescript
// emailcopilot-web/src/types.ts

export type DraftVariant = {
  id: number;
  shape: string;
  shapeLabel: string;
  confidenceScore: number;
  body: string;
  groundingWarning?: string | null;
};

export type EmailAsk = {
  text: string;
  askType: string;
  isOptional: boolean;
};

export type DecisionBranch = {
  summary: string;
  viableReplyShapes: string[];
};

export type EmailAnalysis = {
  asks: EmailAsk[];
  decisionBranches: DecisionBranch[];
  statedDeadlines: string[];
  urgency: string;
};

export type DraftSummary = {
  id: number;
  fromAddress: string;
  subject: string;
  sourceReceivedAt: string;
  draftCreatedAt: string;
  status: string;
  variantCount: number;
  topConfidence: number;
  urgency: string | null;
};

export type DraftDetail = {
  id: number;
  fromAddress: string;
  subject: string;
  sourceReceivedAt: string;
  draftCreatedAt: string;
  status: string;
  variants: DraftVariant[];
  sourceMessageId: string;
  selectedVariantId?: number | null;
  analysis?: EmailAnalysis | null;
};

export type SkippedEmail = {
  id: number;
  fromAddress: string;
  subject: string;
  receivedAt: string;
  reasonCode: string;
};

export type RunRecord = {
  id: number;
  startedAt: string;
  candidatesEvaluated: number;
  skippedCount: number;
  draftCreated: boolean;
  draftId?: number | null;
  skipBuckets: Record<string, number>;
};
```

- [ ] **Step 2: Create `api.ts`**

```typescript
// emailcopilot-web/src/api.ts
import type { DraftDetail, DraftSummary, RunRecord, SkippedEmail } from './types';

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      if (body?.detail) detail = String(body.detail);
      else if (body?.error) detail = String(body.error);
    } catch { /* ignore */ }
    throw new Error(detail);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function getDraftSummaries(): Promise<DraftSummary[]> {
  return fetchJson<DraftSummary[]>('/api/drafts?status=PENDING&skip=0&take=20');
}

export async function getDraftDetail(id: number): Promise<DraftDetail> {
  return fetchJson<DraftDetail>(`/api/drafts/${id}`);
}

export async function getOriginalEmail(id: number): Promise<{ body: string | null }> {
  return fetchJson<{ body: string | null }>(`/api/drafts/${id}/originalEmail`);
}

export async function approveDraft(draftId: number, variantId: number): Promise<void> {
  return fetchJson<void>(`/api/drafts/${draftId}/approve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ variantId }),
  });
}

export async function dismissDraft(draftId: number): Promise<void> {
  return fetchJson<void>(`/api/drafts/${draftId}/dismiss`, { method: 'POST' });
}

export async function getSkippedEmails(): Promise<SkippedEmail[]> {
  return fetchJson<SkippedEmail[]>('/api/skipped?skip=0&take=50');
}

export async function getRunHistory(): Promise<RunRecord[]> {
  return fetchJson<RunRecord[]>('/api/runs?skip=0&take=20');
}
```

- [ ] **Step 3: Commit**

```bash
git add emailcopilot-web/src/types.ts emailcopilot-web/src/api.ts
git commit -m "feat: add typed API client and shared TypeScript types"
```

---

## Task 7: Create keyboard hook

**Files:**
- Create: `emailcopilot-web/src/hooks/useKeyboard.ts`

- [ ] **Step 1: Create the hook**

```typescript
// emailcopilot-web/src/hooks/useKeyboard.ts
import { useEffect } from 'react';

export type KeyHandler = (key: string) => void;

export function useKeyboard(handler: KeyHandler, active: boolean) {
  useEffect(() => {
    if (!active) return;

    function onKeyDown(e: KeyboardEvent) {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target as HTMLElement).isContentEditable) return;
      handler(e.key);
    }

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [handler, active]);
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/hooks/useKeyboard.ts
git commit -m "feat: add useKeyboard hook for document-level shortcuts"
```

---

## Task 8: Create Sidebar component

**Files:**
- Create: `emailcopilot-web/src/components/Sidebar.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/Sidebar.tsx
import { NavLink } from 'react-router-dom';

export function Sidebar() {
  return (
    <nav className="nav-rail" aria-label="Main navigation">
      <NavLink to="/review" title="Review Queue">Q</NavLink>
      <NavLink to="/skipped" title="Skip Audit">S</NavLink>
      <NavLink to="/runs" title="Run History">H</NavLink>
    </nav>
  );
}
```

Note: These are text labels (Q/S/H) because the project uses no icon library. The title tooltip provides full label on hover. Replace with SVG icons if desired later.

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/Sidebar.tsx
git commit -m "feat: add Sidebar nav rail component"
```

---

## Task 9: Create DraftQueueItem component

**Files:**
- Create: `emailcopilot-web/src/components/DraftQueueItem.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/DraftQueueItem.tsx
import type { DraftSummary } from '../types';

type Props = {
  draft: DraftSummary;
  isSelected: boolean;
  isExiting: boolean;
  onClick: () => void;
  onDismiss: (e: React.MouseEvent) => void;
};

export function DraftQueueItem({ draft, isSelected, isExiting, onClick, onDismiss }: Props) {
  const urgency = draft.urgency?.toUpperCase() ?? 'UNKNOWN';

  return (
    <div
      className={`queue-row${isSelected ? ' selected' : ''}${isExiting ? ' queue-row-exiting' : ''}`}
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={e => { if (e.key === 'Enter') onClick(); }}
      aria-selected={isSelected}
    >
      <span className="urgency-dot" data-urgency={urgency} />
      <div className="queue-row-body">
        <div className="queue-row-subject">{draft.subject || '(no subject)'}</div>
        <div className="queue-row-from">{draft.fromAddress}</div>
      </div>
      <div className="queue-row-meta">
        <span className="variant-chip">
          {draft.variantCount} {draft.variantCount === 1 ? 'option' : 'options'}
        </span>
        <div className="confidence-bar">
          <div
            className="confidence-bar-fill"
            style={{ width: `${Math.round(draft.topConfidence * 100)}%` }}
          />
        </div>
      </div>
      <button
        className="queue-dismiss"
        onClick={onDismiss}
        aria-label="Dismiss"
      >
        ×
      </button>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/DraftQueueItem.tsx
git commit -m "feat: add DraftQueueItem queue row component"
```

---

## Task 10: Create DraftQueue component

**Files:**
- Create: `emailcopilot-web/src/components/DraftQueue.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/DraftQueue.tsx
import type { DraftSummary } from '../types';
import { DraftQueueItem } from './DraftQueueItem';

type Props = {
  drafts: DraftSummary[];
  selectedId: number | null;
  exitingIds: Set<number>;
  loading: boolean;
  error: string | null;
  onSelect: (id: number) => void;
  onDismiss: (id: number) => void;
  onRefresh: () => void;
};

export function DraftQueue({
  drafts,
  selectedId,
  exitingIds,
  loading,
  error,
  onSelect,
  onDismiss,
  onRefresh,
}: Props) {
  const sorted = [...drafts].sort((a, b) => {
    const urgencyOrder: Record<string, number> = { HIGH: 0, MEDIUM: 1, LOW: 2, UNKNOWN: 3 };
    const ua = urgencyOrder[(a.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    const ub = urgencyOrder[(b.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    if (ua !== ub) return ua - ub;
    return new Date(b.sourceReceivedAt).getTime() - new Date(a.sourceReceivedAt).getTime();
  });

  return (
    <div className="queue-list">
      <div className="queue-header">
        <h2>Queue {!loading && `· ${drafts.length}`}</h2>
        <button
          className="btn-dismiss"
          onClick={onRefresh}
          disabled={loading}
          style={{ fontSize: 12, padding: '3px 8px' }}
        >
          {loading ? '…' : 'R'}
        </button>
      </div>

      {error && <div className="status-banner error">{error}</div>}

      <div className="queue-items">
        {loading && <div className="queue-empty">Loading…</div>}
        {!loading && sorted.length === 0 && (
          <div className="queue-empty">Queue is clear.</div>
        )}
        {sorted.map(draft => (
          <DraftQueueItem
            key={draft.id}
            draft={draft}
            isSelected={draft.id === selectedId}
            isExiting={exitingIds.has(draft.id)}
            onClick={() => onSelect(draft.id)}
            onDismiss={e => { e.stopPropagation(); onDismiss(draft.id); }}
          />
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/DraftQueue.tsx
git commit -m "feat: add DraftQueue component with client-side urgency sort"
```

---

## Task 11: Create OriginalEmailSection component

**Files:**
- Create: `emailcopilot-web/src/components/OriginalEmailSection.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/OriginalEmailSection.tsx
import { useEffect, useState } from 'react';
import { getOriginalEmail } from '../api';

type Props = {
  draftId: number;
};

type LoadState = 'idle' | 'loading' | 'loaded' | 'error';

export function OriginalEmailSection({ draftId }: Props) {
  const [open, setOpen] = useState(true);
  const [body, setBody] = useState<string | null>(null);
  const [state, setState] = useState<LoadState>('idle');

  useEffect(() => {
    setState('loading');
    setBody(null);
    getOriginalEmail(draftId)
      .then(data => { setBody(data.body); setState('loaded'); })
      .catch(() => setState('error'));
  }, [draftId]);

  return (
    <div className="collapsible">
      <button className="collapsible-header" onClick={() => setOpen(o => !o)}>
        Original Email
        <span className={`collapsible-chevron${open ? ' open' : ''}`}>▼</span>
      </button>
      <div className={`collapsible-body${open ? ' open' : ''}`}>
        <div className="collapsible-inner">
          {state === 'loading' && (
            <>
              <div className="original-email-skeleton" style={{ width: '85%' }} />
              <div className="original-email-skeleton" style={{ width: '70%' }} />
              <div className="original-email-skeleton" style={{ width: '90%' }} />
            </>
          )}
          {state === 'loaded' && body && (
            <div className="original-email-body">{body}</div>
          )}
          {state === 'loaded' && !body && (
            <div className="original-email-fallback">Email body not available for this draft.</div>
          )}
          {state === 'error' && (
            <div className="original-email-fallback">Could not load email body.</div>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/OriginalEmailSection.tsx
git commit -m "feat: add OriginalEmailSection with lazy fetch and loading skeleton"
```

---

## Task 12: Create AnalysisSection component

**Files:**
- Create: `emailcopilot-web/src/components/AnalysisSection.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/AnalysisSection.tsx
import { useState } from 'react';
import type { EmailAnalysis } from '../types';

type Props = {
  analysis: EmailAnalysis;
};

function humanize(value: string) {
  return value.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, l => l.toUpperCase());
}

export function AnalysisSection({ analysis }: Props) {
  const [open, setOpen] = useState(true);
  const urgency = analysis.urgency?.toUpperCase();

  return (
    <div className="collapsible">
      <button className="collapsible-header" onClick={() => setOpen(o => !o)}>
        Analysis
        <span className={`collapsible-chevron${open ? ' open' : ''}`}>▼</span>
      </button>
      <div className={`collapsible-body${open ? ' open' : ''}`}>
        <div className="collapsible-inner">
          {urgency && urgency !== 'UNKNOWN' && (
            <div className="urgency-badge" data-urgency={urgency}>{urgency}</div>
          )}
          {analysis.asks.length > 0 && (
            <ul className="analysis-asks">
              {analysis.asks.map((ask, i) => (
                <li key={i}>
                  {ask.text}{' '}
                  <span className="analysis-ask-type">({humanize(ask.askType)})</span>
                </li>
              ))}
            </ul>
          )}
          {analysis.decisionBranches.length > 0 && (
            <div className="analysis-branches">
              {analysis.decisionBranches.map((branch, i) => (
                <div key={i} className="analysis-branch">{branch.summary}</div>
              ))}
            </div>
          )}
          {analysis.statedDeadlines.length > 0 && (
            <div style={{ marginTop: 8, fontSize: 13, color: 'var(--urgency-medium)' }}>
              Deadline: {analysis.statedDeadlines.join(', ')}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/AnalysisSection.tsx
git commit -m "feat: add AnalysisSection collapsible component"
```

---

## Task 13: Create VariantTabs component

**Files:**
- Create: `emailcopilot-web/src/components/VariantTabs.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/VariantTabs.tsx
import type { DraftVariant } from '../types';

type Props = {
  variants: DraftVariant[];
  activeIndex: number;
  onSelect: (index: number) => void;
};

export function VariantTabs({ variants, activeIndex, onSelect }: Props) {
  const active = variants[activeIndex];

  return (
    <div className="variant-tabs">
      <div className="variant-tab-bar">
        {variants.map((v, i) => (
          <button
            key={v.id}
            className={`variant-tab-btn${i === activeIndex ? ' active' : ''}`}
            onClick={() => onSelect(i)}
            title={`${Math.round(v.confidenceScore * 100)}% confidence (0.5 = baseline, 0.9+ = high)`}
          >
            {i + 1}. {v.shapeLabel}
          </button>
        ))}
      </div>
      {active && (
        <>
          <div className="variant-body">{active.body}</div>
          {active.groundingWarning && (
            <div className="grounding-warning">
              Grounding warning: {active.groundingWarning}
            </div>
          )}
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/VariantTabs.tsx
git commit -m "feat: add VariantTabs component with confidence tooltip"
```

---

## Task 14: Create DraftDetail component

**Files:**
- Create: `emailcopilot-web/src/components/DraftDetail.tsx`

- [ ] **Step 1: Create the file**

```tsx
// emailcopilot-web/src/components/DraftDetail.tsx
import { useState } from 'react';
import type { DraftDetail as DraftDetailType } from '../types';
import { OriginalEmailSection } from './OriginalEmailSection';
import { AnalysisSection } from './AnalysisSection';
import { VariantTabs } from './VariantTabs';

type Props = {
  draft: DraftDetailType | null;
  loading: boolean;
  busy: boolean;
  onApprove: (variantId: number) => void;
  onDismiss: () => void;
};

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function DraftDetail({ draft, loading, busy, onApprove, onDismiss }: Props) {
  const [activeVariantIndex, setActiveVariantIndex] = useState(0);

  if (loading) {
    return (
      <div className="detail-empty">Loading…</div>
    );
  }

  if (!draft) {
    return (
      <div className="detail-empty">
        Select a draft from the queue to review it.<br />
        <span style={{ fontSize: 12, marginTop: 8, display: 'block' }}>
          <kbd>J</kbd> / <kbd>K</kbd> to navigate · <kbd>Enter</kbd> to open
        </span>
      </div>
    );
  }

  const activeVariant = draft.variants[activeVariantIndex] ?? draft.variants[0];

  return (
    <>
      <div className="detail-content">
        <div className="detail-header">
          <h1 className="detail-subject">{draft.subject || '(no subject)'}</h1>
          <div className="detail-from">
            {draft.fromAddress} · {formatDate(draft.sourceReceivedAt)}
          </div>
        </div>

        <OriginalEmailSection key={draft.id} draftId={draft.id} />

        {draft.analysis && (
          <AnalysisSection analysis={draft.analysis} />
        )}

        {draft.variants.length > 0 && (
          <VariantTabs
            variants={draft.variants}
            activeIndex={Math.min(activeVariantIndex, draft.variants.length - 1)}
            onSelect={setActiveVariantIndex}
          />
        )}
      </div>

      <div className="action-bar">
        <button
          className="btn-approve"
          disabled={busy || !activeVariant}
          onClick={() => activeVariant && onApprove(activeVariant.id)}
        >
          Approve this draft <kbd style={{ background: 'rgba(255,255,255,0.2)', color: '#fff' }}>↵</kbd>
        </button>
        <button className="btn-dismiss" disabled={busy} onClick={onDismiss}>
          Dismiss <kbd>D</kbd>
        </button>
        <div className="kbd-hints">
          <kbd>J</kbd><kbd>K</kbd>
          {draft.variants.length > 1 && draft.variants.map((_, i) => (
            <kbd key={i}>{i + 1}</kbd>
          ))}
        </div>
      </div>
    </>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add emailcopilot-web/src/components/DraftDetail.tsx
git commit -m "feat: add DraftDetail panel with action bar"
```

---

## Task 15: Create SkippedPage and RunsPage

**Files:**
- Create: `emailcopilot-web/src/pages/SkippedPage.tsx`
- Create: `emailcopilot-web/src/pages/RunsPage.tsx`

- [ ] **Step 1: Create `SkippedPage.tsx`**

```tsx
// emailcopilot-web/src/pages/SkippedPage.tsx
import { useEffect, useState } from 'react';
import type { SkippedEmail } from '../types';
import { getSkippedEmails } from '../api';

function humanize(value: string) {
  return value.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, l => l.toUpperCase());
}

export function SkippedPage() {
  const [items, setItems] = useState<SkippedEmail[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    getSkippedEmails()
      .then(setItems)
      .catch(err => setError(err instanceof Error ? err.message : 'Could not load skipped mail.'))
      .finally(() => setLoading(false));
  }, []);

  const byReason = items.reduce<Record<string, SkippedEmail[]>>((acc, item) => {
    const key = item.reasonCode;
    (acc[key] ??= []).push(item);
    return acc;
  }, {});

  return (
    <div className="page-content">
      <h1 className="page-title">Skip Audit</h1>
      {error && <div className="status-banner error">{error}</div>}
      {loading && <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>Loading…</div>}
      {!loading && items.length === 0 && (
        <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>No skip decisions yet.</div>
      )}
      {Object.entries(byReason).map(([reason, group]) => (
        <details key={reason} open style={{ marginBottom: 16 }}>
          <summary style={{ cursor: 'pointer', fontWeight: 600, fontSize: 14, marginBottom: 8 }}>
            {humanize(reason)} ({group.length})
          </summary>
          <table className="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>From</th>
                <th>Subject</th>
              </tr>
            </thead>
            <tbody>
              {group.map(item => (
                <tr key={item.id}>
                  <td style={{ whiteSpace: 'nowrap', fontSize: 12 }}>
                    {new Date(item.receivedAt).toLocaleString()}
                  </td>
                  <td>{item.fromAddress}</td>
                  <td>{item.subject}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      ))}
    </div>
  );
}
```

- [ ] **Step 2: Create `RunsPage.tsx`**

```tsx
// emailcopilot-web/src/pages/RunsPage.tsx
import { useEffect, useState } from 'react';
import type { RunRecord } from '../types';
import { getRunHistory } from '../api';

function humanize(value: string) {
  return value.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, l => l.toUpperCase());
}

export function RunsPage() {
  const [runs, setRuns] = useState<RunRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    getRunHistory()
      .then(setRuns)
      .catch(err => setError(err instanceof Error ? err.message : 'Could not load runs.'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="page-content">
      <h1 className="page-title">Run History</h1>
      {error && <div className="status-banner error">{error}</div>}
      {loading && <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>Loading…</div>}
      {!loading && runs.length === 0 && (
        <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>No runs yet.</div>
      )}
      {runs.map(run => (
        <div key={run.id} className="run-row">
          <div className="run-row-header">
            <div>
              <div className="run-id">Run #{run.id}</div>
              <div className="run-meta">{new Date(run.startedAt).toLocaleString()}</div>
            </div>
            <div className="run-stat">Evaluated: {run.candidatesEvaluated}</div>
            <div className="run-stat">Skipped: {run.skippedCount}</div>
            <div className="run-stat">Drafted: {run.draftCreated ? 'Yes' : 'No'}</div>
          </div>
          {Object.keys(run.skipBuckets).length > 0 && (
            <div className="bucket-pills">
              {Object.entries(run.skipBuckets).map(([reason, count]) => (
                <span key={reason} className="reason-pill">
                  {humanize(reason)}: {count}
                </span>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
```

- [ ] **Step 3: Commit**

```bash
git add emailcopilot-web/src/pages/SkippedPage.tsx emailcopilot-web/src/pages/RunsPage.tsx
git commit -m "feat: add SkippedPage (grouped by reason) and RunsPage (timeline layout)"
```

---

## Task 16: Rewrite App.tsx

**Files:**
- Modify: `emailcopilot-web/src/App.tsx`

This is the orchestrator. It owns queue state, selected draft detail state, and keyboard wiring. The component files handle all rendering.

- [ ] **Step 1: Replace `App.tsx` entirely**

```tsx
// emailcopilot-web/src/App.tsx
import { useCallback, useEffect, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import { Sidebar } from './components/Sidebar';
import { DraftQueue } from './components/DraftQueue';
import { DraftDetail } from './components/DraftDetail';
import { SkippedPage } from './pages/SkippedPage';
import { RunsPage } from './pages/RunsPage';
import { useKeyboard } from './hooks/useKeyboard';
import { approveDraft, dismissDraft, getDraftDetail, getDraftSummaries } from './api';
import type { DraftDetail as DraftDetailType, DraftSummary } from './types';

function ReviewPage() {
  const [summaries, setSummaries] = useState<DraftSummary[]>([]);
  const [queueLoading, setQueueLoading] = useState(true);
  const [queueError, setQueueError] = useState<string | null>(null);

  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<DraftDetailType | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const [busy, setBusy] = useState(false);
  const [exitingIds, setExitingIds] = useState<Set<number>>(new Set());

  const sortedSummaries = [...summaries].sort((a, b) => {
    const order: Record<string, number> = { HIGH: 0, MEDIUM: 1, LOW: 2, UNKNOWN: 3 };
    const ua = order[(a.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    const ub = order[(b.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    if (ua !== ub) return ua - ub;
    return new Date(b.sourceReceivedAt).getTime() - new Date(a.sourceReceivedAt).getTime();
  });

  const loadQueue = useCallback(async () => {
    setQueueLoading(true);
    setQueueError(null);
    try {
      setSummaries(await getDraftSummaries());
    } catch (err) {
      setQueueError(err instanceof Error ? err.message : 'Could not load queue.');
    } finally {
      setQueueLoading(false);
    }
  }, []);

  useEffect(() => { void loadQueue(); }, [loadQueue]);

  const selectDraft = useCallback(async (id: number) => {
    setSelectedId(id);
    setDetailLoading(true);
    setDetail(null);
    try {
      setDetail(await getDraftDetail(id));
    } catch {
      // show nothing in panel on error
    } finally {
      setDetailLoading(false);
    }
  }, []);

  async function removeFromQueue(id: number) {
    setExitingIds(prev => new Set(prev).add(id));
    await new Promise(r => setTimeout(r, 210));
    setSummaries(prev => prev.filter(s => s.id !== id));
    setExitingIds(prev => { const n = new Set(prev); n.delete(id); return n; });
    if (selectedId === id) {
      setSelectedId(null);
      setDetail(null);
      // Focus next item
      const idx = sortedSummaries.findIndex(s => s.id === id);
      const next = sortedSummaries[idx + 1] ?? sortedSummaries[idx - 1];
      if (next) void selectDraft(next.id);
    }
  }

  async function handleApprove(variantId: number) {
    if (!selectedId || busy) return;
    setBusy(true);
    try {
      await approveDraft(selectedId, variantId);
      await removeFromQueue(selectedId);
    } catch (err) {
      setQueueError(err instanceof Error ? err.message : 'Could not approve draft.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDismiss(id: number) {
    if (busy) return;
    setBusy(true);
    try {
      await dismissDraft(id);
      await removeFromQueue(id);
    } catch (err) {
      setQueueError(err instanceof Error ? err.message : 'Could not dismiss.');
    } finally {
      setBusy(false);
    }
  }

  const handleKey = useCallback((key: string) => {
    const idx = sortedSummaries.findIndex(s => s.id === selectedId);

    switch (key) {
      case 'j':
      case 'J':
      case 'ArrowDown': {
        const next = sortedSummaries[idx + 1] ?? sortedSummaries[0];
        if (next) void selectDraft(next.id);
        break;
      }
      case 'k':
      case 'K':
      case 'ArrowUp': {
        const prev = sortedSummaries[idx - 1] ?? sortedSummaries[sortedSummaries.length - 1];
        if (prev) void selectDraft(prev.id);
        break;
      }
      case 'Enter': {
        if (sortedSummaries.length > 0 && !selectedId) {
          void selectDraft(sortedSummaries[0].id);
        }
        break;
      }
      case 'Escape': {
        setSelectedId(null);
        setDetail(null);
        break;
      }
      case 'a':
      case 'A': {
        if (detail && detail.variants[0]) {
          void handleApprove(detail.variants[0].id);
        }
        break;
      }
      case 'd':
      case 'D': {
        if (selectedId) void handleDismiss(selectedId);
        break;
      }
      case 'r':
      case 'R': {
        void loadQueue();
        break;
      }
    }
  }, [sortedSummaries, selectedId, detail, loadQueue, selectDraft]);

  useKeyboard(handleKey, true);

  return (
    <>
      <DraftQueue
        drafts={summaries}
        selectedId={selectedId}
        exitingIds={exitingIds}
        loading={queueLoading}
        error={queueError}
        onSelect={id => void selectDraft(id)}
        onDismiss={id => void handleDismiss(id)}
        onRefresh={() => void loadQueue()}
      />
      <div className={`detail-panel${selectedId !== null ? ' open' : ''}`}>
        <DraftDetail
          draft={detail}
          loading={detailLoading}
          busy={busy}
          onApprove={variantId => void handleApprove(variantId)}
          onDismiss={() => selectedId !== null && void handleDismiss(selectedId)}
        />
      </div>
    </>
  );
}

function App() {
  return (
    <div className="app-shell">
      <Sidebar />
      <Routes>
        <Route path="/" element={<ReviewPage />} />
        <Route path="/review" element={<ReviewPage />} />
        <Route path="/skipped" element={<><div /><SkippedPage /></>} />
        <Route path="/runs" element={<><div /><RunsPage /></>} />
      </Routes>
    </div>
  );
}

export default App;
```

Note: `/skipped` and `/runs` routes render `<div />` in the queue column slot so the grid stays intact. The detail panel column is filled by `SkippedPage`/`RunsPage` which are full-width in their own `page-content` div.

- [ ] **Step 2: Run the dev server and test manually**

```powershell
cd emailcopilot-web
npm run dev
```

Verify:
- Three-column layout renders (nav rail 56px, queue list 340px, rest fills)
- J/K move selection up/down in queue
- Enter/click opens detail panel (slides in from right)
- Esc closes detail panel
- 1/2/3 switch variant tabs
- A approve, D dismiss — item slides out of queue, focus moves to next
- R refreshes queue
- Fonts: Plus Jakarta Sans in UI, Instrument Serif in email body, JetBrains Mono in draft body
- Mobile: stack to single column at < 768px (check browser DevTools)

- [ ] **Step 3: TypeScript check**

```powershell
cd emailcopilot-web
npx tsc --noEmit
```
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add emailcopilot-web/src/App.tsx
git commit -m "feat: rewrite App.tsx — split-pane inspector with keyboard navigation"
```

---

## Verification Checklist

After all tasks complete:

- [ ] `dotnet test` passes (all existing NUnit tests green)
- [ ] `npx tsc --noEmit` passes (no TypeScript errors)
- [ ] `npm run dev` serves without console errors
- [ ] Queue list shows urgency dots, subject, from, variant chip, confidence bar
- [ ] Detail panel slides in at 200ms on row click/Enter
- [ ] Original email lazy-fetches and shows skeleton while loading
- [ ] Analysis section shows urgency badge + asks + branches (collapsible)
- [ ] Variant tabs switch without animation (instant)
- [ ] Approve button uses `--accent` copper colour
- [ ] Dismiss ghost button, keyboard hints as `kbd` chips
- [ ] Approve/dismiss removes item from queue with slide-up animation
- [ ] Mobile (< 768px): detail panel is full-screen overlay
- [ ] Skip Audit: items grouped by reason code in collapsible `<details>` sections
- [ ] Run History: timeline rows instead of grid cards
