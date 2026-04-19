import { useState } from 'react';
import type { EmailAnalysis } from '../types';

type Props = {
  analysis: EmailAnalysis;
};

function humanize(value: string) {
  return value.replaceAll('_', ' ').toLowerCase().replace(/\b\w/g, letter => letter.toUpperCase());
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
