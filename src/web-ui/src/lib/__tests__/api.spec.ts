import { ApiError, buildApiUrl, buildAuthHeaders, getJson } from '../api';
import { setBaseUrl } from '../config';
import { token$ } from '../token';

describe('api helpers', () => {
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

  beforeEach(() => {
    token$.set(null);
    setBaseUrl('');
  });

  it('builds api urls with or without base', () => {
    setBaseUrl('');
    expect(buildApiUrl('/api/test')).toBe('/api/test');

    setBaseUrl('http://localhost');
    expect(buildApiUrl('/api/test')).toBe('http://localhost/api/test');

    setBaseUrl('http://localhost/');
    expect(buildApiUrl('/api/test')).toBe('http://localhost/api/test');
  });

  it('builds auth headers with token', () => {
    token$.set('token-1');

    expect(buildAuthHeaders()).toEqual({
      'Content-Type': 'application/json',
      Authorization: 'Bearer token-1'
    });
    expect(buildAuthHeaders(false)).toEqual({ Authorization: 'Bearer token-1' });
  });

  it('returns json for ok responses', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ ok: true })
    } as Response);

    await expect(getJson<{ ok: boolean }>('/api/test')).resolves.toEqual({ ok: true });

    fetchSpy.mockRestore();
  });

  it('returns undefined for 204 responses', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      status: 204,
      json: async () => ({})
    } as Response);

    await expect(getJson('/api/empty')).resolves.toBeUndefined();

    fetchSpy.mockRestore();
  });

  it('throws validation ApiError with mapped errors', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: false,
      status: 400,
      json: async () => ({ errors: [{ field: 'name', message: 'Required' }] })
    } as Response);

    await expect(getJson('/api/invalid')).rejects.toEqual(
      expect.objectContaining({
        status: 400,
        errors: [{ field: 'name', message: 'Required' }]
      })
    );

    fetchSpy.mockRestore();
  });

  it('throws ApiError with server message when available', async () => {
    const fetchSpy = jest.spyOn(global, 'fetch').mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({ error: { message: 'Boom' } })
    } as Response);

    await expect(getJson('/api/fail')).rejects.toBeInstanceOf(ApiError);
    await expect(getJson('/api/fail')).rejects.toEqual(expect.objectContaining({ message: 'Boom' }));

    fetchSpy.mockRestore();
  });
});
