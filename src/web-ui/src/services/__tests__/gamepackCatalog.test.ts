import { apiPost } from '../../lib/api';
import { CatalogApiError, installFromCatalog } from '../gamepackCatalog';

jest.mock('../../lib/api', () => ({
  ...jest.requireActual('../../lib/api'),
  apiGet: jest.fn(),
  apiPost: jest.fn()
}));

const mockPost = apiPost as jest.MockedFunction<typeof apiPost>;

const response = (status: number, body: unknown) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

describe('installFromCatalog', () => {
  beforeEach(() => jest.resetAllMocks());

  it('sends name, version, dryRun and takeOver false', async () => {
    mockPost.mockResolvedValue(response(200, { dryRun: true, valid: true, created: 1, updated: 0, removed: 0 }));

    await installFromCatalog('alpha', '1.2.0', true, false);

    expect(mockPost).toHaveBeenCalledWith('/api/gamepacks/catalog/install', {
      name: 'alpha',
      version: '1.2.0',
      dryRun: true,
      takeOver: false
    });
  });

  it('sends takeOver true and keeps takenOver in the result', async () => {
    mockPost.mockResolvedValue(response(200, { dryRun: true, valid: true, created: 0, updated: 2, removed: 0, takenOver: 3 }));

    const result = await installFromCatalog('alpha', '1.2.0', true, true);

    expect(mockPost).toHaveBeenCalledWith('/api/gamepacks/catalog/install', {
      name: 'alpha',
      version: '1.2.0',
      dryRun: true,
      takeOver: true
    });
    expect(result.takenOver).toBe(3);
  });

  it('throws a CatalogApiError for HTTP 422', async () => {
    mockPost.mockResolvedValue(response(422, { result: 'signature_rejected', problems: [{ message: 'Bad signature.' }] }));

    await expect(installFromCatalog('alpha', '1.2.0', false, false)).rejects.toBeInstanceOf(CatalogApiError);
  });
});
