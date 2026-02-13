import { forceExecuteCommand } from '../commands';
import { postJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    postJson: jest.fn(),
    getJson: jest.fn(),
    patchJson: jest.fn(),
    deleteJson: jest.fn()
  };
});

describe('commands service', () => {
  const mockPostJson = postJson as jest.MockedFunction<typeof postJson>;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('executes commands without session id', async () => {
    mockPostJson.mockResolvedValue({ accepted: 1 } as any);

    await forceExecuteCommand('cmd-1');

    expect(mockPostJson).toHaveBeenCalledWith('/api/commands/cmd-1/force-execute', {});
  });

  it('executes commands with session id', async () => {
    mockPostJson.mockResolvedValue({ accepted: 1 } as any);

    await forceExecuteCommand('cmd-2', 'sess-1');

    expect(mockPostJson).toHaveBeenCalledWith('/api/commands/cmd-2/force-execute?sessionId=sess-1', {});
  });
});
