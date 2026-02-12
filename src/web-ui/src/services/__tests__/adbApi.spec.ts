import { listAdbDevices } from '../adbApi';
import { getJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    getJson: jest.fn()
  };
});

describe('adbApi', () => {
  const mockGetJson = getJson as jest.MockedFunction<typeof getJson>;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('returns devices when api returns an array', async () => {
    mockGetJson.mockResolvedValue([{ serial: 'emu-1' }] as any);

    await expect(listAdbDevices()).resolves.toEqual([{ serial: 'emu-1' }]);
  });

  it('returns empty array when api returns non-array', async () => {
    mockGetJson.mockResolvedValue({} as any);

    await expect(listAdbDevices()).resolves.toEqual([]);
  });
});
