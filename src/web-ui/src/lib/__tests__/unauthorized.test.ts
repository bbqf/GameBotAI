import { act, renderHook } from '@testing-library/react';
import { getJson, apiGet } from '../api';
import { clearUnauthorized, isUnauthorized, useUnauthorized } from '../unauthorized';

const respond = (status: number, body: unknown = {}) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

describe('401 detection', () => {
  const originalFetch = global.fetch;
  beforeEach(() => clearUnauthorized());
  afterEach(() => { global.fetch = originalFetch; });

  it('sets the flag after getJson gets a 401', async () => {
    global.fetch = jest.fn().mockResolvedValue(respond(401)) as unknown as typeof fetch;
    await expect(getJson('/api/queues')).rejects.toMatchObject({ status: 401 });
    expect(isUnauthorized()).toBe(true);
  });

  it('sets the flag after apiGet gets a 401', async () => {
    global.fetch = jest.fn().mockResolvedValue(respond(401)) as unknown as typeof fetch;
    await apiGet('/api/games');
    expect(isUnauthorized()).toBe(true);
  });

  it('does not set the flag for other errors or success', async () => {
    global.fetch = jest.fn().mockResolvedValue(respond(500)) as unknown as typeof fetch;
    await expect(getJson('/api/queues')).rejects.toBeTruthy();
    global.fetch = jest.fn().mockResolvedValue(respond(200, [])) as unknown as typeof fetch;
    await getJson('/api/queues');
    expect(isUnauthorized()).toBe(false);
  });

  it('updates the hook and clears', async () => {
    const { result } = renderHook(() => useUnauthorized());
    expect(result.current).toBe(false);
    global.fetch = jest.fn().mockResolvedValue(respond(401)) as unknown as typeof fetch;
    await act(async () => { await apiGet('/api/games'); });
    expect(result.current).toBe(true);
    act(() => clearUnauthorized());
    expect(result.current).toBe(false);
  });
});
