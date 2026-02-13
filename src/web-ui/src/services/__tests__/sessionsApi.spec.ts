import { ApiError } from '../../lib/api';
import { createSession, getRunningSessions, startSession, stopSession } from '../sessionsApi';

describe('sessionsApi', () => {
  const originalFetch = global.fetch;

  beforeAll(() => {
    if (!global.fetch) {
      (global as any).fetch = jest.fn();
    }
  });

  afterAll(() => {
    if (!originalFetch) {
      delete (global as any).fetch;
    } else {
      global.fetch = originalFetch;
    }
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('returns running sessions on ok response', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ sessions: [{ sessionId: 's1', gameId: 'g1', emulatorId: 'e1', startedAtUtc: '', lastHeartbeatUtc: '', status: 'Running' }] })
    } as Response);

    await expect(getRunningSessions(1000)).resolves.toHaveLength(1);

    fetchSpy.mockRestore();
  });

  it('throws ApiError when running sessions fails', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({ message: 'Boom' })
    } as Response);

    await expect(getRunningSessions(1000)).rejects.toEqual(expect.objectContaining({ status: 500, message: 'Boom' }));

    fetchSpy.mockRestore();
  });

  it('creates sessions and handles validation errors', async () => {
    const okFetch = jest.spyOn(global, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ sessionId: 's1' })
    } as Response);

    await expect(createSession({ gameId: 'g1', adbSerial: 'emu-1' }, 1000)).resolves.toEqual({ sessionId: 's1' });

    okFetch.mockResolvedValueOnce({
      ok: false,
      status: 400,
      json: async () => ({ errors: [{ field: 'gameId', message: 'Required' }] })
    } as Response);

    await expect(createSession({ gameId: 'g1', adbSerial: 'emu-1' }, 1000)).rejects.toBeInstanceOf(ApiError);

    okFetch.mockResolvedValueOnce({
      ok: false,
      status: 504,
      json: async () => ({ message: 'Timeout' })
    } as Response);

    await expect(createSession({ gameId: 'g1', adbSerial: 'emu-1' }, 1000)).rejects.toEqual(
      expect.objectContaining({ status: 504, message: 'Session creation timed out' })
    );

    okFetch.mockRejectedValueOnce(new Error('Network down'));
    await expect(createSession({ gameId: 'g1', adbSerial: 'emu-1' }, 1000)).rejects.toEqual(
      expect.objectContaining({ status: 500 })
    );

    okFetch.mockRestore();
  });

  it('starts and stops sessions with expected errors', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch');
    fetchSpy
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ sessionId: 's1', runningSessions: [] })
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: async () => ({ error: { message: 'Missing' } })
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 429,
        json: async () => ({})
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: async () => ({})
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({})
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: async () => ({})
      } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 504,
        json: async () => ({})
      } as Response);

    await expect(startSession({ gameId: 'g1', emulatorId: 'e1' }, 1000)).resolves.toEqual({ sessionId: 's1', runningSessions: [] });
    await expect(startSession({ gameId: 'g1', emulatorId: 'e1' }, 1000)).rejects.toEqual(expect.objectContaining({ status: 404 }));
    await expect(startSession({ gameId: 'g1', emulatorId: 'e1' }, 1000)).rejects.toEqual(expect.objectContaining({ status: 429 }));
    await expect(startSession({ gameId: 'g1', emulatorId: 'e1' }, 1000)).rejects.toEqual(expect.objectContaining({ status: 400 }));
    await expect(stopSession('s1', 1000)).resolves.toBe(true);
    await expect(stopSession('s1', 1000)).rejects.toEqual(expect.objectContaining({ status: 404 }));
    await expect(stopSession('s1', 1000)).rejects.toEqual(expect.objectContaining({ status: 504 }));

    fetchSpy.mockRestore();
  });

  it('maps abort errors to timeout responses', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockRejectedValue({ name: 'AbortError' });

    await expect(getRunningSessions(1)).rejects.toEqual(expect.objectContaining({ status: 504 }));

    fetchSpy.mockRestore();
  });
});
