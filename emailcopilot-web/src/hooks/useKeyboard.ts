import { useEffect } from 'react';

export type KeyHandler = (key: string) => void;

export function useKeyboard(handler: KeyHandler, active: boolean) {
  useEffect(() => {
    if (!active) {
      return;
    }

    function onKeyDown(e: KeyboardEvent) {
      const target = e.target as HTMLElement;
      const tag = target.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || target.isContentEditable) {
        return;
      }

      handler(e.key);
    }

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [handler, active]);
}
