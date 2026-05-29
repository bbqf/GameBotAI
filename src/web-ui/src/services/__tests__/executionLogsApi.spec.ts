import { getJson } from '../../lib/api';
import { getExecutionLogDetail, listExecutionLogs } from '../executionLogsApi';

jest.mock('../../lib/api', () => ({
  getJson: jest.fn(),
}));

describe('executionLogsApi', () => {
  const getJsonMock = getJson as jest.MockedFunction<typeof getJson>;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('maps list response and nextPageToken fallback from nextCursor', async () => {
    getJsonMock.mockResolvedValueOnce({
      items: [{ id: 'id-1', timestampUtc: '2026-03-02T11:57:00.000Z', executionType: 'command', finalStatus: 'success', objectRef: { objectType: 'command', objectId: 'cmd-1', displayNameSnapshot: 'Cmd 1' }, summary: 'ok' }],
      nextCursor: 'cursor-1'
    } as any);

    const result = await listExecutionLogs({ sortBy: 'timestamp', sortDirection: 'desc', pageSize: 10 });

    expect(getJsonMock).toHaveBeenCalledWith('/api/execution-logs?sortBy=timestamp&sortDirection=desc&pageSize=10');
    expect(result.items).toHaveLength(1);
    expect(result.nextPageToken).toBe('cursor-1');
  });

  it('returns wait-for-image detail attributes from detail payload', async () => {
    getJsonMock.mockResolvedValueOnce({
      executionId: 'exe-1',
      summary: 'Wait step logged',
      relatedObjects: [],
      snapshot: { isAvailable: false },
      stepOutcomes: [
        {
          stepName: 'waitForImage',
          stepType: 'waitForImage',
          status: 'completed_timeout',
          message: 'timeout_elapsed',
          detailAttributes: {
            timeoutMs: 1500,
            effectiveTimeoutMs: 1500,
            referenceImageId: 'mail_icon',
            confidence: 0.92,
            exitCondition: 'timeout_elapsed',
            imageLoadStatus: 'loaded'
          }
        }
      ]
    } as any);

    const result = await getExecutionLogDetail('exe-1');

    expect(getJsonMock).toHaveBeenCalledWith('/api/execution-logs/exe-1');
    expect(result.stepOutcomes[0]?.detailAttributes?.timeoutMs).toBe(1500);
    expect(result.stepOutcomes[0]?.detailAttributes?.referenceImageId).toBe('mail_icon');
    expect(result.stepOutcomes[0]?.detailAttributes?.exitCondition).toBe('timeout_elapsed');
  });
});
