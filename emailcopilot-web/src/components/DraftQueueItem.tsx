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
      onKeyDown={e => {
        if (e.key === 'Enter') {
          onClick();
        }
      }}
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
