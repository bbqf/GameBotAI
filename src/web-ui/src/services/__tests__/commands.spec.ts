import { createCommand, forceExecuteCommand, updateCommand } from '../commands';
import { patchJson, postJson } from '../../lib/api';

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
  const mockPatchJson = patchJson as jest.MockedFunction<typeof patchJson>;

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

  it('returns primitive step outcomes from execute responses', async () => {
    mockPostJson.mockResolvedValue({
      accepted: 1,
      stepOutcomes: [
        {
          stepOrder: 0,
          status: 'executed',
          resolvedPoint: { x: 10, y: 20 },
          detectionConfidence: 0.97
        }
      ]
    } as any);

    const result = await forceExecuteCommand('cmd-3');

    expect(result.accepted).toBe(1);
    expect(result.stepOutcomes?.[0]?.status).toBe('executed');
    expect(result.stepOutcomes?.[0]?.resolvedPoint).toEqual({ x: 10, y: 20 });
  });

  it('returns wait-for-image step outcomes from execute responses', async () => {
    mockPostJson.mockResolvedValue({
      accepted: 1,
      stepOutcomes: [
        {
          stepOrder: 0,
          stepType: 'waitForImage',
          status: 'completed_timeout',
          reason: 'timeout_elapsed',
          timeoutMs: 25,
          effectiveTimeoutMs: 25,
          referenceImageId: null,
          imageLoadStatus: 'loaded'
        }
      ]
    } as any);

    const result = await forceExecuteCommand('cmd-wait');

    expect(result.accepted).toBe(1);
    expect(result.stepOutcomes?.[0]?.stepType).toBe('waitForImage');
    expect(result.stepOutcomes?.[0]?.status).toBe('completed_timeout');
    expect(result.stepOutcomes?.[0]?.reason).toBe('timeout_elapsed');
    expect(result.stepOutcomes?.[0]?.effectiveTimeoutMs).toBe(25);
    expect(result.stepOutcomes?.[0]?.imageLoadStatus).toBe('loaded');
  });

  it('creates commands with wait-for-image steps', async () => {
    mockPostJson.mockResolvedValue({ id: 'cmd-wait', name: 'Wait Command' } as any);

    const payload = {
      name: 'Wait Command',
      steps: [
        {
          type: 'WaitForImage' as const,
          order: 0,
          waitForImage: {
            detectionTarget: {
              referenceImageId: 'mail_icon',
              confidence: 0.92,
            },
            timeoutMs: 1800,
          }
        }
      ]
    };

    await createCommand(payload);

    expect(mockPostJson).toHaveBeenCalledWith('/api/commands', payload);
  });

  it('updates commands with wait-for-image steps', async () => {
    mockPatchJson.mockResolvedValue({ id: 'cmd-wait', name: 'Wait Command Updated' } as any);

    const payload = {
      name: 'Wait Command Updated',
      steps: [
        {
          type: 'WaitForImage' as const,
          order: 0,
          waitForImage: {
            timeoutMs: 0,
          }
        }
      ]
    };

    await updateCommand('cmd-wait', payload);

    expect(mockPatchJson).toHaveBeenCalledWith('/api/commands/cmd-wait', payload);
  });
});
