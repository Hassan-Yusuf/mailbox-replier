import { useEffect, useState } from 'react';
import type { SkippedEmail } from '../types';
import { getSkippedEmails } from '../api';

function humanize(value: string) {
  return value.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, letter => letter.toUpperCase());
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
