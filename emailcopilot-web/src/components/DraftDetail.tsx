import { useState } from 'react';
import type { DraftDetail as DraftDetailType, WorkflowMode } from '../types';
import { OriginalEmailSection } from './OriginalEmailSection';
import { AnalysisSection } from './AnalysisSection';
import { VariantTabs } from './VariantTabs';

type Props = {
  draft: DraftDetailType | null;
  loading: boolean;
  busy: boolean;
  activeVariantIndex: number;
  workflowMode: WorkflowMode;
  editedBodies: Record<number, string>;
  onVariantSelect: (index: number) => void;
  onBodyChange: (variantId: number, body: string) => void;
  onApprove: (variantId: number) => void;
  onDismiss: (reason?: string) => void;
};

const PRESET_REASONS = ['Wrong tone', 'Off-topic', 'Already replied', 'Inaccurate'];

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function DraftDetail({
  draft,
  loading,
  busy,
  activeVariantIndex,
  workflowMode,
  editedBodies,
  onVariantSelect,
  onBodyChange,
  onApprove,
  onDismiss,
}: Props) {
  const suggestOnly = workflowMode === 'SuggestOnly';
  const [pickerOpen, setPickerOpen] = useState(false);
  const [otherReason, setOtherReason] = useState('');

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

  function chooseReason(reason: string) {
    setPickerOpen(false);
    setOtherReason('');
    onDismiss(reason);
  }

  function submitOther() {
    const trimmed = otherReason.trim();
    setPickerOpen(false);
    setOtherReason('');
    onDismiss(trimmed.length > 0 ? trimmed : undefined);
  }

  return (
    <>
      <div className="detail-content">
        <div className="detail-header">
          <h1 className="detail-subject">{draft.subject || '(no subject)'}</h1>
          <div className="detail-from">
            {draft.fromAddress} · {formatDate(draft.sourceReceivedAt)}
            {draft.tier && (
              <>
                {' · '}
                <span className="tier-chip" data-tier={draft.tier}>
                  {draft.tier} confidence
                  {draft.aggregateConfidence != null
                    ? ` (${Math.round(draft.aggregateConfidence * 100)}%)`
                    : ''}
                </span>
              </>
            )}
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
            editedBodies={editedBodies}
            onSelect={onVariantSelect}
            onBodyChange={onBodyChange}
          />
        )}
      </div>

      <div className="action-bar">
        {suggestOnly ? (
          <span className="suggest-only-badge" title="Workflow mode is Suggest Only — approval is disabled.">
            Suggest only
          </span>
        ) : (
          <button
            className="btn-approve"
            disabled={busy || !activeVariant}
            onClick={() => activeVariant && onApprove(activeVariant.id)}
          >
            Approve this draft <kbd style={{ background: 'rgba(255,255,255,0.2)', color: '#fff' }}>Enter</kbd>
          </button>
        )}
        <button
          className="btn-dismiss"
          disabled={busy}
          onClick={() => setPickerOpen(open => !open)}
        >
          Dismiss <kbd>D</kbd>
        </button>
        <div className="kbd-hints" aria-label="Keyboard shortcuts">
          <span className="kbd-group">
            <span className="kbd-label">Queue</span>
            <kbd>J</kbd>
            <kbd>K</kbd>
          </span>
          {draft.variants.length > 1 && (
            <span className="kbd-group">
              <span className="kbd-label">Variants</span>
              {draft.variants.map((_, i) => (
                <kbd key={i}>{i + 1}</kbd>
              ))}
            </span>
          )}
        </div>
      </div>

      {pickerOpen && (
        <div className="reason-picker">
          <div className="reason-picker-label">Why are you dismissing?</div>
          <div className="reason-picker-options">
            {PRESET_REASONS.map(reason => (
              <button
                key={reason}
                className="reason-chip"
                onClick={() => chooseReason(reason)}
                disabled={busy}
              >
                {reason}
              </button>
            ))}
            <button
              className="reason-chip reason-chip-skip"
              onClick={() => chooseReason('')}
              disabled={busy}
              title="Dismiss without recording a reason"
            >
              Skip reason
            </button>
          </div>
          <div className="reason-picker-other">
            <input
              type="text"
              className="reason-other-input"
              placeholder="Other (free text)…"
              value={otherReason}
              onChange={event => setOtherReason(event.target.value)}
              onKeyDown={event => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  submitOther();
                }
              }}
              disabled={busy}
            />
            <button
              className="reason-submit"
              onClick={submitOther}
              disabled={busy || otherReason.trim().length === 0}
            >
              Dismiss
            </button>
          </div>
        </div>
      )}
    </>
  );
}
