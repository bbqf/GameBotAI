import {
  applyTemplate,
  apiErrorCode,
  apiErrorHint,
  getQueueHealth,
  getTemplateDiff,
  pauseQueue,
  resumeQueue
} from '../queues';
import { ApiError, getJson, postJson } from '../../lib/api';

jest.mock('../../lib/api', () => {
  const actual = jest.requireActual('../../lib/api');
  return {
    ...actual,
    getJson: jest.fn(),
    postJson: jest.fn(),
    putJson: jest.fn(),
    deleteJson: jest.fn()
  };
});

describe('queues service: apply template changes (feature 171)', () => {
  const mockGetJson = getJson as jest.MockedFunction<typeof getJson>;
  const mockPostJson = postJson as jest.MockedFunction<typeof postJson>;

  beforeEach(() => jest.clearAllMocks());

  it('reads the differences from the template-diff endpoint', async () => {
    mockGetJson.mockResolvedValue({ queueId: 'q1', differences: [] } as never);

    await getTemplateDiff('q1');

    expect(mockGetJson).toHaveBeenCalledWith('/api/queues/q1/template-diff');
  });

  it('applies with the token and the selected keys', async () => {
    mockPostJson.mockResolvedValue({ applied: ['disabled:1'], planVersion: 1 } as never);

    const result = await applyTemplate('q1', 'sha256:abc', ['disabled:1']);

    expect(mockPostJson).toHaveBeenCalledWith('/api/queues/q1/apply-template', { baseToken: 'sha256:abc', selectedKeys: ['disabled:1'] });
    expect(result.planVersion).toBe(1);
  });

  it('asks for a pause and a resume through the queue endpoints', async () => {
    mockPostJson.mockResolvedValue({} as never);

    await pauseQueue('q1');
    await resumeQueue('q1');

    expect(mockPostJson).toHaveBeenNthCalledWith(1, '/api/queues/q1/pause', {});
    expect(mockPostJson).toHaveBeenNthCalledWith(2, '/api/queues/q1/resume', {});
  });

  it('reads the health block of one queue, and null when the queue does not run', async () => {
    mockGetJson.mockResolvedValueOnce({ id: 'q1', health: { paused: false, operatorPause: 'none' } } as never);
    mockGetJson.mockResolvedValueOnce({ id: 'q1', health: null } as never);
    mockGetJson.mockResolvedValueOnce({ id: 'q1' } as never);

    expect(await getQueueHealth('q1')).toEqual({ paused: false, operatorPause: 'none' });
    expect(await getQueueHealth('q1')).toBeNull();
    expect(await getQueueHealth('q1')).toBeNull();
    expect(mockGetJson).toHaveBeenCalledWith('/api/queues/q1');
  });

  it('reads the code and the hint of a refused call', () => {
    const err = new ApiError(409, 'The queue is not paused.', undefined, { error: { code: 'not_paused', message: 'The queue is not paused.', hint: 'Pause the queue first.' } });

    expect(apiErrorCode(err)).toBe('not_paused');
    expect(apiErrorHint(err)).toBe('Pause the queue first.');
  });

  it('gives null for an error with no code or hint', () => {
    expect(apiErrorCode(new Error('x'))).toBeNull();
    expect(apiErrorHint(new Error('x'))).toBeNull();
    expect(apiErrorCode(null)).toBeNull();
    expect(apiErrorHint(new ApiError(500, 'x', undefined, { error: { code: 'x', hint: '' } }))).toBeNull();
  });
});
