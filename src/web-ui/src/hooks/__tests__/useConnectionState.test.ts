import { act, renderHook } from '@testing-library/react';
import { HEALTH_POLL_MS, useConnectionState } from '../useConnectionState';

const okResponse = { ok: true, status: 200 } as Response;

const setOnLine = (value: boolean) => {
  Object.defineProperty(window.navigator, 'onLine', { configurable: true, value });
};

describe('useConnectionState', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    jest.useFakeTimers();
    setOnLine(true);
  });

  afterEach(() => {
    jest.useRealTimers();
    global.fetch = originalFetch;
    setOnLine(true);
  });

  const flush = async (ms = 0) => { await act(async () => { await jest.advanceTimersByTimeAsync(ms); }); };

  it('is online when the health call works', async () => {
    global.fetch = jest.fn().mockResolvedValue(okResponse) as unknown as typeof fetch;
    const { result } = renderHook(() => useConnectionState());
    await flush();
    expect(result.current).toBe('online');
  });

  it('gives lost on an offline event', async () => {
    global.fetch = jest.fn().mockResolvedValue(okResponse) as unknown as typeof fetch;
    const { result } = renderHook(() => useConnectionState());
    await flush();
    setOnLine(false);
    await act(async () => { window.dispatchEvent(new Event('offline')); });
    expect(result.current).toBe('lost');
  });

  it('gives lost when the health call fails', async () => {
    global.fetch = jest.fn().mockRejectedValue(new Error('down')) as unknown as typeof fetch;
    const { result } = renderHook(() => useConnectionState());
    await flush();
    expect(result.current).toBe('lost');
  });

  it('gives lost when the health call returns an error status', async () => {
    global.fetch = jest.fn().mockResolvedValue({ ok: false, status: 503 }) as unknown as typeof fetch;
    const { result } = renderHook(() => useConnectionState());
    await flush();
    expect(result.current).toBe('lost');
  });

  it('returns to online after a good call and runs the reload callback once', async () => {
    const fetchMock = jest.fn().mockRejectedValue(new Error('down'));
    global.fetch = fetchMock as unknown as typeof fetch;
    const onReconnect = jest.fn();
    const { result } = renderHook(() => useConnectionState(onReconnect));
    await flush();
    expect(result.current).toBe('lost');
    expect(onReconnect).not.toHaveBeenCalled();

    fetchMock.mockResolvedValue(okResponse);
    await flush(HEALTH_POLL_MS);
    expect(result.current).toBe('online');
    expect(onReconnect).toHaveBeenCalledTimes(1);

    await flush(HEALTH_POLL_MS * 2);
    expect(onReconnect).toHaveBeenCalledTimes(1);
  });

  it('sends only GET /health calls (no action is sent again)', async () => {
    const fetchMock = jest.fn().mockResolvedValue(okResponse);
    global.fetch = fetchMock as unknown as typeof fetch;
    renderHook(() => useConnectionState());
    await flush(HEALTH_POLL_MS);
    for (const call of fetchMock.mock.calls) {
      expect(String(call[0])).toContain('/health');
      expect(call[1]?.method).toBe('GET');
    }
  });
});
