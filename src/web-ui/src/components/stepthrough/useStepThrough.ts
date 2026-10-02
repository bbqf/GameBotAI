import { useCallback, useEffect, useRef, useState } from 'react';
import {
  cancelStep,
  endStepThrough,
  endStepThroughOnClose,
  getStepThrough,
  pauseStepThroughQueue,
  restartStepThrough,
  runNextStep,
  selectStep,
  setStepThroughValues,
  startStepThrough,
  StepThroughError,
  StepThroughHistoryEntryDto,
  StepThroughStateDto,
  StepThroughValuesRequest
} from '../../services/stepThrough';

/** The server keeps at most this many history entries. The view keeps the same number. */
export const HISTORY_CAP = 1000;

export type UseStepThroughOptions = {
  /** Poll interval while no step runs. The read also renews the lease of 90 seconds. */
  pollIdleMs?: number;
  /** Poll interval while a step runs. It gives the status within one second after the step ends. */
  pollRunningMs?: number;
};

/**
 * Joins the state from a read with the state in the view. A read with `afterSeq` has only the newer history
 * entries, so the view adds them to its own list. A read with the full history replaces the list.
 */
export const mergeState = (
  previous: StepThroughStateDto | undefined,
  next: StepThroughStateDto,
  partialHistory: boolean
): StepThroughStateDto => {
  if (!partialHistory || !previous || previous.id !== next.id) return next;
  const lastSeq = previous.history.length > 0 ? previous.history[previous.history.length - 1].seq : 0;
  const added: StepThroughHistoryEntryDto[] = next.history.filter((entry) => entry.seq > lastSeq);
  // An entry that the server changed (for example its log id) replaces the old entry with the same number.
  const updated = new Map(next.history.map((entry) => [entry.seq, entry]));
  const history = [...previous.history.map((entry) => updated.get(entry.seq) ?? entry), ...added];
  return { ...next, history: history.slice(-HISTORY_CAP) };
};

const asError = (error: unknown): StepThroughError =>
  error instanceof StepThroughError ? error : new StepThroughError(500, 'error', (error as Error)?.message ?? 'The request failed.');

const LEASE_LOST_MESSAGE = 'The step-through ended because the view sent no read for 90 seconds. Start it again.';

/** State and actions of one step-through (feature 127). */
export const useStepThrough = (options: UseStepThroughOptions = {}) => {
  const { pollIdleMs = 2000, pollRunningMs = 500 } = options;
  const [state, setState] = useState<StepThroughStateDto | undefined>(undefined);
  const [error, setError] = useState<StepThroughError | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const idRef = useRef<string | undefined>(undefined);
  const lastSeqRef = useRef(0);

  const id = state?.id;
  const running = state?.state === 'running';

  useEffect(() => {
    idRef.current = id;
  }, [id]);

  useEffect(() => {
    const entries = state?.history ?? [];
    lastSeqRef.current = entries.length > 0 ? entries[entries.length - 1].seq : 0;
  }, [state?.history]);

  // Poll the state. The read renews the lease, and it shows the end of a running step.
  useEffect(() => {
    if (!id) return undefined;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const tick = async () => {
      try {
        const next = await getStepThrough(id, lastSeqRef.current);
        if (cancelled) return;
        setState((previous) => mergeState(previous, next, true));
      } catch (caught) {
        if (cancelled) return;
        const failure = asError(caught);
        if (failure.code === 'step_through_not_found') {
          idRef.current = undefined;
          setState(undefined);
          setError(new StepThroughError(404, failure.code, LEASE_LOST_MESSAGE));
          return;
        }
        // A short failure of the network does not end the step-through. The next read tries again.
      }
      if (!cancelled) timer = setTimeout(tick, running ? pollRunningMs : pollIdleMs);
    };
    timer = setTimeout(tick, running ? pollRunningMs : pollIdleMs);
    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
  }, [id, running, pollIdleMs, pollRunningMs]);

  // End the step-through when the view closes (FR-012b): at unmount and when the page closes.
  useEffect(() => {
    const onPageHide = () => {
      if (idRef.current) endStepThroughOnClose(idRef.current);
    };
    window.addEventListener('pagehide', onPageHide);
    return () => {
      window.removeEventListener('pagehide', onPageHide);
      const current = idRef.current;
      if (current) {
        idRef.current = undefined;
        void endStepThrough(current).catch(() => undefined);
      }
    };
  }, []);

  const perform = useCallback(async (action: () => Promise<StepThroughStateDto>, partialHistory = false) => {
    setBusy(true);
    setError(undefined);
    try {
      const next = await action();
      setState((previous) => mergeState(previous, next, partialHistory));
      return next;
    } catch (caught) {
      setError(asError(caught));
      return undefined;
    } finally {
      setBusy(false);
    }
  }, []);

  const start = useCallback(
    (sequenceId: string, gameSessionId: string, extra: { startPath?: string; parameterValues?: Record<string, string> } = {}) =>
      perform(() => startStepThrough({ sequenceId, gameSessionId, ...extra })),
    [perform]
  );

  const runNext = useCallback(() => (id ? perform(() => runNextStep(id)) : Promise.resolve(undefined)), [id, perform]);

  const select = useCallback((path: string) => (id ? perform(() => selectStep(id, path)) : Promise.resolve(undefined)), [id, perform]);

  const cancel = useCallback(() => (id ? perform(() => cancelStep(id)) : Promise.resolve(undefined)), [id, perform]);

  const restart = useCallback(() => (id ? perform(() => restartStepThrough(id)) : Promise.resolve(undefined)), [id, perform]);

  const setValues = useCallback(
    (values: StepThroughValuesRequest) => (id ? perform(() => setStepThroughValues(id, values)) : Promise.resolve(undefined)),
    [id, perform]
  );

  const pauseQueue = useCallback(() => (id ? perform(() => pauseStepThroughQueue(id)) : Promise.resolve(undefined)), [id, perform]);

  const end = useCallback(async () => {
    const current = idRef.current;
    idRef.current = undefined;
    setState(undefined);
    setError(undefined);
    if (current) {
      try {
        await endStepThrough(current);
      } catch (caught) {
        setError(asError(caught));
      }
    }
  }, []);

  const clearError = useCallback(() => setError(undefined), []);

  return { state, error, busy, start, runNext, select, cancel, restart, setValues, pauseQueue, end, clearError };
};
