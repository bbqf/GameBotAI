import * as api from '../../lib/api';
import * as passkey from '../../lib/passkey';
import {
  endAllSessions,
  getAuthStatus,
  listPasskeys,
  registerPasskey,
  removePasskey,
  renamePasskey,
  signInWithPasskey,
  signOut
} from '../authApi';

jest.mock('../../lib/api');
jest.mock('../../lib/passkey');

const response = (status: number, body?: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  json: async () => body
}) as unknown as Response;

const mockedApi = api as jest.Mocked<typeof api>;
const mockedPasskey = passkey as jest.Mocked<typeof passkey>;

afterEach(() => jest.resetAllMocks());

describe('authApi', () => {
  it('reads the status', async () => {
    mockedApi.apiGet.mockResolvedValue(response(200, { signInEnabled: true, signedIn: false }));
    const result = await getAuthStatus();
    expect(result.status?.signInEnabled).toBe(true);
    expect(mockedApi.apiGet).toHaveBeenCalledWith('/api/auth/status');
  });

  it('gives the code of a refusal', async () => {
    mockedApi.apiGet.mockResolvedValue(response(403, { error: { code: 'remote_access_refused' } }));
    const result = await getAuthStatus();
    expect(result).toEqual({ status: null, httpStatus: 403, code: 'remote_access_refused' });
  });

  it('signs in with a passkey', async () => {
    mockedApi.apiPost
      .mockResolvedValueOnce(response(200, { ceremonyId: 'c1', publicKey: { challenge: 'x' } }))
      .mockResolvedValueOnce(response(204));
    mockedPasskey.getPasskey.mockResolvedValue({ id: 'a' } as any);
    await expect(signInWithPasskey()).resolves.toBe('signedIn');
    expect(mockedApi.apiPost).toHaveBeenLastCalledWith('/api/auth/sign-in/complete', { ceremonyId: 'c1', credential: { id: 'a' } });
  });

  it('gives cancelled when the owner cancels and failed after a refusal', async () => {
    mockedApi.apiPost.mockResolvedValue(response(200, { ceremonyId: 'c1', publicKey: { challenge: 'x' } }));
    mockedPasskey.getPasskey.mockResolvedValue(null);
    await expect(signInWithPasskey()).resolves.toBe('cancelled');
    mockedApi.apiPost.mockResolvedValueOnce(response(401));
    await expect(signInWithPasskey()).resolves.toBe('failed');
  });

  it('registers a passkey with the code and the trimmed name', async () => {
    mockedApi.apiPost
      .mockResolvedValueOnce(response(200, { ceremonyId: 'c2', publicKey: { challenge: 'x' } }))
      .mockResolvedValueOnce(response(204));
    mockedPasskey.createPasskey.mockResolvedValue({ id: 'n' } as any);
    await expect(registerPasskey('ABCD-EFGH', ' My phone ')).resolves.toBe('registered');
    expect(mockedApi.apiPost).toHaveBeenNthCalledWith(1, '/api/auth/register/options', { code: 'ABCD-EFGH' });
    expect(mockedApi.apiPost).toHaveBeenNthCalledWith(2, '/api/auth/register/complete', {
      ceremonyId: 'c2', name: 'My phone', credential: { id: 'n' }
    });
  });

  it('gives failed for a wrong code', async () => {
    mockedApi.apiPost.mockResolvedValue(response(401));
    await expect(registerPasskey('x', '')).resolves.toBe('failed');
  });

  it('calls the session and the device endpoints', async () => {
    mockedApi.apiPost.mockResolvedValue(response(204));
    await signOut();
    await endAllSessions();
    expect(mockedApi.apiPost).toHaveBeenCalledWith('/api/auth/sign-out', undefined);
    expect(mockedApi.apiPost).toHaveBeenCalledWith('/api/auth/sessions/end-all', undefined);

    mockedApi.apiGet.mockResolvedValue(response(200, { passkeys: [{ id: '1' }] }));
    await expect(listPasskeys()).resolves.toEqual([{ id: '1' }]);

    mockedApi.apiPatch.mockResolvedValue(response(200, { id: '1', name: 'x' }));
    await expect(renamePasskey('1', 'x')).resolves.toEqual({ id: '1', name: 'x' });
    expect(mockedApi.apiPatch).toHaveBeenCalledWith('/api/auth/passkeys/1', { name: 'x' });

    mockedApi.apiDelete.mockResolvedValue(response(204));
    await expect(removePasskey('1')).resolves.toBeUndefined();
    mockedApi.apiDelete.mockResolvedValue(response(404));
    await expect(removePasskey('2')).rejects.toThrow();
  });
});
