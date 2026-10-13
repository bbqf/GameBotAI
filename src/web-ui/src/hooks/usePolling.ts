import { useEffect, useRef } from 'react';

export type UsePollingOptions = {
  intervalMs: number;
  enabled: boolean;
};

/**
 * Runs `callback` every `intervalMs` while `enabled` is true and the page is visible.
 * When the page becomes visible again, or `enabled` becomes true, it runs the callback at once.
 */
export const usePolling = (callback: () => void, { intervalMs, enabled }: UsePollingOptions): void => {
  const callbackRef = useRef(callback);
  callbackRef.current = callback;

  useEffect(() => {
    if (!enabled) return;
    let timer: ReturnType<typeof setInterval> | null = null;
    const isHidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';

    const stop = () => {
      if (timer !== null) {
        clearInterval(timer);
        timer = null;
      }
    };
    const start = () => {
      stop();
      timer = setInterval(() => callbackRef.current(), intervalMs);
    };
    const onVisibility = () => {
      if (isHidden()) {
        stop();
      } else {
        callbackRef.current();
        start();
      }
    };

    if (!isHidden()) {
      callbackRef.current();
      start();
    }
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      stop();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [enabled, intervalMs]);
};
