import { useEffect, useRef } from 'react';

export type UseSequentialPollingOptions = {
  /** The time from the start of one request to the start of the next request, in milliseconds. */
  intervalMs: number;
  enabled: boolean;
  /** Change this value to abort the open request and start a new loop at once (for example a new session id). */
  resetKey?: string | null;
};

/**
 * Runs `task` again and again, one run at a time (feature 141, FR-005 and FR-006).
 * The next run starts `intervalMs` after the start of the last run, or at once when the last run took longer.
 * It never starts a run while one is open. This is different from `usePolling`, which uses a fixed timer
 * and can overlap two slow requests.
 * - The first run starts at once.
 * - When the page is hidden, no new run starts. One run starts at once when the page is visible again.
 * - The signal of the open run is aborted when `resetKey` changes, `enabled` becomes false, or the component unmounts.
 */
export const useSequentialPolling = (
  task: (signal: AbortSignal) => Promise<void>,
  { intervalMs, enabled, resetKey = null }: UseSequentialPollingOptions
): void => {
  const taskRef = useRef(task);
  taskRef.current = task;

  useEffect(() => {
    if (!enabled) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | null = null;
    let running = false;
    let disposed = false;
    let startedAt = 0;

    const isHidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';
    const clearTimer = () => {
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
    };

    const schedule = () => {
      clearTimer();
      if (disposed || isHidden()) return;
      const wait = Math.max(0, intervalMs - (Date.now() - startedAt));
      timer = setTimeout(() => { void run(); }, wait);
    };

    const run = async () => {
      timer = null;
      if (disposed || running || isHidden()) return;
      running = true;
      startedAt = Date.now();
      try {
        await taskRef.current(controller.signal);
      } catch {
        // The task shows its own errors. A failed run does not stop the loop.
      } finally {
        running = false;
        if (!disposed) schedule();
      }
    };

    const onVisibility = () => {
      if (isHidden()) {
        clearTimer();
      } else if (!running) {
        void run();
      }
    };

    document.addEventListener('visibilitychange', onVisibility);
    void run();

    return () => {
      disposed = true;
      clearTimer();
      controller.abort();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [enabled, intervalMs, resetKey]);
};
