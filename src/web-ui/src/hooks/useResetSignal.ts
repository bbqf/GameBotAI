import { useEffect, useRef } from 'react';

/**
 * Runs `handler` whenever `signal` changes to a new value (skipping the initial
 * mount). Used to let a parent nudge a page back to its list view when the user
 * re-clicks the already-active navigation tab/area: the parent bumps a counter,
 * and the mounted page reacts by closing its editor (honoring unsaved changes).
 */
export const useResetSignal = (signal: number | undefined, handler: () => void) => {
  const prev = useRef(signal);
  useEffect(() => {
    if (signal === undefined) return;
    if (prev.current === signal) return;
    prev.current = signal;
    handler();
    // handler is intentionally not a dependency: we react only to signal changes,
    // using the handler closure from the render in which the change was observed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signal]);
};
