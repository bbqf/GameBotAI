import { useEffect, useRef, useState } from 'react';
import { buildApiUrl } from '../lib/api';

export type ConnectionState = 'online' | 'lost';

export const HEALTH_POLL_MS = 10_000;

const initialState = (): ConnectionState =>
  typeof navigator !== 'undefined' && navigator.onLine === false ? 'lost' : 'online';

const probeHealth = async (): Promise<boolean> => {
  try {
    const res = await fetch(buildApiUrl('/health'), { method: 'GET', cache: 'no-store' });
    return !!res && res.ok;
  } catch {
    return false;
  }
};

/**
 * Tracks whether the browser reaches the service (contract C-09).
 * Uses the browser online state and a `GET /health` call every 10 s while the page is visible.
 * When `enabled` is false the hook makes no call. `onReconnect` runs when the state changes from `lost` to `online`. The hook never repeats an action.
 */
export const useConnectionState = (onReconnect?: () => void, enabled: boolean = true): ConnectionState => {
  const [state, setState] = useState<ConnectionState>(initialState);
  const stateRef = useRef<ConnectionState>(state);
  const reconnectRef = useRef(onReconnect);
  reconnectRef.current = onReconnect;

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;

    const apply = (next: ConnectionState) => {
      if (disposed) return;
      const previous = stateRef.current;
      if (previous === next) return;
      stateRef.current = next;
      setState(next);
      if (previous === 'lost' && next === 'online') reconnectRef.current?.();
    };

    const check = async () => {
      if (typeof navigator !== 'undefined' && navigator.onLine === false) {
        apply('lost');
        return;
      }
      apply((await probeHealth()) ? 'online' : 'lost');
    };

    const onOffline = () => apply('lost');
    const onOnline = () => { void check(); };
    window.addEventListener('offline', onOffline);
    window.addEventListener('online', onOnline);

    const timer = setInterval(() => {
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return;
      void check();
    }, HEALTH_POLL_MS);
    void check();

    return () => {
      disposed = true;
      window.removeEventListener('offline', onOffline);
      window.removeEventListener('online', onOnline);
      clearInterval(timer);
    };
  }, [enabled]);

  return state;
};
