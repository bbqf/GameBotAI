import { renderHook, act } from '@testing-library/react';
import { usePolling } from '../usePolling';

const setVisibility = (state: 'visible' | 'hidden') => {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
};

describe('usePolling', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    setVisibility('visible');
  });
  afterEach(() => {
    jest.useRealTimers();
    setVisibility('visible');
  });

  it('calls at once and then every interval', () => {
    const cb = jest.fn();
    renderHook(() => usePolling(cb, { intervalMs: 5000, enabled: true }));
    expect(cb).toHaveBeenCalledTimes(1);
    act(() => { jest.advanceTimersByTime(10000); });
    expect(cb).toHaveBeenCalledTimes(3);
  });

  it('does not call while disabled', () => {
    const cb = jest.fn();
    renderHook(() => usePolling(cb, { intervalMs: 5000, enabled: false }));
    act(() => { jest.advanceTimersByTime(20000); });
    expect(cb).not.toHaveBeenCalled();
  });

  it('pauses on a hidden page and calls at once on resume', () => {
    const cb = jest.fn();
    renderHook(() => usePolling(cb, { intervalMs: 5000, enabled: true }));
    cb.mockClear();
    act(() => setVisibility('hidden'));
    act(() => { jest.advanceTimersByTime(20000); });
    expect(cb).not.toHaveBeenCalled();
    act(() => setVisibility('visible'));
    expect(cb).toHaveBeenCalledTimes(1);
    act(() => { jest.advanceTimersByTime(5000); });
    expect(cb).toHaveBeenCalledTimes(2);
  });

  it('calls at once when enabled becomes true', () => {
    const cb = jest.fn();
    const { rerender } = renderHook(({ enabled }) => usePolling(cb, { intervalMs: 5000, enabled }), {
      initialProps: { enabled: false }
    });
    expect(cb).not.toHaveBeenCalled();
    rerender({ enabled: true });
    expect(cb).toHaveBeenCalledTimes(1);
  });

  it('clears the timer on unmount', () => {
    const cb = jest.fn();
    const { unmount } = renderHook(() => usePolling(cb, { intervalMs: 5000, enabled: true }));
    unmount();
    cb.mockClear();
    act(() => { jest.advanceTimersByTime(20000); });
    act(() => setVisibility('visible'));
    expect(cb).not.toHaveBeenCalled();
  });
});
