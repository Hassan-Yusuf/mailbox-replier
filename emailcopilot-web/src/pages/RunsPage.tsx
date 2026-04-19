import { useEffect, useState } from 'react';
import type { RunRecord } from '../types';
import { getRunHistory } from '../api';

function humanize(value: string) {
  return value.replace(/_/g, ' ').toLowerCase().replace(/\b\w/g, (letter: string) => letter.toUpperCase());
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
