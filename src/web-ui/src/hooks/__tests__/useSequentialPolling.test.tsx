import { renderHook, act } from '@testing-library/react';
import { useSequentialPolling } from '../useSequentialPolling';

const setVisibility = (state: 'visible' | 'hidden') => {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
};

const advance = async (ms: number) => {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
};

/** A task whose runs end only when the test says so. */
const makeControlledTask = () => {
  const resolvers: Array<() => void> = [];
  const signals: AbortSignal[] = [];
  const task = jest.fn((signal: AbortSignal) => {
    signals.push(signal);
    return new Promise<void>((resolve) => { resolvers.push(resolve); });
  });
  return { task, resolvers, signals };
};

describe('useSequentialPolling (feature 141, FR-024)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    setVisibility('visible');
  });
  afterEach(() => {
    jest.useRealTimers();
    setVisibility('visible');
  });

  it('starts the first run at once and the next run 2 s after the start of the last run', async () => {
    const task = jest.fn().mockResolvedValue(undefined);
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    expect(task).toHaveBeenCalledTimes(1);
    await advance(1900);
    expect(task).toHaveBeenCalledTimes(1);
    await advance(100);
    expect(task).toHaveBeenCalledTimes(2);
    await advance(2000);
    expect(task).toHaveBeenCalledTimes(3);
  });

  it('counts the 2 s from the start of the last run, not from its end', async () => {
    let finish: () => void = () => undefined;
    const task = jest.fn().mockImplementationOnce(() => new Promise<void>((resolve) => { finish = resolve; }))
      .mockResolvedValue(undefined);
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    await advance(1500);
    await act(async () => { finish(); });
    await advance(400);
    expect(task).toHaveBeenCalledTimes(1);
    await advance(100);
    expect(task).toHaveBeenCalledTimes(2);
  });

  it('starts no run while one is open, and starts the next run at once when a slow run ends', async () => {
    const { task, resolvers } = makeControlledTask();
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    await advance(10000);
    expect(task).toHaveBeenCalledTimes(1);
    await act(async () => { resolvers[0](); });
    await advance(0);
    expect(task).toHaveBeenCalledTimes(2);
  });

  it('keeps the loop alive after a failed run', async () => {
    const task = jest.fn().mockRejectedValueOnce(new Error('boom')).mockResolvedValue(undefined);
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    await advance(2000);
    expect(task).toHaveBeenCalledTimes(2);
  });

  it('aborts the open run and starts a new run at once when the reset key changes', async () => {
    const { task, signals } = makeControlledTask();
    const { rerender } = renderHook(
      ({ id }) => useSequentialPolling(task, { intervalMs: 2000, enabled: true, resetKey: id }),
      { initialProps: { id: 'a' } }
    );
    await advance(0);
    expect(signals[0].aborted).toBe(false);
    rerender({ id: 'b' });
    await advance(0);
    expect(signals[0].aborted).toBe(true);
    expect(task).toHaveBeenCalledTimes(2);
    expect(signals[1].aborted).toBe(false);
  });

  it('aborts the open run and stops on unmount', async () => {
    const { task, signals, resolvers } = makeControlledTask();
    const { unmount } = renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    unmount();
    expect(signals[0].aborted).toBe(true);
    await act(async () => { resolvers[0](); });
    await advance(10000);
    expect(task).toHaveBeenCalledTimes(1);
  });

  it('does not run while disabled', async () => {
    const task = jest.fn().mockResolvedValue(undefined);
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: false }));
    await advance(10000);
    expect(task).not.toHaveBeenCalled();
  });

  it('pauses on a hidden page and sends one run at once on resume', async () => {
    const task = jest.fn().mockResolvedValue(undefined);
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    task.mockClear();
    await act(async () => { setVisibility('hidden'); });
    await advance(20000);
    expect(task).not.toHaveBeenCalled();
    await act(async () => { setVisibility('visible'); });
    await advance(0);
    expect(task).toHaveBeenCalledTimes(1);
    await advance(2000);
    expect(task).toHaveBeenCalledTimes(2);
  });

  it('sends no second run on resume when a run is still open', async () => {
    const { task, resolvers } = makeControlledTask();
    renderHook(() => useSequentialPolling(task, { intervalMs: 2000, enabled: true }));
    await advance(0);
    await act(async () => { setVisibility('hidden'); });
    await act(async () => { setVisibility('visible'); });
    await advance(0);
    expect(task).toHaveBeenCalledTimes(1);
    await act(async () => { resolvers[0](); });
    await advance(2000);
    expect(task).toHaveBeenCalledTimes(2);
  });
});
