import type { DraftDetail, DraftSummary, PolicyRule, RunRecord, SkippedEmail, WorkflowConfig } from './types';

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    let detail = `${response.status} ${response.statusText}`;
    try {
      const body = await response.json();
      if (body?.detail) {
        detail = String(body.detail);
      } else if (body?.error) {
        detail = String(body.error);
      }
    } catch {
      // ignore
    }

    throw new Error(detail);
  }

  if (response.status === 204) {
    return undefined as T;
  }

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

export async function approveDraft(draftId: number, variantId: number, editedBody?: string): Promise<void> {
  const payload: { variantId: number; editedBody?: string } = { variantId };
  if (editedBody !== undefined) {
    payload.editedBody = editedBody;
  }
  return fetchJson<void>(`/api/drafts/${draftId}/approve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
}

export async function dismissDraft(draftId: number, reason?: string): Promise<void> {
  const hasReason = reason !== undefined && reason.trim().length > 0;
  return fetchJson<void>(`/api/drafts/${draftId}/dismiss`, {
    method: 'POST',
    headers: hasReason ? { 'Content-Type': 'application/json' } : undefined,
    body: hasReason ? JSON.stringify({ reason: reason!.trim() }) : undefined,
  });
}

export async function getSkippedEmails(): Promise<SkippedEmail[]> {
  return fetchJson<SkippedEmail[]>('/api/skipped?skip=0&take=50');
}

export async function getRunHistory(): Promise<RunRecord[]> {
  return fetchJson<RunRecord[]>('/api/runs?skip=0&take=20');
}

export async function getPolicyRules(): Promise<PolicyRule[]> {
  return fetchJson<PolicyRule[]>('/api/policies/rules');
}

export async function setPolicyRule(ruleId: string, enabled: boolean): Promise<void> {
  return fetchJson<void>(`/api/policies/rules/${encodeURIComponent(ruleId)}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled }),
  });
}

export async function getWorkflowConfig(): Promise<WorkflowConfig> {
  return fetchJson<WorkflowConfig>('/api/config/workflow');
}
