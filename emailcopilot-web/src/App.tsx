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
  const [activeVariantIndex, setActiveVariantIndex] = useState(0);

  const [busy, setBusy] = useState(false);
  const [exitingIds, setExitingIds] = useState<Set<number>>(new Set());

  const sortedSummaries = [...summaries].sort((a, b) => {
    const order: Record<string, number> = { HIGH: 0, MEDIUM: 1, LOW: 2, UNKNOWN: 3 };
    const urgencyA = order[(a.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    const urgencyB = order[(b.urgency ?? 'UNKNOWN').toUpperCase()] ?? 3;
    if (urgencyA !== urgencyB) {
      return urgencyA - urgencyB;
    }

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

  useEffect(() => {
    void loadQueue();
  }, [loadQueue]);

  const selectDraft = useCallback(async (id: number) => {
    setSelectedId(id);
    setDetailLoading(true);
    setDetail(null);
    setActiveVariantIndex(0);
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
    await new Promise(resolve => setTimeout(resolve, 210));
    setSummaries(prev => prev.filter(summary => summary.id !== id));
    setExitingIds(prev => {
      const next = new Set(prev);
      next.delete(id);
      return next;
    });

    if (selectedId === id) {
      setSelectedId(null);
      setDetail(null);
      const idx = sortedSummaries.findIndex(summary => summary.id === id);
      const next = sortedSummaries[idx + 1] ?? sortedSummaries[idx - 1];
      if (next) {
        void selectDraft(next.id);
      }
    }
  }

  async function handleApprove(variantId: number) {
    if (!selectedId || busy) {
      return;
    }

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
    if (busy) {
      return;
    }

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
    const idx = sortedSummaries.findIndex(summary => summary.id === selectedId);

    switch (key) {
      case 'j':
      case 'J':
      case 'ArrowDown': {
        const next = sortedSummaries[idx + 1] ?? sortedSummaries[0];
        if (next) {
          void selectDraft(next.id);
        }
        break;
      }

      case 'k':
      case 'K':
      case 'ArrowUp': {
        const prev = sortedSummaries[idx - 1] ?? sortedSummaries[sortedSummaries.length - 1];
        if (prev) {
          void selectDraft(prev.id);
        }
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
        const activeVariant = detail?.variants[activeVariantIndex] ?? detail?.variants[0];
        if (activeVariant) {
          void handleApprove(activeVariant.id);
        }
        break;
      }

      case '1':
      case '2':
      case '3': {
        if (detail) {
          const nextIndex = Number(key) - 1;
          if (nextIndex >= 0 && nextIndex < detail.variants.length) {
            setActiveVariantIndex(nextIndex);
          }
        }
        break;
      }

      case 'd':
      case 'D': {
        if (selectedId) {
          void handleDismiss(selectedId);
        }
        break;
      }

      case 'r':
      case 'R': {
        void loadQueue();
        break;
      }
    }
  }, [sortedSummaries, selectedId, detail, activeVariantIndex, loadQueue, selectDraft]);

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
          activeVariantIndex={activeVariantIndex}
          onVariantSelect={setActiveVariantIndex}
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
