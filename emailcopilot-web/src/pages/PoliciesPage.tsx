import { useEffect, useState } from 'react';
import type { PolicyRule } from '../types';
import { getPolicyRules, setPolicyRule } from '../api';

function humanize(value: string) {
  return value.replace(/_/g, ' ').toLowerCase().replace(/\b\w/g, (letter: string) => letter.toUpperCase());
}

export function PoliciesPage() {
  const [rules, setRules] = useState<PolicyRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingRuleId, setSavingRuleId] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    getPolicyRules()
      .then(setRules)
      .catch(err => setError(err instanceof Error ? err.message : 'Could not load rules.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleToggle(rule: PolicyRule) {
    if (!rule.isUserConfigurable || savingRuleId) {
      return;
    }

    const nextEnabled = !rule.currentlyEnabled;
    setSavingRuleId(rule.ruleId);
    setError(null);

    setRules(prev => prev.map(r => r.ruleId === rule.ruleId ? { ...r, currentlyEnabled: nextEnabled } : r));

    try {
      await setPolicyRule(rule.ruleId, nextEnabled);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update rule.');
      setRules(prev => prev.map(r => r.ruleId === rule.ruleId ? { ...r, currentlyEnabled: !nextEnabled } : r));
    } finally {
      setSavingRuleId(null);
    }
  }

  const byCategory = rules.reduce<Record<string, PolicyRule[]>>((acc, rule) => {
    (acc[rule.category] ??= []).push(rule);
    return acc;
  }, {});

  return (
    <div className="page-content">
      <h1 className="page-title">Rule Policies</h1>
      {error && <div className="status-banner error">{error}</div>}
      {loading && <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>Loading…</div>}
      {!loading && rules.length === 0 && (
        <div style={{ color: 'var(--text-secondary)', fontSize: 13 }}>No rules to configure.</div>
      )}
      {Object.entries(byCategory).map(([category, group]) => (
        <details key={category} open style={{ marginBottom: 16 }}>
          <summary style={{ cursor: 'pointer', fontWeight: 600, fontSize: 14, marginBottom: 8 }}>
            {humanize(category)} ({group.length})
          </summary>
          <table className="data-table">
            <thead>
              <tr>
                <th>Rule</th>
                <th>Description</th>
                <th style={{ whiteSpace: 'nowrap', textAlign: 'right' }}>Enabled</th>
              </tr>
            </thead>
            <tbody>
              {group.map(rule => {
                const locked = !rule.isUserConfigurable;
                const saving = savingRuleId === rule.ruleId;
                return (
                  <tr key={rule.ruleId}>
                    <td style={{ fontWeight: 500 }}>{rule.displayName}</td>
                    <td style={{ color: 'var(--text-secondary)', fontSize: 13 }}>{rule.description}</td>
                    <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                      {locked ? (
                        <span
                          title="Safety rule — not user-configurable"
                          style={{ color: 'var(--text-secondary)', fontSize: 12 }}
                        >
                          🔒 Always on
                        </span>
                      ) : (
                        <label style={{ display: 'inline-flex', alignItems: 'center', gap: 8, cursor: saving ? 'wait' : 'pointer' }}>
                          <input
                            type="checkbox"
                            checked={rule.currentlyEnabled}
                            disabled={saving}
                            onChange={() => void handleToggle(rule)}
                          />
                          <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                            {rule.currentlyEnabled ? 'On' : 'Off'}
                          </span>
                        </label>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </details>
      ))}
    </div>
  );
}
