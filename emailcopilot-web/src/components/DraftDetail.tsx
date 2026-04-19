import type { DraftDetail as DraftDetailType } from '../types';
import { OriginalEmailSection } from './OriginalEmailSection';
import { AnalysisSection } from './AnalysisSection';
import { VariantTabs } from './VariantTabs';

type Props = {
  draft: DraftDetailType | null;
  loading: boolean;
  busy: boolean;
  activeVariantIndex: number;
  onVariantSelect: (index: number) => void;
  onApprove: (variantId: number) => void;
  onDismiss: () => void;
};

function formatDate(value: string) {
  return new Date(value).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

export function DraftDetail({
  draft,
  loading,
  busy,
  activeVariantIndex,
  onVariantSelect,
  onApprove,
  onDismiss,
}: Props) {
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
            onSelect={onVariantSelect}
          />
        )}
      </div>

      <div className="action-bar">
        <button
          className="btn-approve"
          disabled={busy || !activeVariant}
          onClick={() => activeVariant && onApprove(activeVariant.id)}
        >
          Approve this draft <kbd style={{ background: 'rgba(255,255,255,0.2)', color: '#fff' }}>Enter</kbd>
        </button>
        <button className="btn-dismiss" disabled={busy} onClick={onDismiss}>
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
    </>
  );
}
