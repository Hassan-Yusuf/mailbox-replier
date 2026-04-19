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
    const urgencyA = urgencyOrder[(a.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    const urgencyB = urgencyOrder[(b.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    if (urgencyA !== urgencyB) {
      return urgencyA - urgencyB;
    }

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
            onDismiss={e => {
              e.stopPropagation();
              onDismiss(draft.id);
            }}
          />
        ))}
      </div>
    </div>
  );
}
