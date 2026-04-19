import { useEffect, useState } from 'react';
import { getOriginalEmail } from '../api';

type Props = {
  draftId: number;
};

type LoadState = 'idle' | 'loading' | 'loaded' | 'error';

export function OriginalEmailSection({ draftId }: Props) {
  const [open, setOpen] = useState(true);
  const [body, setBody] = useState<string | null>(null);
  const [state, setState] = useState<LoadState>('idle');

  useEffect(() => {
    setState('loading');
    setBody(null);
    getOriginalEmail(draftId)
      .then(data => {
        setBody(data.body);
        setState('loaded');
      })
      .catch(() => setState('error'));
  }, [draftId]);

  return (
    <div className="collapsible">
      <button className="collapsible-header" onClick={() => setOpen(o => !o)}>
        Original Email
        <span className={`collapsible-chevron${open ? ' open' : ''}`}>▼</span>
      </button>
      <div className={`collapsible-body${open ? ' open' : ''}`}>
        <div className="collapsible-inner">
          {state === 'loading' && (
            <>
              <div className="original-email-skeleton" style={{ width: '85%' }} />
              <div className="original-email-skeleton" style={{ width: '70%' }} />
              <div className="original-email-skeleton" style={{ width: '90%' }} />
            </>
          )}
          {state === 'loaded' && body && (
            <div className="original-email-body">{body}</div>
          )}
          {state === 'loaded' && !body && (
            <div className="original-email-fallback">
              Email body not available for this draft. Older drafts may have been created before original-email capture was enabled.
            </div>
          )}
          {state === 'error' && (
            <div className="original-email-fallback">Could not load email body.</div>
          )}
        </div>
      </div>
    </div>
  );
}
