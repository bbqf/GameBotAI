import { createSequence, executeSequence, updateSequence } from '../sequences';
import { postJson, putJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    postJson: jest.fn(),
    putJson: jest.fn(),
    patchJson: jest.fn(),
    getJson: jest.fn(),
    deleteJson: jest.fn()
  };
});

describe('sequences service', () => {
  const mockPostJson = postJson as jest.MockedFunction<typeof postJson>;
  const mockPutJson = putJson as jest.MockedFunction<typeof putJson>;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('creates sequences with wait-for-image primitive actions', async () => {
    mockPostJson.mockResolvedValue({ id: 'seq-wait', name: 'Wait Sequence', steps: [] } as any);

    const payload = {
      name: 'Wait Sequence',
      version: 1,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action' as const,
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              detectionTarget: {
                referenceImageId: 'mail_icon',
                confidence: 0.85,
              },
              timeoutMs: 2000,
            }
          },
          condition: null,
        }
      ],
      interStepDelayRangeMs: null,
    };

    await createSequence(payload);

    expect(mockPostJson).toHaveBeenCalledWith('/api/sequences', payload);
  });

  it('updates sequences with wait-for-image primitive actions', async () => {
    mockPutJson.mockResolvedValue({ id: 'seq-wait', name: 'Wait Sequence Updated', steps: [] } as any);

    const payload = {
      name: 'Wait Sequence Updated',
      version: 3,
      steps: [
        {
          stepId: 'step-1',
          stepType: 'Action' as const,
          primitiveAction: {
            type: 'WaitForImage',
            schemaVersion: 'v1',
            payload: {
              timeoutMs: 0,
            }
          },
          condition: null,
        }
      ],
      interStepDelayRangeMs: null,
    };

    await updateSequence('seq-wait', payload);

    expect(mockPutJson).toHaveBeenCalledWith('/api/sequences/seq-wait', payload);
  });

  it('returns wait-for-image execution outcomes from sequence execute responses', async () => {
    mockPostJson.mockResolvedValue({
      sequenceId: 'seq-wait',
      status: 'Succeeded',
      steps: [
        {
          commandId: 'wait-step',
          status: 'Succeeded',
          actionOutcome: 'image_unavailable'
        },
        {
          commandId: 'after-wait',
          status: 'Succeeded',
          actionOutcome: 'executed'
        }
      ]
    } as any);

    const result = await executeSequence('seq-wait');

    expect(mockPostJson).toHaveBeenCalledWith('/api/sequences/seq-wait/execute', {});
    expect(result.status).toBe('Succeeded');
    expect(result.steps[0]?.actionOutcome).toBe('image_unavailable');
    expect(result.steps[1]?.actionOutcome).toBe('executed');
  });
});