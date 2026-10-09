import { apiGet, apiPost } from '../../lib/api';
import { UpdateApiError, checkForUpdate, getUpdateStatus, installUpdate } from '../update';

jest.mock('../../lib/api', () => ({
  ...jest.requireActual('../../lib/api'),
  apiGet: jest.fn(),
  apiPost: jest.fn()
}));

const mockGet = apiGet as jest.MockedFunction<typeof apiGet>;
const mockPost = apiPost as jest.MockedFunction<typeof apiPost>;

const ok = (body: unknown, status = 200) => ({ ok: true, status, json: async () => body }) as unknown as Response;
const fail = (status: number, body?: unknown) =>
  ({ ok: false, status, json: async () => { if (body === undefined) throw new Error('no body'); return body; } }) as unknown as Response;

describe('update service', () => {
  beforeEach(() => jest.resetAllMocks());

  it('reads the status', async () => {
    mockGet.mockResolvedValue(ok({ installedVersion: '1.7.0.412', canInstallHere: true }));

    await expect(getUpdateStatus()).resolves.toMatchObject({ installedVersion: '1.7.0.412' });
    expect(mockGet).toHaveBeenCalledWith('/api/update/status');
  });

  it('runs a check', async () => {
    mockPost.mockResolvedValue(ok({ status: 'upToDate' }));

    await expect(checkForUpdate()).resolves.toEqual({ status: 'upToDate' });
    expect(mockPost).toHaveBeenCalledWith('/api/update/check', undefined);
  });

  it('sends the confirmation with the install request', async () => {
    mockPost.mockResolvedValue(ok({ attemptId: 'a', state: 'downloading' }, 202));

    await expect(installUpdate('1.7.0.430')).resolves.toEqual({ attemptId: 'a', state: 'downloading' });
    expect(mockPost).toHaveBeenCalledWith('/api/update/install', { targetVersion: '1.7.0.430', confirmStopQueues: true });
  });

  it('maps an error body to an UpdateApiError with code and hint', async () => {
    mockPost.mockResolvedValue(fail(403, { error: { code: 'update_local_only', message: 'Install from the bot PC.', hint: 'Open the UI on the PC.' } }));

    const error = await installUpdate('1.7.0.430').catch((e) => e);

    expect(error).toBeInstanceOf(UpdateApiError);
    expect(error).toMatchObject({ status: 403, code: 'update_local_only', message: 'Install from the bot PC.', hint: 'Open the UI on the PC.' });
  });

  it('uses a generic error when the body has no error object', async () => {
    mockGet.mockResolvedValue(fail(500));

    const error = await getUpdateStatus().catch((e) => e);

    expect(error).toMatchObject({ status: 500, code: 'http_500', message: 'HTTP 500' });
    expect(error.hint).toBeNull();
  });

  it('maps a failed check request', async () => {
    mockPost.mockResolvedValue(fail(409, { error: { code: 'update_in_progress', message: 'An update is in progress.' } }));

    await expect(checkForUpdate()).rejects.toMatchObject({ code: 'update_in_progress', hint: null });
  });
});
