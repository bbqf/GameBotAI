import type { ActionCreate, ActionUpdate } from '../../types/actions';
import { createAction, listActions, updateAction } from '../actionsApi';
import { getJson, patchJson, postJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    getJson: jest.fn(),
    postJson: jest.fn(),
    patchJson: jest.fn(),
    deleteJson: jest.fn()
  };
});

type GetJsonMock = typeof getJson;
const mockGetJson = getJson as jest.MockedFunction<GetJsonMock>;
const mockPostJson = postJson as jest.MockedFunction<typeof postJson>;
const mockPatchJson = patchJson as jest.MockedFunction<typeof patchJson>;

describe('actionsApi', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('filters actions by gameId and type', async () => {
    mockGetJson.mockResolvedValue([
      {
        id: 'a1',
        name: 'Connect',
        gameId: 'g1',
        steps: [{ type: 'connect-to-game', args: { adbSerial: 'emu-1' } }]
      },
      {
        id: 'a2',
        name: 'Tap',
        gameId: 'g2',
        steps: [{ type: 'tap', args: { x: 1 } }]
      }
    ] as any);

    const result = await listActions({ gameId: 'g1', type: 'connect-to-game' });

    expect(result).toHaveLength(1);
    expect(result[0].id).toBe('a1');
    expect(result[0].type).toBe('connect-to-game');
  });

  it('normalizes durationMs and connect-to-game attributes on create', async () => {
    const payload: ActionCreate = {
      name: 'Connect',
      gameId: 'g1',
      type: 'connect-to-game',
      attributes: { adbSerial: 'emu-1', durationMs: '1500' }
    };

    mockPostJson.mockResolvedValue({
      id: 'a1',
      name: 'Connect',
      gameId: 'g1',
      steps: [
        {
          type: 'connect-to-game',
          args: { adbSerial: 'emu-1', gameId: 'g1' },
          delayMs: 0,
          durationMs: 1500
        }
      ],
      checkpoints: []
    } as any);

    const result = await createAction(payload);

    expect(mockPostJson).toHaveBeenCalledWith('/api/actions', expect.objectContaining({
      name: 'Connect',
      gameId: 'g1',
      steps: [
        expect.objectContaining({
          type: 'connect-to-game',
          args: { adbSerial: 'emu-1', gameId: 'g1' },
          durationMs: 1500
        })
      ]
    }));
    expect(result.attributes).toEqual({ adbSerial: 'emu-1', gameId: 'g1', durationMs: 1500 });
  });

  it('updates actions with normalized attributes', async () => {
    const payload: ActionUpdate = {
      name: 'Tap',
      gameId: 'g1',
      type: 'tap',
      attributes: { x: 5, durationMs: 200 }
    };

    mockPatchJson.mockResolvedValue({
      id: 'a1',
      name: 'Tap',
      gameId: 'g1',
      steps: [{ type: 'tap', args: { x: 5 }, delayMs: 0, durationMs: 200 }],
      checkpoints: []
    } as any);

    const result = await updateAction('a1', payload);

    expect(mockPatchJson).toHaveBeenCalledWith('/api/actions/a1', expect.any(Object));
    expect(result.attributes).toEqual({ x: 5, durationMs: 200 });
  });
});
